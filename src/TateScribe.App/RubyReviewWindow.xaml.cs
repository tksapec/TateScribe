using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using TateScribe.Core.Ruby;

namespace TateScribe.App;

public partial class RubyReviewWindow : Window
{
    private readonly RubyImportResultSource source;
    private readonly Action<string>? showPage;
    private readonly Func<RubyImportDocument, RubyImportPreview>? validateReviewed;
    private readonly IReadOnlyList<RubyOcrCandidate> ocrCandidates;
    private readonly ObservableCollection<RubyAnnotationView> annotations;

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
        SummaryText.Text = $"候補 {annotations.Count} 件、未確定 {preview.Result.Unresolved.Count} 件。本文は読み取り専用です。個別確認後に保存してください。";
        AnnotationGrid.SelectedIndex = annotations.Count > 0 ? 0 : -1;
    }

    public RubyImportDocument ReviewedDocument => source.Import with
    {
        Annotations = annotations.Select(view => view.ToProposal()).ToArray(),
    };

    private void AnnotationSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AnnotationGrid.SelectedItem is not RubyAnnotationView selected) return;
        var paragraph = source.Document.Paragraphs.SingleOrDefault(item =>
            string.Equals(item.ParagraphId.ToString("D"), selected.ParagraphId, StringComparison.OrdinalIgnoreCase));
        ParagraphText.Text = paragraph?.PlainText ?? string.Empty;
        if (selected.Start >= 0
            && selected.Length >= 1
            && (long)selected.Start + selected.Length <= ParagraphText.Text.Length)
        {
            ParagraphText.Focus();
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
    }

    private void AcceptSelected(object sender, RoutedEventArgs e) => SetSelected(RubyAnnotationStatus.Confirmed);
    private void RejectSelected(object sender, RoutedEventArgs e) => SetSelected(RubyAnnotationStatus.Rejected);

    private void ConfirmImageBased(object sender, RoutedEventArgs e) =>
        ConfirmSource(RubySource.ImageConfirmed);

    private void ConfirmTextBased(object sender, RoutedEventArgs e) =>
        ConfirmSource(RubySource.TextConfirmed);

    private void ConfirmSource(RubySource source)
    {
        AnnotationGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var current = ReviewedDocument;
        var validation = validateReviewed?.Invoke(current)
            ?? new RubyImportPreview(current, annotations.SelectMany(item => item.Issues).ToArray());
        if (!validation.IsValid) return;
        foreach (var item in annotations)
        {
            var proposal = item.ToProposal();
            var issues = validation.Issues
                .Where(issue => RubyBulkConfirmationPolicy.Matches(issue, proposal))
                .ToArray();
            item.UpdateIssues(issues);
            if (RubyBulkConfirmationPolicy.CanConfirm(proposal, source, issues))
                item.Status = RubyAnnotationStatus.Confirmed;
        }
        AnnotationGrid.Items.Refresh();
    }

    private void ShowEvidencePage(object sender, RoutedEventArgs e)
    {
        if (AnnotationGrid.SelectedItem is RubyAnnotationView selected
            && selected.EvidencePageMarkers.Count > 0)
            showPage?.Invoke(selected.EvidencePageMarkers[0]);
    }

    private void SetSelected(RubyAnnotationStatus status)
    {
        if (AnnotationGrid.SelectedItem is not RubyAnnotationView selected) return;
        selected.Status = status;
        AnnotationGrid.Items.Refresh();
    }

    private void SaveReview(object sender, RoutedEventArgs e)
    {
        AnnotationGrid.CommitEdit(DataGridEditingUnit.Row, true);
        try
        {
            _ = annotations.Select(view => view.ToProposal()).ToArray();
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

    private void CancelReview(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed record RubyImportResultSource(
        StructuredDocument Document,
        RubyImportDocument Import,
        IReadOnlyList<RubyValidationIssue> Issues);

    private sealed class RubyAnnotationView
    {
        private readonly RubyAnnotationProposal original;
        private readonly string paragraphText;
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
        public void UpdateIssues(IReadOnlyList<RubyValidationIssue> issues)
        {
            Issues = issues;
            Warning = string.Join(Environment.NewLine, issues.Select(issue => issue.Message));
        }
        public RubyAnnotationProposal ToProposal()
        {
            if (Start < 0 || Length < 1 || (long)Start + Length > paragraphText.Length)
                throw new ArgumentOutOfRangeException(nameof(Start));
            return original with
            {
                Start = Start,
                Length = Length,
                BaseText = paragraphText.Substring(Start, Length),
                Reading = Reading,
                Status = Status,
            };
        }
    }
}
