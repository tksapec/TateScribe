using System.IO;
using TateScribe.Core.Denden;
using TateScribe.Core.Export;
using TateScribe.Core.Proofreading;
using TateScribe.Core.Projects;
using TateScribe.Core.Ruby;
using TateScribe.Infrastructure.Storage;

namespace TateScribe.App.Services;

public sealed record DocumentExportPreparation(
    ExportDocument Document,
    int IncludedPageCount,
    int EmptyPageCount,
    int UnproofreadPageCount,
    IReadOnlyList<ProjectPage> OtherPagesWithText);

public sealed record StructuredDocumentPreparation(
    StructuredDocument Document,
    Guid? ExistingSnapshotId,
    DocumentExportPreparation LegacyPreparation,
    ExportPreflightResult Preflight);

public sealed record DendenDocumentPreparation(
    DendenExportDocument Document,
    Guid? ExistingSnapshotId,
    DocumentExportPreparation LegacyPreparation,
    ExportPreflightResult Preflight);

public sealed class DocumentExportService
{
    public async Task<DendenDocumentPreparation> PrepareDendenAsync(
        string projectDirectory,
        IReadOnlyList<ProjectPage> pages,
        bool includeIllustrations,
        CancellationToken cancellationToken)
    {
        var structured = await PrepareStructuredAsync(
            projectDirectory, pages, false, cancellationToken);
        var pageSortOrders = pages.ToDictionary(page => page.Id, page => page.SortOrder);
        var illustrations = includeIllustrations
            ? pages.Where(page => page.IsIncluded && page.PageRole == PageRole.Illustration)
                .OrderBy(page => page.SortOrder)
                .Select((page, index) => new DendenIllustration(
                    page.Id,
                    page.SortOrder,
                    page.SourcePath,
                    $"挿絵 {index + 1}",
                    Crop: page.Crop ?? TateScribe.Core.Images.NormalizedCrop.Full,
                    RotationDegrees: page.RotationDegrees))
                .ToArray()
            : [];
        var dendenDocument = DendenDocumentAssembler.Assemble(
            structured.Document,
            pageSortOrders,
            illustrations);
        return new DendenDocumentPreparation(
            dendenDocument,
            structured.ExistingSnapshotId,
            structured.LegacyPreparation,
            structured.Preflight with
            {
                IllustrationCount = illustrations.Length,
                Issues = structured.Preflight.Issues.Concat(
                    dendenDocument.Warnings
                        .Select(warning => new ExportPreflightIssue(
                            warning.Code,
                            warning.Message)))
                    .ToArray(),
            });
    }

    public async Task<StructuredDocumentPreparation> PrepareStructuredAsync(
        string projectDirectory,
        IReadOnlyList<ProjectPage> pages,
        bool pageBreakBeforeChapters,
        CancellationToken cancellationToken)
    {
        var legacy = await PrepareAsync(projectDirectory, pages, pageBreakBeforeChapters, cancellationToken);
        await using var repository = await SqliteProjectRepository.CreateAsync(projectDirectory, cancellationToken);
        var projectId = await repository.GetProjectIdAsync(cancellationToken);
        var sourcePages = pages.Where(page => page.IsIncluded
                && page.PageRole is not (PageRole.Illustration or PageRole.Blank))
            .OrderBy(page => page.SortOrder).ToArray();
        var sourceTexts = new List<ExportSourcePageText>();
        for (var pageIndex = 0; pageIndex < sourcePages.Length; pageIndex++)
        {
            var page = sourcePages[pageIndex];
            var state = await repository.LoadPageTextStateAsync(page.Id, cancellationToken);
            var selected = state.SelectForProofreading();
            if (string.IsNullOrWhiteSpace(selected.Text)
                || !DocumentPageSelection.IncludeInDocx(page.PageRole, selected.Text))
                continue;
            var text = page.PageRole == PageRole.ChapterTitle
                ? BookDocumentAssembler.CreateChapterPageText(selected.Text)
                : selected.Text;
            sourceTexts.Add(new ExportSourcePageText(
                page.Id,
                (pageIndex + 1).ToString("0000", System.Globalization.CultureInfo.InvariantCulture),
                text,
                page.BoundaryJoinType));
        }
        var assembled = BookDocumentAssembler.AssembleWithSourceSpans(sourceTexts);
        if (assembled.Count != legacy.Document.Paragraphs.Count
            || assembled.Where((item, index) =>
                    item.Paragraph != legacy.Document.Paragraphs[index])
                .Any())
            throw new InvalidDataException("Structured document assembly diverged from the standard DOCX assembly.");

        var localOrdinals = new Dictionary<Guid, int>();
        var paragraphs = new List<StructuredParagraph>();
        for (var ordinal = 0; ordinal < assembled.Count; ordinal++)
        {
            var item = assembled[ordinal].Paragraph;
            var spans = assembled[ordinal].SourceSpans;
            var source = spans.Count == 0 ? null : spans[0];
            var localOrdinal = source is null ? ordinal : localOrdinals.GetValueOrDefault(source.PageId);
            if (source is not null) localOrdinals[source.PageId] = localOrdinal + 1;
            var logicalKey = source is null
                ? $"document:{ordinal}:{item.Role}"
                : $"{source.PageId:D}:{localOrdinal}:{item.Role}";
            var paragraphId = await repository.FindStableParagraphIdAsync(projectId, logicalKey, cancellationToken)
                ?? Guid.NewGuid();
            IReadOnlyList<InlineElement> inlines = item.Ruby is null
                ? [new TextInline(item.Text)]
                : [new RubyInline(Guid.NewGuid(), item.Ruby.ParentText, item.Ruby.RubyText,
                    RubySource.UserConfirmed, 1)];
            paragraphs.Add(new StructuredParagraph(paragraphId, item.Role, inlines,
                DocumentTextHash.Compute(item.Text), spans, logicalKey));
        }
        var draft = new StructuredDocument(projectId, paragraphs, string.Empty);
        var document = draft with { DocumentTextHash = DocumentTextHash.Compute(draft) };
        var snapshotId = await repository.FindDocumentSnapshotAsync(projectId, document.DocumentTextHash, cancellationToken);
        var withConfirmedRuby = snapshotId is { } existingSnapshotId
            ? await repository.LoadStructuredDocumentAsync(projectId, existingSnapshotId, cancellationToken)
            : document;
        var preflight = await BuildPreflightAsync(
            repository,
            snapshotId,
            legacy,
            cancellationToken);
        return new StructuredDocumentPreparation(
            withConfirmedRuby,
            snapshotId,
            legacy,
            preflight);
    }

