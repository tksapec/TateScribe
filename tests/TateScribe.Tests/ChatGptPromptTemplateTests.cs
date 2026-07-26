using TateScribe.Core.ChatGpt;
using TateScribe.Core.Proofreading;

namespace TateScribe.Tests;

public sealed class ChatGptPromptTemplateTests
{
    private readonly ChatGptPromptTemplateProvider provider = new();

    [Fact]
    public void Text_proofreading_requires_format_2_and_forbids_finished_files_and_ruby()
    {
        var text = provider.GetTemplate(ChatGptTaskType.TextProofreading);

        Assert.Contains("[[TATESCRIBE_FORMAT:2]]", text, StringComparison.Ordinal);
        Assert.Contains("構造化テキストだけ", text, StringComparison.Ordinal);
        Assert.Contains("ルビを本文へ追加しない", text, StringComparison.Ordinal);
        Assert.Contains("［判読不能: PAGE-xxxx］", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DOCXファイルとして返", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Ruby_annotation_requires_json_only_and_unchanged_body()
    {
        var text = provider.GetTemplate(ChatGptTaskType.RubyAnnotation);

        Assert.Contains("JSONだけ", text, StringComparison.Ordinal);
        Assert.Contains("本文の文字、句読点、空白、改行、段落、章・節構造を一切変更しない", text, StringComparison.Ordinal);
        Assert.Contains("UTF-16コード単位", text, StringComparison.Ordinal);
        Assert.Contains("unresolved", text, StringComparison.Ordinal);
        Assert.Contains("rubyPolicy", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_prompt_uses_the_central_text_proofreading_template()
    {
        Assert.Equal(provider.GetTemplate(ChatGptTaskType.TextProofreading), FinalProofreadingPrompt.Text);
    }
}
