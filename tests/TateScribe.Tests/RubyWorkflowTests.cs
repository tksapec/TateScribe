using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using TateScribe.Core.Export;
using TateScribe.Core.Ruby;

namespace TateScribe.Tests;

public sealed class RubyWorkflowTests
{
    [Fact]
    public void Composer_supports_multiple_rubies_without_changing_plain_text()
    {
        var paragraph = Paragraph("八角と万二");
        var annotations = new[]
        {
            Proposal(paragraph, 0, 2, "八角", "やすみ") with { Status = RubyAnnotationStatus.Confirmed },
            Proposal(paragraph, 3, 2, "万二", "まんじ") with { Status = RubyAnnotationStatus.Confirmed },
        };

        var actual = RubyDocumentComposer.Apply(paragraph, annotations);

        Assert.Equal("八角と万二", actual.PlainText);
        Assert.Collection(actual.Inlines,
            item => Assert.IsType<RubyInline>(item),
            item => Assert.Equal("と", Assert.IsType<TextInline>(item).Text),
            item => Assert.IsType<RubyInline>(item));
    }

    [Fact]
    public void Composer_ignores_proposed_rejected_and_stale_annotations()
    {
        var paragraph = Paragraph("八角");
        var proposals = new[]
        {
            Proposal(paragraph, 0, 2, "八角", "やすみ"),
            Proposal(paragraph, 0, 2, "八角", "はっかく") with { Status = RubyAnnotationStatus.Rejected },
            Proposal(paragraph, 0, 2, "八角", "やすみ") with { Status = RubyAnnotationStatus.Stale },
        };

        var actual = RubyDocumentComposer.Apply(paragraph, proposals);

        Assert.Equal("八角", actual.PlainText);
        Assert.DoesNotContain(actual.Inlines, inline => inline is RubyInline);
    }

    [Fact]
    public void Validator_accepts_exact_utf16_ranges_and_reports_suggestions()
    {
        var paragraph = Paragraph("𠮷野家と八角");
        var document = Document(paragraph);
        var proposal = Proposal(paragraph, 5, 2, "八角", "やすみ") with
        {
            Source = RubySource.ContextSuggested,
            Confidence = 0.6,
        };

        var preview = new RubyImportValidator().Validate(Json(document, proposal), Context(document));

        Assert.True(preview.IsValid);
        Assert.Contains(preview.Issues, issue => issue.Code == "SuggestedReading" && !issue.IsError);
        Assert.Contains(preview.Issues, issue => issue.Code == "LowConfidence" && !issue.IsError);
    }

    [Theory]
    [InlineData("wrong-project")]
    [InlineData("wrong-hash")]
    [InlineData("overlap")]
    [InlineData("split-surrogate")]
    [InlineData("unknown-page")]
    [InlineData("stale")]
    public void Validator_rejects_invalid_or_stale_results(string scenario)
    {
        var paragraph = Paragraph("𠮷野八角");
        var document = Document(paragraph);
        var first = Proposal(paragraph, scenario == "split-surrogate" ? 1 : 3, 2, scenario == "split-surrogate" ? "?" : "八角", "やすみ");
        var annotations = scenario == "overlap"
            ? new[] { first, first with { AnnotationId = Guid.NewGuid(), Start = 4, Length = 1, BaseText = "角" } }
            : new[] { first };
        var payload = new
        {
            formatVersion = 1,
            projectId = scenario == "wrong-project" ? Guid.NewGuid() : document.ProjectId,
            batchId = BatchId,
            documentTextHash = scenario == "wrong-hash" ? "BAD" : document.DocumentTextHash,
            annotations = annotations.Select(ToJson),
            unresolved = Array.Empty<object>(),
        };
        var context = Context(document) with
        {
            BatchPageMarkers = scenario == "unknown-page" ? new HashSet<string>() : new HashSet<string> { "0001" },
            ConfirmedTextIsStale = scenario == "stale",
        };

        var preview = new RubyImportValidator().Validate(
            JsonSerializer.Serialize(payload, JsonOptions), context);

        Assert.False(preview.IsValid);
        Assert.Contains(preview.Issues, issue => issue.IsError);
    }

    [Fact]
    public void Preserve_original_policy_rejects_text_or_dictionary_only_sources()
    {
        var paragraph = Paragraph("八角");
        var document = Document(paragraph);
        var proposal = Proposal(paragraph, 0, 2, "八角", "やすみ") with
        {
            Source = RubySource.TextConfirmed,
        };
        var context = Context(document) with { Policy = RubyPolicy.PreserveOriginalOnly };

        var preview = new RubyImportValidator().Validate(Json(document, proposal), context);

        Assert.False(preview.IsValid);
        Assert.Contains(preview.Issues, issue => issue.Code == "RubyPolicy" && issue.IsError);
    }

