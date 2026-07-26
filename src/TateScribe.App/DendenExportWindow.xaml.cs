using Microsoft.Win32;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using TateScribe.Core.Denden;

namespace TateScribe.App;

public partial class DendenExportWindow : Window
{
    public DendenExportWindow(string defaultTitle)
    {
        InitializeComponent();
        TitleEditor.Text = defaultTitle;
    }

    public DendenExportOptions? Options { get; private set; }
    public string? CoverPath => string.IsNullOrWhiteSpace(CoverPathEditor.Text) ? null : CoverPathEditor.Text;

    private void ChooseCover(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "JPEG画像|*.jpg;*.jpeg" };
        if (dialog.ShowDialog(this) == true) CoverPathEditor.Text = dialog.FileName;
    }

    private void Accept(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleEditor.Text) || string.IsNullOrWhiteSpace(CreatorEditor.Text)
            || !int.TryParse(TocDepthEditor.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var depth)
            || depth is < 1 or > 6
            || !int.TryParse(TcyDigitsEditor.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var digits)
            || digits is < 1 or > 4)
        {
            MessageBox.Show(this, "書名・著者を入力し、目次深度は1～6、縦中横の桁数は1～4で指定してください。",
                "入力内容を確認してください", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var vertical = DirectionSelector.SelectedItem is ComboBoxItem { Tag: "rtl" };
        Options = new DendenExportOptions(
            TitleEditor.Text.Trim(), CreatorEditor.Text.Trim(), LanguageEditor.Text.Trim(),
            vertical, TitlePageCheck.IsChecked == true, TocCheck.IsChecked == true,
            depth, AutoTcyCheck.IsChecked == true, digits, SplitChapterCheck.IsChecked == true);
        DialogResult = true;
    }

    private void Cancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
