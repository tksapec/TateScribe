using System.Windows;
using TateScribe.Core.Proofreading;

namespace TateScribe.App;

public partial class ProofreadingImportWindow : Window
{
    private readonly IReadOnlyList<ImportCandidateView> _candidates;

    public ProofreadingImportWindow(ProofreadingImportPreview preview, string details)
    {
        InitializeComponent();
        _candidates = preview.Candidates.Select(candidate => new ImportCandidateView(candidate)).ToArray();
        CandidateGrid.ItemsSource = _candidates;
        Summary.Text = $"{details}{Environment.NewLine}ページごとに採用・保留を選択してください。保留ページの既存本文は変更しません。";
    }

    public IReadOnlySet<string> AcceptedMarkers => _candidates.Where(candidate => candidate.IsAccepted)
        .Select(candidate => candidate.PageMarker).ToHashSet(StringComparer.Ordinal);

    private void Accept(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed class ImportCandidateView(ProofreadingImportCandidate candidate)
    {
        public bool IsAccepted { get; set; } = true;

        public string PageMarker { get; } = candidate.PageMarker;

        public string Preview { get; } = candidate.ConfirmedText.Length <= 180 ? candidate.ConfirmedText : $"{candidate.ConfirmedText[..180]}…";
    }
}
