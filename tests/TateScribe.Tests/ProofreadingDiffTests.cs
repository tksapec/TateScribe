using TateScribe.Core.Proofreading;

namespace TateScribe.Tests;

public sealed class ProofreadingDiffTests
{
    [Fact]
    public void Calculate_distinguishes_replacement_insertion_and_deletion()
    {
        var diff = ProofreadingDiff.Calculate("甲乙丙丁", "甲X乙丁追");

        Assert.Contains(diff.Spans, span => span.Kind == ProofreadingDiffKind.Added && span.AfterText.Contains('X'));
        Assert.Contains(diff.Spans, span => span.Kind == ProofreadingDiffKind.Deleted && span.BeforeText.Contains('丙'));
        Assert.True(diff.ChangedCharacterCount >= 2);
    }

    [Fact]
    public void Calculate_counts_changed_paragraphs()
    {
        var diff = ProofreadingDiff.Calculate("第一段落\n第二段落\n第三段落", "第一段落\n変更段落\n第三段落\n追加段落");

        Assert.Equal(2, diff.ChangedParagraphCount);
        Assert.True(diff.ChangedCharacterCount > 0);
    }

    [Fact]
    public void Calculate_groups_adjacent_delete_and_insert_as_a_replacement()
    {
        var diff = ProofreadingDiff.Calculate("甲乙", "甲X");

        var changed = Assert.Single(diff.Spans, span => span.Kind == ProofreadingDiffKind.Changed);
        Assert.Equal(("乙", "X"), (changed.BeforeText, changed.AfterText));
        Assert.Equal(1, diff.ReplacedCharacterCount);
    }

    [Fact]
    public void Calculate_uses_a_bounded_diff_for_large_page_text()
    {
        var before = new string('甲', 10_000);
        var after = new string('乙', 10_000);

        var diff = ProofreadingDiff.Calculate(before, after);

        var changed = Assert.Single(diff.Spans);
        Assert.Equal(ProofreadingDiffKind.Changed, changed.Kind);
        Assert.Equal(10_000, diff.ReplacedCharacterCount);
    }
}
