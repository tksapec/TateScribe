using TateScribe.Core.Ocr;
using TateScribe.Core.Projects;
using TateScribe.Infrastructure.Storage;

namespace TateScribe.Tests;

public sealed class ManualEditProtectionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TateScribeTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Reocr_replaces_machine_evidence_but_preserves_manual_text()
    {
        Directory.CreateDirectory(_directory);
        var page = new ProjectPage(Guid.NewGuid(), "page.png", "C:\\source\\page.png", "hash", 0, true, 0);
        await using var repository = await SqliteProjectRepository.CreateAsync(_directory, CancellationToken.None);
        await repository.SavePagesAsync([page], CancellationToken.None);
        await repository.ReplaceOcrWordsAsync(page.Id, "paddle", "model-a", [new OcrWord("誤字", 0.4, 1, 2, 3, 4)], CancellationToken.None);
        await repository.SaveManualTextAsync(page.Id, "正字", CancellationToken.None);

        await repository.ReplaceOcrWordsAsync(page.Id, "paddle", "model-b", [new OcrWord("再認識", 0.9, 1, 2, 3, 4)], CancellationToken.None);
        var state = await repository.LoadPageTextStateAsync(page.Id, CancellationToken.None);

        Assert.Equal("正字", state.ManualText);
        Assert.Equal("再認識", Assert.Single(state.MachineWords).Text);
        Assert.Equal("model-b", state.ModelVersion);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
