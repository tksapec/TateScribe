using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TateScribe.Core.Ruby;

namespace TateScribe.App;

public partial class RubyReviewWindow : Window
{
    private readonly RubyImportResultSource source;
    private readonly Action<string>? showPage;
    private readonly Func<RubyImportDocument, RubyImportPreview>? validateReviewed;
    private readonly IReadOnlyList<RubyOcrCandidate> ocrCandidates;
    private readonly ObservableCollection<RubyAnnotationView> annotations;
    private readonly int unresolvedCount;
    private RubyAnnotationView? focusedAnnotation;

    public RubyReviewWindow(
        StructuredDocument document,
        RubyImportPreview preview,
        IReadOnlyList<RubyOcrCandidate>? ocrCandidates = null,
        Action<string>? showPage = null,
        Func<RubyImportDocument, RubyImportPreview>? validateReviewed = null)
    {
        InitializeComponent();
        if (preview.Result is null) throw new ArgumentException("ルビ候補がありません。", nameof(preview));
        source = new RubyImportResultSource(document, preview.Result, preview.Issues);
        unresolvedCount = preview.Result.Unresolved.Count;
        this.ocrCandidates = ocrCandidates ?? [];
        this.showPage = showPage;
        this.validateReviewed = validateReviewed;
        var readings = preview.Result.Annotations
            .GroupBy(item => item.BaseText, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => group.Select(item => item.Reading).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        annotations = new ObservableCollection<RubyAnnotationView>(preview.Result.Annotations.Select(item =>
        {
            var candidateIssues = preview.Issues
                .Where(issue => RubyBulkConfirmationPolicy.Matches(issue, item))
                .ToArray();
            var readingSummary = readings[item.BaseText].Length > 1
                ? $"同じ表記の読み: {string.Join(" / ", readings[item.BaseText])}"
                : $"同じ表記の読み: {readings[item.BaseText][0]}";
            var warning = string.Join(
                Environment.NewLine,
                candidateIssues.Select(issue => issue.Message).Prepend(readingSummary));
            return new RubyAnnotationView(
                item,
                warning,
                candidateIssues,
                document.Paragraphs.SingleOrDefault(paragraph =>
                    string.Equals(
                        paragraph.ParagraphId.ToString("D"),
                        item.ParagraphId,
                        StringComparison.OrdinalIgnoreCase))?.PlainText ?? string.Empty);
        }));
        AnnotationGrid.ItemsSource = annotations;
        UnresolvedGrid.ItemsSource = preview.Result.Unresolved.Select(item => new
        {
            item.BaseText,
            item.Start,
            item.Length,
            PageMarkers = string.Join(", ", item.EvidencePageMarkers),
            item.Reason,
        }).ToArray();
        UpdateSummary();
        AnnotationGrid.SelectedIndex = annotations.Count > 0 ? 0 : -1;
    }

    public RubyImportDocument ReviewedDocument => source.Import with
    {
        Annotations = annotations.Select(view => view.ToSnapshot()).ToArray(),
    };

    private void AnnotationSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = AnnotationGrid.CurrentItem as RubyAnnotationView;
        if (selected is null || !AnnotationGrid.SelectedItems.Contains(selected))
            selected = e.AddedItems.OfType<RubyAnnotationView>().LastOrDefault()
                ?? AnnotationGrid.SelectedItems.OfType<RubyAnnotationView>().FirstOrDefault();
        if (selected is null)
        {
            focusedAnnotation = null;
            ParagraphText.Clear();
            EvidenceText.Clear();
            UpdateSummary();
            return;
        }
        focusedAnnotation = selected;
        var paragraph = source.Document.Paragraphs.SingleOrDefault(item =>
            string.Equals(item.ParagraphId.ToString("D"), selected.ParagraphId, StringComparison.OrdinalIgnoreCase));
        ParagraphText.Text = paragraph?.PlainText ?? string.Empty;
        if (selected.Start >= 0
            && selected.Length >= 1
            && (long)selected.Start + selected.Length <= ParagraphText.Text.Length)
        {
            ParagraphText.Select(selected.Start, selected.Length);
        }
        var coordinates = ocrCandidates.Where(candidate =>
                selected.EvidencePageMarkers.Contains(candidate.PageMarker, StringComparer.Ordinal)
                && string.Equals(
                    RubyTextNormalizer.NormalizeReading(candidate.ReadingCandidate),
                    RubyTextNormalizer.NormalizeReading(selected.Reading),
                    StringComparison.Ordinal)
                && (candidate.BaseTextCandidate is null
                    || string.Equals(
                        candidate.BaseTextCandidate,
                        selected.BaseText,
                        StringComparison.Ordinal)))
            .OrderByDescending(candidate => candidate.LinkConfidence ?? 0)
            .Select(candidate =>
                $"OCR座標: {candidate.PageMarker} ({candidate.Left:0.##}, {candidate.Top:0.##})-({candidate.Right:0.##}, {candidate.Bottom:0.##}) OCR信頼度 {candidate.Confidence:0.00} リンク信頼度 {(candidate.LinkConfidence?.ToString("0.00") ?? "未特定")} 親文字候補 {candidate.BaseTextCandidate ?? "未特定"}");
        EvidenceText.Text = $"根拠: {selected.Evidence}{Environment.NewLine}ページ: {selected.PageMarkers}{Environment.NewLine}{string.Join(Environment.NewLine, coordinates)}{Environment.NewLine}{selected.Warning}";
        UpdateSummary();
    }

