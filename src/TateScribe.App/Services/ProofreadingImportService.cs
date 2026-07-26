using System.IO;
using System.IO.Compression;
using TateScribe.Core.Proofreading;
using TateScribe.Infrastructure.Storage;

namespace TateScribe.App.Services;

public sealed class ProofreadingImportService
{
    public async Task<ProofreadingImportPreview> PrepareAsync(
        string projectDirectory,
        string path,
        CancellationToken cancellationToken)
    {
        var content = await ReadContentAsync(path, cancellationToken);
        var document = ProofreadingImportParser.Parse(content);
        await using var repository = await SqliteProjectRepository.CreateAsync(projectDirectory, cancellationToken);
        return await repository.PrepareConfirmedImportAsync(document, cancellationToken);
    }

    public async Task SaveAsync(
        string projectDirectory,
        ProofreadingImportPreview preview,
        IReadOnlySet<string> acceptedMarkers,
        CancellationToken cancellationToken)
    {
        await using var repository = await SqliteProjectRepository.CreateAsync(projectDirectory, cancellationToken);
        await repository.SaveConfirmedTextAsync(preview, acceptedMarkers, cancellationToken);
    }

    public static string BuildDetails(ProofreadingImportPreview preview)
    {
        var lines = preview.Issues.Select(issue =>
            $"{(issue.IsError ? "エラー" : "警告")}: {issue.Code} {issue.PageMarker ?? string.Empty}").ToList();
        lines.AddRange(preview.Candidates.Select(candidate =>
        {
            var diff = candidate.Diff ?? ProofreadingDiff.Calculate(candidate.BaselineText, candidate.ConfirmedText);
            return $"PAGE {candidate.PageMarker}: {candidate.BaselineText.Length} → {candidate.ConfirmedText.Length}文字、変更 {diff.ChangedCharacterCount}字 / {diff.ChangedParagraphCount}段落";
        }));
        return string.Join(Environment.NewLine, lines);
    }

    private static async Task<string> ReadContentAsync(string path, CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
            return await File.ReadAllTextAsync(path, cancellationToken);
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.Entries.SingleOrDefault(entry => string.Equals(entry.FullName, "proofread.txt", StringComparison.OrdinalIgnoreCase))
            ?? archive.Entries.SingleOrDefault(entry => string.Equals(entry.FullName, "proofread.md", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("ZIP内に proofread.txt または proofread.md がありません。");
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
