using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using TateScribe.Core.Proofreading;

namespace TateScribe.App;

public partial class ProofreadingImportWindow : Window
{
    private readonly IReadOnlyList<ImportCandidateView> _candidates;
    private readonly ICollectionView _view;
    private readonly Action<Guid>? _showMainPage;

    public ProofreadingImportWindow(ProofreadingImportPreview preview, string details, Action<Guid>? showMainPage = null)
    {
        InitializeComponent();
        _showMainPage = showMainPage;
        _candidates = preview.Candidates.Select(candidate =>
        {
            var issues = preview.Issues.Where(issue => issue.PageMarker == candidate.PageMarker).ToArray();
            return new ImportCandidateView(candidate, issues);
        }).ToArray();
        CandidateGrid.ItemsSource = _candidates;
        _view = CollectionViewSource.GetDefaultView(_candidates);
        Summary.Text = $"{details}{Environment.NewLine}赤は取込み前、緑は取込み後です。ページごとに採用・保留を選択してください。エラーページは採用できません。";
    }

    public IReadOnlySet<string> AcceptedMarkers => _candidates
        .Where(candidate => candidate.IsAccepted && candidate.CanAccept)
        .Select(candidate => candidate.PageMarker)
        .ToHashSet(StringComparer.Ordinal);

    private void SelectAll(object sender, RoutedEventArgs e)
    {
        foreach (var candidate in _candidates.Where(candidate => candidate.CanAccept)) candidate.IsAccepted = true;
        CandidateGrid.Items.Refresh();
    }

    private void ClearAll(object sender, RoutedEventArgs e)
    {
        foreach (var candidate in _candidates) candidate.IsAccepted = false;
        CandidateGrid.Items.Refresh();
    }

    private void ToggleWarningsOnly(object sender, RoutedEventArgs e)
    {
        _view.Filter = WarningsOnly.IsChecked == true
            ? item => item is ImportCandidateView candidate && candidate.HasIssue
            : null;
        _view.Refresh();
    }

    private void ShowMainPage(object sender, RoutedEventArgs e)
    {
        if (CandidateGrid.SelectedItem is ImportCandidateView candidate)
            _showMainPage?.Invoke(candidate.PageId);
    }

    private void Accept(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed class ImportCandidateView
    {
        public ImportCandidateView(
            ProofreadingImportCandidate candidate,
            IReadOnlyList<ProofreadingImportIssue> issues)
        {
            PageId = candidate.PageId;
            PageMarker = candidate.PageMarker;
            BeforeText = candidate.BaselineText;
            AfterText = candidate.ConfirmedText;
            CanAccept = issues.All(issue => !issue.IsError);
            IsAccepted = CanAccept;
            HasIssue = issues.Count > 0;
            Status = issues.Count == 0
                ? "変更確認"
                : string.Join(" / ", issues.Select(issue => $"{(issue.IsError ? "エラー" : "警告")}:{issue.Code}"));
            var diff = candidate.Diff ?? ProofreadingDiff.Calculate(candidate.BaselineText, candidate.ConfirmedText);
            InlineSpans = diff.Spans.Select(ToInlineSpan).ToArray();
            DiffSummary = $"変更 {diff.ChangedCharacterCount}字 / {diff.ChangedParagraphCount}段落\n追加 {diff.AddedCharacterCount}・削除 {diff.DeletedCharacterCount}・置換 {diff.ReplacedCharacterCount}";
        }

        public bool IsAccepted { get; set; }
        public Guid PageId { get; }
        public bool CanAccept { get; }
        public bool HasIssue { get; }
        public string PageMarker { get; }
        public string BeforeText { get; }
        public string AfterText { get; }
        public string Status { get; }
        public string DiffSummary { get; }
        public IReadOnlyList<DiffSpanView> InlineSpans { get; }

        private static DiffSpanView ToInlineSpan(ProofreadingDiffSpan span) =>
            span.Kind switch
            {
                ProofreadingDiffKind.Equal => new DiffSpanView(span.AfterText, "Transparent", "変更なし"),
                ProofreadingDiffKind.Added => new DiffSpanView($"＋{span.AfterText}", "#FFC8F7C5", "追加"),
                ProofreadingDiffKind.Deleted => new DiffSpanView($"－{span.BeforeText}", "#FFFFC7C7", "削除"),
                ProofreadingDiffKind.Changed => new DiffSpanView(
                    $"{span.BeforeText} → {span.AfterText}", "#FFFFE8A3", "置換"),
                _ => throw new ArgumentOutOfRangeException(nameof(span.Kind))
            };
    }

    private sealed record DiffSpanView(string Text, string Background, string ToolTip);
}
