using System.Text;
using TateScribe.Core.Projects;

namespace TateScribe.Core.Export;

public sealed record ExportPreflightIssue(
    string Code,
    string Message,
    bool IsFatal = false);

public sealed record ExportPreflightResult(
    int IncludedPageCount,
    int UnproofreadPageCount,
    int EmptyPageCount,
    IReadOnlyList<ProjectPage> OtherPagesWithText,
    int ConfirmedRubyCount,
    int ProposedRubyCount,
    int UnresolvedRubyCount,
    int StaleRubyCount,
    int IllustrationCount,
    IReadOnlyList<ExportPreflightIssue> Issues)
{
    public bool HasFatalErrors => Issues.Any(issue => issue.IsFatal);

    public bool RequiresConfirmation =>
        UnproofreadPageCount > 0
        || OtherPagesWithText.Count > 0
        || EmptyPageCount > 0
        || ProposedRubyCount > 0
        || UnresolvedRubyCount > 0
        || StaleRubyCount > 0
        || IllustrationCount > 0
        || Issues.Count > 0;

    public string FormatConfirmation(string destinationLabel)
    {
        var result = new StringBuilder();
        result.Append(destinationLabel).AppendLine("へ次を出力します。").AppendLine();
        result.Append("本文ページ: ").AppendLine(IncludedPageCount.ToString());
        result.Append("未校正ページ: ").AppendLine(UnproofreadPageCount.ToString());
        result.Append("本文が空のページ: ").AppendLine(EmptyPageCount.ToString());
        result.Append("PageRole=Otherの本文ページ: ").AppendLine(OtherPagesWithText.Count.ToString());
        result.Append("確定ルビ: ").AppendLine(ConfirmedRubyCount.ToString());
        result.Append("未確定ルビ: ").AppendLine(UnresolvedRubyCount.ToString());
        result.Append("Proposedルビ: ").AppendLine(ProposedRubyCount.ToString());
        result.Append("Staleルビ: ").AppendLine(StaleRubyCount.ToString());
        result.Append("挿絵: ").AppendLine(IllustrationCount.ToString());
        if (Issues.Count > 0)
        {
            result.AppendLine();
            foreach (var issue in Issues)
                result.Append("・").AppendLine(issue.Message);
        }
        result.AppendLine();
        result.AppendLine("未確定、Proposed、Staleのルビは出力されません。");
        result.Append("続行しますか？");
        return result.ToString();
    }
}
