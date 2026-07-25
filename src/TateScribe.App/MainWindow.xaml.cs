using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace TateScribe.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void ImportImages(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.webp"
        };
        if (dialog.ShowDialog(this) == true)
        {
            PageList.ItemsSource = dialog.FileNames.Select(Path.GetFileName).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
}
