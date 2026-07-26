using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TateScribe.Core.Ocr;
using TateScribe.Core.Projects;
using TateScribe.Core.Images;
using TateScribe.Infrastructure.Images;
using TateScribe.Infrastructure.Storage;

namespace TateScribe.App;

public partial class PageReviewWindow : Window
{
    private readonly string _projectDirectory;
    private readonly ProjectPage _page;
    private IReadOnlyList<OcrWordReviewView> _wordViews = [];

    public PageReviewWindow(string projectDirectory, ProjectPage page)
    {
        InitializeComponent();
        _projectDirectory = projectDirectory;
        _page = page;
        Loaded += LoadData;
    }

    private async void LoadData(object sender, RoutedEventArgs e)
    {
        await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
        HistoryGrid.ItemsSource = await repository.LoadPageTextVersionsAsync(_page.Id, CancellationToken.None);
        OcrFailureGrid.ItemsSource = await repository.LoadOcrFailuresAsync(_page.Id, CancellationToken.None);
        _wordViews = (await repository.LoadLatestOcrWordStatesAsync(_page.Id, CancellationToken.None))
            .Select(word => new OcrWordReviewView(word)).ToArray();
        RubyGrid.ItemsSource = _wordViews;
        if (File.Exists(_page.SourcePath))
        {
            var prepared = await new ScreenshotPreprocessor().PrepareAsync(
                _page.SourcePath, System.IO.Path.Combine(_projectDirectory, ".tatescribe-cache"),
                _page.Crop ?? NormalizedCrop.Full, _page.RotationDegrees, CancellationToken.None);
            RubyPreview.Source = new BitmapImage(new Uri(prepared.CachePath, UriKind.Absolute));
        }
        DrawRubyBoxes();
    }

    private async void RestoreSelectedVersion(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not PageTextVersion version) return;
        await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
        await repository.RestoreTextVersionAsync(version, CancellationToken.None);
        HistoryGrid.ItemsSource = await repository.LoadPageTextVersionsAsync(_page.Id, CancellationToken.None);
        MessageBox.Show(this, $"{version.Kind}版を復元しました。復元操作も新しい履歴として保存されます。", "TateScribe");
    }

    private async void SaveRubyReview(object sender, RoutedEventArgs e)
    {
        RubyGrid.CommitEdit();
        try
        {
            await using var repository = await SqliteProjectRepository.CreateAsync(_projectDirectory, CancellationToken.None);
            foreach (var view in _wordViews.Where(view => view.IsChanged))
                await repository.UpdateOcrWordReviewAsync(
                    _page.Id, view.RunId, view.Ordinal, view.Role, view.IncludedInDraft, CancellationToken.None);
            MessageBox.Show(this, "ルビ分類と下書きへの採否を保存しました。", "TateScribe");
            DrawRubyBoxes();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, "分類は Body または RubyCandidate を指定してください。\n" + exception.Message,
                "ルビ分類を保存できません", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RubyPreviewSizeChanged(object sender, SizeChangedEventArgs e) => DrawRubyBoxes();

    private void DrawRubyBoxes()
    {
        RubyOverlay.Children.Clear();
        if (RubyPreview.Source is not BitmapSource bitmap || RubyPreview.ActualWidth <= 0 || RubyPreview.ActualHeight <= 0) return;
        RubyOverlay.Width = RubyPreview.ActualWidth;
        RubyOverlay.Height = RubyPreview.ActualHeight;
        var scaleX = RubyPreview.ActualWidth / bitmap.PixelWidth;
        var scaleY = RubyPreview.ActualHeight / bitmap.PixelHeight;
        foreach (var view in _wordViews.Where(word => word.Role == "RubyCandidate"))
        {
            var rectangle = new Rectangle
            {
                Width = Math.Max(2, (view.Word.Right - view.Word.Left) * scaleX),
                Height = Math.Max(2, (view.Word.Bottom - view.Word.Top) * scaleY),
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(32, 255, 0, 0))
            };
            Canvas.SetLeft(rectangle, view.Word.Left * scaleX);
            Canvas.SetTop(rectangle, view.Word.Top * scaleY);
            RubyOverlay.Children.Add(rectangle);
        }
    }

    private void CloseWindow(object sender, RoutedEventArgs e) => Close();

    private sealed class OcrWordReviewView(OcrWordReviewState state)
    {
        public Guid RunId { get; } = state.RunId;
        public int Ordinal { get; } = state.Ordinal;
        public OcrWord Word { get; } = state.Word;
        public string Text => Word.Text;
        public string Role { get; set; } = state.Role;
        public bool IncludedInDraft { get; set; } = state.IncludedInDraft;
        public bool IsChanged => Role != state.Role || IncludedInDraft != state.IncludedInDraft;
        public string Coordinates => $"{Word.Left:0},{Word.Top:0}-{Word.Right:0},{Word.Bottom:0}";
    }
}
