using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TateScribe.Core.Ruby;

namespace TateScribe.App;

public partial class RubyBatchHistoryWindow : Window
{
    public RubyBatchHistoryWindow(IReadOnlyList<RubyBatchHistoryItem> history)
    {
        InitializeComponent();
        BatchGrid.ItemsSource = history;
        BatchGrid.SelectedItem =
            history.FirstOrDefault(item => item.AnnotationCount > 0);
        OpenButton.IsEnabled = SelectedBatch is not null;
    }

    public RubyBatchHistoryItem? SelectedBatch =>
        BatchGrid.SelectedItem is RubyBatchHistoryItem { AnnotationCount: > 0 } item
            ? item
            : null;

    private void BatchSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OpenButton.IsEnabled = SelectedBatch is not null;
    }

    private void OpenSelectedBatch(object sender, RoutedEventArgs e)
    {
        if (SelectedBatch is null) return;
        DialogResult = true;
    }

    private void OpenSelectedBatch(object sender, MouseButtonEventArgs e)
    {
        if (SelectedBatch is null) return;
        DialogResult = true;
    }
}
