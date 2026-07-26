using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using TateScribe.Core.Export;

namespace TateScribe.Core.Ruby;

public enum RubyPolicy
{
    PreserveOriginalOnly,
    OriginalAndTextConfirmed,
    SuggestDifficultReadings,
}

public enum RubySource
{
    ImageConfirmed,
    TextConfirmed,
    UserConfirmed,
    DictionarySuggested,
    ContextSuggested,
}

public enum RubyAnnotationStatus
{
    Proposed,
    Confirmed,
    Rejected,
    Stale,
}

public sealed record SourceSpan(Guid PageId, string PageMarker, int Start, int Length);

public abstract record InlineElement;

public sealed record TextInline(string Text) : InlineElement;

public sealed record RubyInline(
    Guid AnnotationId,
    string BaseText,
    string Reading,
    RubySource Source,
    double Confidence) : InlineElement;

public sealed record StructuredParagraph(
    Guid ParagraphId,
    DocumentElementRole Role,
    IReadOnlyList<InlineElement> Inlines,
    string TextHash,
    IReadOnlyList<SourceSpan> SourceSpans,
    string LogicalKey = "")
{
    public string PlainText => string.Concat(Inlines.Select(item => item switch
    {
        TextInline text => text.Text,
        RubyInline ruby => ruby.BaseText,
        _ => string.Empty,
    }));
}

public sealed record StructuredDocument(
    Guid ProjectId,
    IReadOnlyList<StructuredParagraph> Paragraphs,
    string DocumentTextHash);

public sealed record RubyAnnotationProposal(
    [property: JsonRequired] string ParagraphId,
    [property: JsonRequired] int Start,
    [property: JsonRequired] int Length,
    [property: JsonRequired] string BaseText,
    [property: JsonRequired] string Reading,
    [property: JsonRequired] RubySource Source,
    [property: JsonRequired] double Confidence,
    [property: JsonRequired] IReadOnlyList<string> EvidencePageMarkers,
    [property: JsonRequired] string Evidence,
    [property: System.Text.Json.Serialization.JsonIgnore] Guid AnnotationId = default,
    [property: System.Text.Json.Serialization.JsonIgnore] RubyAnnotationStatus Status = RubyAnnotationStatus.Proposed);

public sealed record RubyUnresolvedItem(
    [property: JsonRequired] string ParagraphId,
    [property: JsonRequired] int Start,
    [property: JsonRequired] int Length,
    [property: JsonRequired] string BaseText,
    [property: JsonRequired] IReadOnlyList<string> EvidencePageMarkers,
    [property: JsonRequired] string Reason);

public sealed record RubyImportDocument(
    [property: JsonRequired] int FormatVersion,
    [property: JsonRequired] Guid ProjectId,
    [property: JsonRequired] Guid BatchId,
    [property: JsonRequired] string DocumentTextHash,
    [property: JsonRequired] IReadOnlyList<RubyAnnotationProposal> Annotations,
    [property: JsonRequired] IReadOnlyList<RubyUnresolvedItem> Unresolved);

public sealed record RubyValidationIssue(string Code, string Message, bool IsError);

public sealed record RubyValidationContext(
    Guid ProjectId,
    Guid BatchId,
    StructuredDocument Document,
    IReadOnlySet<string> BatchPageMarkers,
    RubyPolicy Policy = RubyPolicy.PreserveOriginalOnly,
    bool ConfirmedTextIsStale = false,
    IReadOnlyList<RubyOcrCandidate>? OcrCandidates = null);

public sealed record RubyImportPreview(
    RubyImportDocument? Result,
    IReadOnlyList<RubyValidationIssue> Issues)
{
    public bool IsValid => Result is not null && Issues.All(issue => !issue.IsError);
}

public sealed record RubyBatchSnapshot(
    Guid BatchId,
    RubyPolicy Policy,
    Guid SnapshotId,
    StructuredDocument Document,
    IReadOnlySet<string> PageMarkers,
    IReadOnlyDictionary<string, Guid> PageIdsByMarker,
    bool ConfirmedTextIsStale,
    IReadOnlyList<RubyOcrCandidate> OcrCandidates);

public static class DocumentTextHash
{
    public static string Compute(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static string Compute(StructuredDocument document) =>
        Compute(string.Join("\n", document.Paragraphs.Select(paragraph =>
            string.Join(
                "\u001f",
                paragraph.Role,
                paragraph.PlainText,
                paragraph.LogicalKey,
                string.Join(
                    "\u001e",
                    paragraph.SourceSpans.Select(span =>
                        $"{span.PageId:D}:{span.PageMarker}:{span.Start}:{span.Length}"))))));
}

public static class RubyDocumentComposer
{
    public static StructuredParagraph Apply(
        StructuredParagraph paragraph,
        IEnumerable<RubyAnnotationProposal> annotations)
    {
        var confirmed = annotations
            .Where(item => item.Status == RubyAnnotationStatus.Confirmed)
            .OrderBy(item => item.Start)
            .ThenBy(item => item.Length)
            .ToArray();
        if (confirmed.Length == 0) return paragraph;

        var inlines = new List<InlineElement>();
        var position = 0;
        foreach (var annotation in confirmed)
        {
            if (annotation.Start < position
                || annotation.Start + annotation.Length > paragraph.PlainText.Length
                || !string.Equals(
                    paragraph.PlainText.Substring(annotation.Start, annotation.Length),
                    annotation.BaseText,
                    StringComparison.Ordinal))
                throw new InvalidOperationException("Confirmed ruby annotations must be non-overlapping and match the paragraph text.");
            if (annotation.Start > position)
                inlines.Add(new TextInline(paragraph.PlainText[position..annotation.Start]));
            inlines.Add(new RubyInline(
                annotation.AnnotationId, annotation.BaseText, annotation.Reading,
                annotation.Source, annotation.Confidence));
            position = annotation.Start + annotation.Length;
        }
        if (position < paragraph.PlainText.Length)
            inlines.Add(new TextInline(paragraph.PlainText[position..]));
        return paragraph with { Inlines = inlines };
    }
}