    private void AcceptSelected(object sender, RoutedEventArgs e) =>
        SetSelected(RubyAnnotationStatus.Confirmed);
    private void RejectSelected(object sender, RoutedEventArgs e) => SetSelected(RubyAnnotationStatus.Rejected);

    private void ConfirmImageBased(object sender, RoutedEventArgs e) =>
        ConfirmSource(RubySource.ImageConfirmed);

    private void ConfirmTextBased(object sender, RoutedEventArgs e) =>
        ConfirmSource(RubySource.TextConfirmed);

    private void ConfirmSource(RubySource source)
    {
        if (RubyReviewPendingEditBoundary.TryRun(CommitPendingEdits, () =>
                ConfirmSourceAfterPendingEdits(source)))
            return;
        ShowPendingEditBulkFailure(source);
    }

    private void ConfirmSourceAfterPendingEdits(RubySource source)
    {
        var current = ReviewedDocument;
        var validation = validateReviewed?.Invoke(current)
            ?? new RubyImportPreview(current, annotations.SelectMany(item => item.Issues).ToArray());
        RefreshValidationIssues(validation.Issues);
        var validationErrors = validation.Issues.Where(issue => issue.IsError).ToArray();
        var summary = RubyBulkConfirmationSummary.Create(
            current.Annotations,
            source,
            proposal => validation.Issues
                .Where(issue => RubyBulkConfirmationPolicy.Matches(issue, proposal))
                .ToArray(),
            validationErrors);
        if (validation.IsValid)
        {
            foreach (var item in annotations)
            {
                var proposal = item.ToSnapshot();
                if (RubyBulkConfirmationPolicy.CanConfirm(proposal, source, item.Issues))
                    item.ApplyProposal(proposal with { Status = RubyAnnotationStatus.Confirmed });
            }
        }
        AnnotationGrid.Items.Refresh();
        UpdateSummary();
        MessageBox.Show(
            this,
            FormatBulkOutcome(source, summary),
            "Ruby bulk confirmation",
            MessageBoxButton.OK,
            validationErrors.Length > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private void ShowEvidencePage(object sender, RoutedEventArgs e)
    {
        if (focusedAnnotation is RubyAnnotationView selected
            && selected.EvidencePageMarkers.Count > 0)
            showPage?.Invoke(selected.EvidencePageMarkers[0]);
    }

    private void SetSelected(RubyAnnotationStatus status)
    {
        if (RubyReviewPendingEditBoundary.TryRun(CommitPendingEdits, () =>
                SetSelectedAfterPendingEdits(status)))
            return;
        ShowPendingEditValidationFailure();
    }

    private void SetSelectedAfterPendingEdits(RubyAnnotationStatus status)
    {
        var selectedViews = AnnotationGrid.SelectedItems.OfType<RubyAnnotationView>().ToArray();
        var result = RubyReviewSelectionService.ApplyStatus(
            selectedViews.Select(item => item.ToSnapshot()).ToArray(),
            status,
            ParagraphTextFor);
        if (!result.IsSuccess)
        {
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, result.Errors.Select(error => $"- {error.Message} ({error.Key})")),
                "Ruby validation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            UpdateSummary();
            return;
        }
        if (result.SelectedCount == 0)
        {
            MessageBox.Show(this, "対象のルビ候補を1件以上選択してください。", "TateScribe",
                MessageBoxButton.OK, MessageBoxImage.Information);
            UpdateSummary();
            return;
        }
        for (var index = 0; index < selectedViews.Length; index++)
            selectedViews[index].ApplyProposal(result.Items[index]);
        AnnotationGrid.Items.Refresh();
        UpdateSummary();
        var changedLabel = status == RubyAnnotationStatus.Confirmed ? "新規確定" : "新規却下";
        var existingLabel = status == RubyAnnotationStatus.Confirmed ? "確定済み" : "却下済み";
        MessageBox.Show(this,
            $"選択: {result.SelectedCount}件{Environment.NewLine}{changedLabel}: {result.ChangedCount}件{Environment.NewLine}{existingLabel}: {result.AlreadyInTargetStatusCount}件",
            "TateScribe", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AnnotationGridPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        SetSelected(RubyAnnotationStatus.Confirmed);
        e.Handled = true;
    }

    private bool CommitPendingEdits() =>
        RubyReviewPendingEditBoundary.TryCommit(
            () => AnnotationGrid.CommitEdit(DataGridEditingUnit.Cell, true),
            () => AnnotationGrid.CommitEdit(DataGridEditingUnit.Row, true));

    private void ShowPendingEditValidationFailure() =>
        MessageBox.Show(
            this,
            "保留中の編集を確定できませんでした。入力値を確認してください。",
            "Ruby validation",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

    private void ShowPendingEditBulkFailure(RubySource source) =>
        MessageBox.Show(
            this,
            (source == RubySource.ImageConfirmed ? "画像根拠" : "本文根拠")
            + "の一括確定は、保留中の編集を確定できないため中止しました。入力値を確認してください。",
            "Ruby bulk confirmation",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

    private string? ParagraphTextFor(string paragraphId) =>
        source.Document.Paragraphs.SingleOrDefault(item =>
            string.Equals(
                item.ParagraphId.ToString("D"),
                paragraphId,
                StringComparison.OrdinalIgnoreCase))?.PlainText;

    private void UpdateSummary()
    {
        SummaryText.Text = RubyReviewSummaryFormatter.Format(
            annotations.Select(item => item.ToSnapshot()).ToArray(),
            unresolvedCount,
            AnnotationGrid.SelectedItems.Count);
    }

    private static string FormatBulkOutcome(
        RubySource source,
        RubyBulkConfirmationSummary summary)
    {
        var lines = new List<string>
        {
            source == RubySource.ImageConfirmed ? "画像根拠の一括確定" : "本文根拠の一括確定",
            $"確認対象: {summary.Examined} 件",
            $"新規確定: {summary.NewlyConfirmed} 件",
            $"確定済み: {summary.AlreadyConfirmed} 件",
            $"根拠種別違い: {summary.WrongSource} 件",
            $"除外: {summary.Excluded} 件",
        };
        if (summary.Examined == 0)
            lines.Add("対象候補はありませんでした。");
        if (summary.ExcludedByReason.Count > 0)
        {
            lines.Add("除外理由:");
            lines.AddRange(summary.ExcludedByReason
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => $"- {item.Key}: {item.Value} 件"));
        }
        if (summary.ValidationErrors.Count > 0)
        {
            lines.Add("検証エラーのため確定処理は行われませんでした:");
            lines.AddRange(summary.ValidationErrors.Select(issue => $"- {issue.Code}: {issue.Message}"));
        }
        return string.Join(Environment.NewLine, lines);
    }

    private void SaveReview(object sender, RoutedEventArgs e)
    {
        if (RubyReviewPendingEditBoundary.TryRun(CommitPendingEdits, SaveReviewAfterPendingEdits))
            return;
        ShowPendingEditValidationFailure();
    }

    private void SaveReviewAfterPendingEdits()
    {
        try
        {
            var current = ReviewedDocument;
            var validation = validateReviewed?.Invoke(current)
                ?? new RubyImportPreview(
                    current,
                    annotations.SelectMany(item => item.Issues).ToArray());
            RefreshValidationIssues(validation.Issues);
            AnnotationGrid.Items.Refresh();
            var errors = validation.Issues.Where(issue => issue.IsError).ToArray();
            if (errors.Length > 0)
            {
                MessageBox.Show(
                    this,
                    string.Join(Environment.NewLine, errors.Select(issue => $"- {issue.Message}")),
                    "Ruby validation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            if (HasNewConfirmedWarnings())
            {
                var warnings = annotations
                    .Where(item =>
                        item.Status == RubyAnnotationStatus.Confirmed
                        && item.HasNewWarnings)
                    .SelectMany(item => item.Issues.Where(issue => !issue.IsError))
                    .Select(issue => $"- {issue.Message}")
                    .Distinct(StringComparer.Ordinal);
                var acknowledged = MessageBox.Show(
                    this,
                    "Confirmed candidates have new warnings."
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, warnings)
                    + Environment.NewLine
                    + "Save them as Confirmed after reviewing these warnings?",
                    "Ruby warning acknowledgement",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (acknowledged != MessageBoxResult.Yes) return;
            }
            DialogResult = true;
        }
        catch (ArgumentOutOfRangeException)
        {
            MessageBox.Show(
                this,
                "開始位置と長さが本文の範囲外です。本文表示を確認して修正してください。",
                "ルビ範囲を確認",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RefreshValidationIssues(IReadOnlyList<RubyValidationIssue> issues)
    {
        foreach (var item in annotations)
        {
            var proposal = item.ToSnapshot();
            item.UpdateIssues(
                issues
                    .Where(issue => RubyBulkConfirmationPolicy.Matches(issue, proposal))
                    .ToArray());
        }
        UpdateSummary();
    }

    private bool HasNewConfirmedWarnings() =>
        annotations.Any(item =>
            item.Status == RubyAnnotationStatus.Confirmed
            && item.HasNewWarnings);

    private void CancelReview(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed record RubyImportResultSource(
        StructuredDocument Document,
        RubyImportDocument Import,
        IReadOnlyList<RubyValidationIssue> Issues);

    private sealed class RubyAnnotationView
    {
        private readonly RubyAnnotationProposal original;
        private readonly string paragraphText;
        private readonly HashSet<string> initialWarningKeys;
        public RubyAnnotationView(
            RubyAnnotationProposal original,
            string warning,
            IReadOnlyList<RubyValidationIssue> issues,
            string paragraphText)
        {
            this.original = original;
            this.paragraphText = paragraphText;
            ParagraphId = original.ParagraphId;
            Start = original.Start;
            Length = original.Length;
            Reading = original.Reading;
            Source = original.Source;
            Confidence = original.Confidence;
            EvidencePageMarkers = original.EvidencePageMarkers;
            Evidence = original.Evidence;
            Status = original.Status;
            Warning = warning;
            Issues = issues;
            initialWarningKeys = issues
                .Where(issue => !issue.IsError)
                .Select(WarningKey)
                .ToHashSet(StringComparer.Ordinal);
        }
        public string ParagraphId { get; }
        public int Start { get; set; }
        public int Length { get; set; }
        public string BaseText =>
            Start >= 0 && Length >= 1 && (long)Start + Length <= paragraphText.Length
                ? paragraphText.Substring(Start, Length)
                : original.BaseText;
        public string Reading { get; set; }
        public RubySource Source { get; }
        public double Confidence { get; }
        public IReadOnlyList<string> EvidencePageMarkers { get; }
        public string PageMarkers => string.Join(", ", EvidencePageMarkers);
        public string Evidence { get; }
        public RubyAnnotationStatus Status { get; set; }
        public string Warning { get; private set; }
        public IReadOnlyList<RubyValidationIssue> Issues { get; private set; }
        public bool HasNewWarnings => Issues
            .Where(issue => !issue.IsError)
            .Select(WarningKey)
            .Any(key => !initialWarningKeys.Contains(key));
        public void UpdateIssues(IReadOnlyList<RubyValidationIssue> issues)
        {
            Issues = issues;
            Warning = string.Join(Environment.NewLine, issues.Select(issue => issue.Message));
        }
        public void ApplyProposal(RubyAnnotationProposal proposal)
        {
            Status = proposal.Status;
        }
        public RubyAnnotationProposal ToSnapshot() => original with
        {
            Start = Start,
            Length = Length,
            BaseText = BaseText,
            Reading = Reading,
            Status = Status,
        };

        private static string WarningKey(RubyValidationIssue issue) =>
            $"{issue.Code}\0{issue.Message}";
    }
}
