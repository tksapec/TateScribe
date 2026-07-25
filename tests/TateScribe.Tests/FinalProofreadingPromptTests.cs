using TateScribe.Core.Proofreading;

namespace TateScribe.Tests;

public sealed class FinalProofreadingPromptTests
{
    [Fact]
    public void Text_requests_correction_as_a_docx_without_guessing_uncertain_passages()
    {
        var text = FinalProofreadingPrompt.Text;

        Assert.Contains("OCR", text, StringComparison.Ordinal);
        Assert.Contains("誤字・脱字・文字化け", text, StringComparison.Ordinal);
        Assert.Contains("推測で確定せず", text, StringComparison.Ordinal);
        Assert.Contains("ユーザーに確認", text, StringComparison.Ordinal);
        Assert.Contains("修正を反映したDOCXファイル", text, StringComparison.Ordinal);
    }
}
