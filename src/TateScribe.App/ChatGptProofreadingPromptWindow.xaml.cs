using System.Windows;
using TateScribe.Core.Proofreading;

namespace TateScribe.App;

public partial class ChatGptProofreadingPromptWindow : Window
{
    public ChatGptProofreadingPromptWindow()
    {
        InitializeComponent();
        PromptEditor.Text = FinalProofreadingPrompt.Text;
    }

    private void CopyPrompt(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PromptEditor.Text))
        {
            CopyStatus.Text = "コピーする指示文を入力してください。";
            return;
        }

        try
        {
            Clipboard.SetText(PromptEditor.Text);
            CopyStatus.Text = "指示文をクリップボードにコピーしました。";
        }
        catch (Exception exception)
        {
            CopyStatus.Text = "クリップボードへコピーできませんでした。";
            MessageBox.Show(this, exception.Message, "コピーに失敗しました", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
}