    public async Task<Guid> PersistAfterSuccessfulOutputAsync(
        string projectDirectory,
        StructuredDocument document,
        CancellationToken cancellationToken)
    {
        await using var repository = await SqliteProjectRepository.CreateAsync(projectDirectory, cancellationToken);
        return await repository.SaveDocumentSnapshotAsync(document, "SelectedConfirmedText", cancellationToken);
    }

    public async Task<DocumentExportPreparation> PrepareAsync(
        string projectDirectory,
        IReadOnlyList<ProjectPage> pages,
        bool pageBreakBeforeChapters,
        CancellationToken cancellationToken)
    {
        await using var repository = await SqliteProjectRepository.CreateAsync(projectDirectory, cancellationToken);
        var texts = new List<ExportPageText>();
        var empty = 0;
        var unproofread = 0;
        var other = new List<ProjectPage>();
        foreach (var page in pages.Where(page => page.IsIncluded).OrderBy(page => page.SortOrder))
        {
            if (page.PageRole is PageRole.Illustration or PageRole.Blank) continue;
            var state = await repository.LoadPageTextStateAsync(page.Id, cancellationToken);
            var selected = state.SelectForProofreading();
            if (state.ConfirmedText is null) unproofread++;
            if (string.IsNullOrWhiteSpace(selected.Text))
            {
                empty++;
                continue;
            }
            if (!DocumentPageSelection.IncludeInDocx(page.PageRole, selected.Text)) continue;
            if (page.PageRole == PageRole.Other) other.Add(page);
            var text = page.PageRole == PageRole.ChapterTitle
                ? BookDocumentAssembler.CreateChapterPageText(selected.Text)
                : selected.Text;
            texts.Add(new ExportPageText(text, page.BoundaryJoinType));
        }
        var document = BookDocumentAssembler.Assemble(texts) with { PageBreakBeforeChapters = pageBreakBeforeChapters };
        return new DocumentExportPreparation(document, texts.Count, empty, unproofread, other);
    }

    private static async Task<ExportPreflightResult> BuildPreflightAsync(
        SqliteProjectRepository repository,
        Guid? snapshotId,
        DocumentExportPreparation legacy,
        CancellationToken cancellationToken)
    {
        var rubyCounts = snapshotId is { } id
            ? await repository.GetRubyPreflightCountsAsync(id, cancellationToken)
            : new RubyPreflightCounts(0, 0, 0, 0, []);
        return new ExportPreflightResult(
            legacy.IncludedPageCount,
            legacy.UnproofreadPageCount,
            legacy.EmptyPageCount,
            legacy.OtherPagesWithText,
            rubyCounts.Confirmed,
            rubyCounts.Proposed,
            rubyCounts.Unresolved,
            rubyCounts.Stale,
            0,
            rubyCounts.Conflicts.Select(conflict => new ExportPreflightIssue(
                "RubyConflict", $"Conflicting ruby readings require review ({conflict}).")).ToArray());
    }
}
