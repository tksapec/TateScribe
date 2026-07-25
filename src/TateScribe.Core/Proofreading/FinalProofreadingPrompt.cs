namespace TateScribe.Core.Proofreading;

public static class FinalProofreadingPrompt
{
    public const string Text = """
        添付したDOCXファイルは、書籍の縦書き画面をOCRで読み取って作成したものです。
        誤字・脱字・文字化け・句読点の誤り・文脈上明らかな欠落がある可能性があるため、全文を校正し、適切に修正してください。
        原文の意味・文体・固有名詞を不用意に変更しないでください。
        原文画像がなく判断できない箇所や、複数の解釈が可能な箇所は推測で確定せず、ユーザーに確認してください。
        校正結果は、修正を反映したDOCXファイルとして返してください。
        """;
}
