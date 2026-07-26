using System.Windows;
using System.Windows.Controls;
using TateScribe.Core.ChatGpt;

namespace TateScribe.App;

public partial class ChatGptProofreadingPromptWindow : Window
{
    private readonly IChatGptPromptTemplateProvider promptTemplates = new ChatGptPromptTemplateProvider();

    public ChatGptProofreadingPromptWindow()
    {
        InitializeComponent();
        ResetToSelectedTemplate();
    }

    private ChatGptTaskType SelectedTaskType =>
        TaskTypeSelector.SelectedItem is ComboBoxItem { Tag: string tag }
        && Enum.TryParse<ChatGptTaskType>(tag, out var taskType)
            ? taskType
            : ChatGptTaskType.TextProofreading;

    private void TaskTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized) return;
        ResetToSelectedTemplate();
    }

    private void ResetPrompt(object sender, RoutedEventArgs e) => ResetToSelectedTemplate();

    private void ResetToSelectedTemplate()
    {
        var taskType = SelectedTaskType;
        PromptEditor.Text = promptTemplates.GetTemplate(taskType);
        TaskDescription.Text = taskType == ChatGptTaskType.TextProofreading
            ? "校正用パッケージと一緒にChatGPTへ渡します。返却された構造化テキストはTateScribeへ取り込みます。"
            : "ルビ確認用パッケージと一緒にChatGPTへ渡します。ChatGPTは本文を変更せず、ルビ候補のJSONだけを返します。";
        CopyStatus.Text = string.Empty;
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
