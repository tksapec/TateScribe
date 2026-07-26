using TateScribe.Core.Export;
using TateScribe.Core.Proofreading;
using TateScribe.Core.Projects;
using TateScribe.Infrastructure.Storage;

namespace TateScribe.App.Services;

public sealed record DocumentExportPreparation(
    ExportDocument Document,
    int IncludedPageCount,
    int EmptyPageCount,
    int UnproofreadPageCount,
    IReadOnlyList<ProjectPage> OtherPagesWithText);

public sealed class DocumentExportService
{
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
}