    [Theory]
    [InlineData("batch")]
    [InlineData("paragraph")]
    [InlineData("range")]
    [InlineData("base")]
    [InlineData("reading")]
    [InlineData("source")]
    [InlineData("duplicate")]
    public void Validator_rejects_each_required_identity_and_annotation_error(string scenario)
    {
        var paragraph = Paragraph("八角");
        var document = Document(paragraph);
        var valid = Json(document, Proposal(paragraph, 0, 2, "八角", "やすみ"));
        var annotationsStart = valid.IndexOf('[', valid.IndexOf("\"annotations\"", StringComparison.Ordinal)) + 1;
        var annotationsEnd = valid.IndexOf("],\"unresolved\"", StringComparison.Ordinal);
        var annotationJson = valid[annotationsStart..annotationsEnd];
        var invalid = scenario switch
        {
            "batch" => valid.Replace(BatchId.ToString("D"), Guid.NewGuid().ToString("D"), StringComparison.Ordinal),
            "paragraph" => valid.Replace(paragraph.ParagraphId.ToString("D"), Guid.NewGuid().ToString("D"), StringComparison.Ordinal),
            "range" => valid.Replace("\"length\":2", "\"length\":3", StringComparison.Ordinal),
            "base" => valid.Replace("\"baseText\":\"八角\"", "\"baseText\":\"八隅\"", StringComparison.Ordinal),
            "reading" => valid.Replace("\"reading\":\"やすみ\"", "\"reading\":\" \"", StringComparison.Ordinal),
            "source" => valid.Replace("\"source\":\"ImageConfirmed\"", "\"source\":\"Unsupported\"", StringComparison.Ordinal),
            "duplicate" => valid[..annotationsStart] + annotationJson + "," + annotationJson + valid[annotationsEnd..],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        var preview = new RubyImportValidator().Validate(invalid, Context(document));

        Assert.False(preview.IsValid);
        var expectedCode = scenario switch
        {
            "batch" => "BatchId",
            "paragraph" => "ParagraphId",
            "range" => "Range",
            "base" => "BaseText",
            "reading" => "Reading",
            "source" => "InvalidJson",
            "duplicate" => "Duplicate",
            _ => string.Empty,
        };
        Assert.Contains(preview.Issues, issue => issue.IsError && issue.Code == expectedCode);
    }

    [Fact]
    public void Validator_accepts_unresolved_items_without_promoting_them_to_annotations()
    {
        var paragraph = Paragraph("万二");
        var document = Document(paragraph);
        var json = JsonSerializer.Serialize(new
        {
            formatVersion = 1,
            projectId = document.ProjectId,
            batchId = BatchId,
            documentTextHash = document.DocumentTextHash,
            annotations = Array.Empty<object>(),
            unresolved = new[]
            {
                new
                {
                    paragraphId = paragraph.ParagraphId.ToString("D"),
                    start = 0,
                    length = 2,
                    baseText = "万二",
                    evidencePageMarkers = new[] { "0001" },
                    reason = "読みの根拠がない",
                },
            },
        }, JsonOptions);

        var preview = new RubyImportValidator().Validate(json, Context(document));

        Assert.True(preview.IsValid);
        Assert.Empty(preview.Result!.Annotations);
        Assert.Single(preview.Result.Unresolved);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("confidence")]
    [InlineData("evidence")]
    [InlineData("evidencePageMarkers")]
    public void Validator_rejects_a_missing_required_annotation_property(string propertyName)
    {
        var paragraph = Paragraph("八角");
        var document = Document(paragraph);
        var root = JsonNode.Parse(Json(document, Proposal(paragraph, 0, 2, "八角", "やすみ")))!.AsObject();
        root["annotations"]![0]!.AsObject().Remove(propertyName);

        var preview = new RubyImportValidator().Validate(root.ToJsonString(JsonOptions), Context(document));

        Assert.False(preview.IsValid);
        Assert.Contains(preview.Issues, issue => issue.Code == "InvalidJson" && issue.IsError);
    }

    [Fact]
    public void Validator_rejects_overflowing_ranges_without_throwing()
    {
        var paragraph = Paragraph("八角");
        var document = Document(paragraph);
        var proposal = Proposal(paragraph, int.MaxValue, int.MaxValue, "八角", "やすみ");

        var preview = new RubyImportValidator().Validate(Json(document, proposal), Context(document));

        Assert.False(preview.IsValid);
        Assert.Contains(preview.Issues, issue => issue.Code == "Range" && issue.IsError);
    }

    private static readonly Guid BatchId = Guid.NewGuid();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    private static StructuredParagraph Paragraph(string text)
    {
        return new StructuredParagraph(
            Guid.NewGuid(), DocumentElementRole.BodyParagraph,
            [new TextInline(text)], DocumentTextHash.Compute(text),
            [new SourceSpan(Guid.NewGuid(), "0001", 0, text.Length)]);
    }

    private static StructuredDocument Document(StructuredParagraph paragraph)
    {
        var draft = new StructuredDocument(Guid.NewGuid(), [paragraph], string.Empty);
        return draft with { DocumentTextHash = DocumentTextHash.Compute(draft) };
    }

    private static RubyAnnotationProposal Proposal(
        StructuredParagraph paragraph, int start, int length, string baseText, string reading) =>
        new(paragraph.ParagraphId.ToString("D"), start, length, baseText, reading,
            RubySource.ImageConfirmed, 1, ["0001"], "画像", Guid.NewGuid());

    private static RubyValidationContext Context(StructuredDocument document) =>
        new(document.ProjectId, BatchId, document, new HashSet<string> { "0001" },
            RubyPolicy.SuggestDifficultReadings);

    private static string Json(StructuredDocument document, params RubyAnnotationProposal[] annotations) =>
        JsonSerializer.Serialize(new
        {
            formatVersion = 1,
            projectId = document.ProjectId,
            batchId = BatchId,
            documentTextHash = document.DocumentTextHash,
            annotations = annotations.Select(ToJson),
            unresolved = Array.Empty<object>(),
        }, JsonOptions);

    private static object ToJson(RubyAnnotationProposal annotation) => new
    {
        paragraphId = annotation.ParagraphId,
        start = annotation.Start,
        length = annotation.Length,
        baseText = annotation.BaseText,
        reading = annotation.Reading,
        source = annotation.Source,
        confidence = annotation.Confidence,
        evidencePageMarkers = annotation.EvidencePageMarkers,
        evidence = annotation.Evidence,
    };
}
