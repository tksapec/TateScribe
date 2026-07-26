using TateScribe.Core.Layout;
using TateScribe.Core.Ocr;
using TateScribe.Core.Projects;
using TateScribe.Infrastructure.Storage;

namespace TateScribe.Tests;

public sealed class NonDestructiveOcrTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TateScribeTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Propose_keeps_paddle_words_and_anchors_supplementary_punctuation()
    {
        var words = new[]
        {
            new OcrWord("私は", .91, 10, 20, 20, 40),
            new OcrWord("学生です", .92, 10, 42, 20, 72)
        };

        var proposal = PunctuationMerger.Propose("私は学生です", "私は、学生です。", words, 16);

        Assert.Equal("私は、学生です。", proposal.SuggestedText);
        Assert.All(proposal.Operations, operation => Assert.NotNull(operation.AnchorWordOrdinal));
        Assert.Equal(10, words[0].Left);
        Assert.Equal("学生です", words[1].Text);
    }

    [Fact]
    public void Propose_marks_unaligned_auxiliary_text_as_unanchored_without_changing_raw_text()
    {
        var proposal = PunctuationMerger.Propose("甲", "乙", [], 16);

        Assert.Equal("甲", proposal.SuggestedText);
        Assert.Contains(proposal.ReviewItems, item => item.Code == "UnanchoredSuggestion");
        Assert.Contains(proposal.Operations, operation => operation.AnchorWordOrdinal is null && operation.ProposedText == "乙");
    }

    [Fact]
    public async Task Saving_a_merge_proposal_preserves_raw_paddle_words_and_auxiliary_text()
    {
        Directory.CreateDirectory(_directory);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        var page = new ProjectPage(Guid.NewGuid(), "page.png", "C:\\page.png", "hash", 0, true, 0);
        await repository.SavePagesAsync([page], CancellationToken.None);
        var paddle = new OcrPageResult("paddle-request", "paddle", "model-a", [new OcrWord("私は", .8, 11, 12, 13, 14), new OcrWord("学生です", .8, 15, 16, 17, 18)]);
        var proposal = PunctuationMerger.Propose("私は学生です", "私は、学生です。", paddle.Words, 16);

        await repository.SaveOcrAnalysisAsync(page.Id, paddle, "私は、学生です。", proposal, CancellationToken.None);
        var state = await repository.LoadPageTextStateAsync(page.Id, CancellationToken.None);

        var word = state.RawPaddleWords[0];
        Assert.Equal(2, state.RawPaddleWords.Count);
        Assert.Equal((11d, 12d, 13d, 14d), (word.Left, word.Top, word.Right, word.Bottom));
        Assert.Equal("私は、学生です。", state.RawTesseractText);
        Assert.Equal("私は、学生です。", state.SuggestedText);
        Assert.Null(state.ManualText);
        Assert.Null(state.ConfirmedText);
        var runs = await repository.LoadOcrRunsAsync(page.Id, CancellationToken.None);
        var run = Assert.Single(runs, run => run.Engine == "paddle");
        Assert.Equal(("paddle", "model-a"), (run.Engine, run.ModelVersion));
        Assert.Contains(runs, run => run.Engine == "tesseract" && run.ModelVersion == "jpn_vert");
    }

    [Fact]
    public void Propose_with_reading_order_words_persists_the_raw_paddle_ordinal_as_its_anchor()
    {
        var rawWords = new[]
        {
            new OcrWord("A", .9, 0, 20, 10, 40),
            new OcrWord("B", .9, 100, 20, 110, 40),
            new OcrWord("C", .9, 100, 42, 110, 62),
            new OcrWord("D", .9, 100, 64, 110, 84)
        };
        var orderedWords = VerticalTextReconstruction.OrderWordsForReadingWithRawOrdinals(rawWords, 20);

        var proposal = PunctuationMerger.ProposeWithRawWordOrdinals("BCDA", "BC\u3001DA", orderedWords, 16);

        Assert.Equal([1, 2, 3, 0], orderedWords.Select(word => word.RawOrdinal).ToArray());
        Assert.Contains(proposal.Operations, operation => operation.ProposedText == "\u3001" && operation.AnchorWordOrdinal == 3);
    }

    public void Dispose()
    {
        TestFileCleanup.DeleteDirectory(_directory);
    }
}
