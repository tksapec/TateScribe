using System.Text.Json;
using System.Text.Json.Serialization;

namespace TateScribe.Core.Ruby;

public sealed class RubyImportValidator
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    public RubyImportPreview Validate(string json, RubyValidationContext context)
    {
        RubyImportDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<RubyImportDocument>(json, Options);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return Invalid("InvalidJson", exception.Message);
        }
        if (document is null) return Invalid("InvalidJson", "JSONのルートがありません。");

        return Validate(document, context);
    }

    public RubyImportPreview Validate(
        RubyImportDocument document,
        RubyValidationContext context)
    {
        var issues = new List<RubyValidationIssue>();
        ErrorIf(document.FormatVersion != 1, "FormatVersion", "formatVersionは1である必要があります。");
        ErrorIf(document.ProjectId != context.ProjectId, "ProjectId", "projectIdが現在のプロジェクトと一致しません。");
        ErrorIf(document.BatchId != context.BatchId, "BatchId", "batchIdが出力したバッチと一致しません。");
        ErrorIf(!string.Equals(document.DocumentTextHash, context.Document.DocumentTextHash, StringComparison.Ordinal),
            "DocumentTextHash", "本文がパッケージ出力後に変更されています。");
        ErrorIf(context.ConfirmedTextIsStale, "ConfirmedTextStale", "確定本文がStaleです。");
        ErrorIf(document.Annotations is null, "Annotations", "annotations配列がありません。");
        ErrorIf(document.Unresolved is null, "Unresolved", "unresolved配列がありません。");

        var paragraphs = context.Document.Paragraphs.ToDictionary(
            paragraph => paragraph.ParagraphId.ToString("D"), StringComparer.OrdinalIgnoreCase);
        var ranges = new Dictionary<string, List<(int Start, int End)>>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var annotation in document.Annotations ?? [])
        {
            ValidateRange(annotation.ParagraphId, annotation.Start, annotation.Length, annotation.BaseText,
                annotation.EvidencePageMarkers, out var paragraph);
            ErrorFor(annotation, string.IsNullOrWhiteSpace(annotation.Reading), "Reading", "readingは空にできません。");
            ErrorFor(annotation, string.IsNullOrWhiteSpace(annotation.Evidence), "Evidence", "evidenceは空にできません。");
            ErrorFor(annotation, annotation.EvidencePageMarkers is null, "EvidencePageMarkers",
                "evidencePageMarkers配列がありません。");
            ErrorFor(annotation,
                annotation.EvidencePageMarkers is { Count: > 1 }
                && annotation.EvidencePageMarkers.Distinct(StringComparer.Ordinal).Count()
                    != annotation.EvidencePageMarkers.Count,
                "EvidencePageMarkersUnique",
                "evidencePageMarkersに同じページを重複して指定できません。");
            ErrorFor(annotation,
                annotation.Source is RubySource.ImageConfirmed or RubySource.TextConfirmed
                && annotation.EvidencePageMarkers is not { Count: > 0 },
                "EvidencePageMarkers",
                "ImageConfirmedまたはTextConfirmedには根拠ページが1件以上必要です。");
            ErrorFor(annotation, annotation.Confidence is < 0 or > 1, "Confidence", "confidenceは0.0から1.0で指定してください。");
            if (!Enum.IsDefined(annotation.Source))
                AddFor(annotation, "Source", "sourceが許可された値ではありません。", true);
            ErrorFor(annotation, !AllowedByPolicy(annotation.Source, context.Policy), "RubyPolicy",
                $"source '{annotation.Source}' はrubyPolicy '{context.Policy}' の対象外です。");
            var annotationEnd = (long)annotation.Start + annotation.Length;
            if (paragraph is not null
                && annotation.Start >= 0
                && annotation.Length >= 1
                && annotationEnd <= paragraph.PlainText.Length
                && IsSplitSurrogate(paragraph.PlainText, annotation.Start, annotation.Length))
                AddFor(annotation, "Utf16Range", "startまたはlengthがUTF-16文字の途中を指しています。", true);

            var paragraphKey = annotation.ParagraphId ?? string.Empty;
            var duplicateKey = $"{paragraphKey}\0{annotation.Start}\0{annotation.Length}\0{annotation.Reading}";
            ErrorFor(annotation, !duplicates.Add(duplicateKey), "Duplicate", "同じルビ注釈が重複しています。");
            if (!ranges.TryGetValue(paragraphKey, out var paragraphRanges))
            {
                paragraphRanges = [];
                ranges[paragraphKey] = paragraphRanges;
            }
            var end = (long)annotation.Start + annotation.Length;
            ErrorFor(annotation, paragraphRanges.Any(range => annotation.Start < range.End && end > range.Start),
                "Overlap", "同じ段落内でルビ範囲が重複しています。");
            if (end <= int.MaxValue)
                paragraphRanges.Add((annotation.Start, (int)end));

            if (annotation.Source is RubySource.DictionarySuggested or RubySource.ContextSuggested)
                AddFor(annotation, "SuggestedReading", "辞書または文脈だけを根拠にした候補です。", false);
            if (annotation.Confidence < RubyBulkConfirmationPolicy.MinBulkConfirmAnnotationConfidence)
                AddFor(annotation, "LowConfidence", "confidenceが低い候補です。", false);
            if (RubyTextNormalizer.NormalizeReading(annotation.Reading).Any(character => !IsKana(character)))
                AddFor(annotation, "NonKanaReading", "readingにひらがな・カタカナ以外が含まれます。", false);
            if (annotation.Source == RubySource.ImageConfirmed && paragraph is not null)
                ValidateImageCandidate(annotation, paragraph);
        }
        foreach (var group in (document.Annotations ?? []).GroupBy(item => item.BaseText, StringComparer.Ordinal)
            .Where(group => group.Select(item => item.Reading).Distinct(StringComparer.Ordinal).Count() > 1))
            foreach (var annotation in group)
                AddFor(annotation, "MultipleReadings",
                    $"同一表記「{group.Key}」に複数の読みがあります。出現位置ごとに確認してください。", false);
        foreach (var unresolved in document.Unresolved ?? [])
        {
            ValidateRange(unresolved.ParagraphId, unresolved.Start, unresolved.Length, unresolved.BaseText,
                unresolved.EvidencePageMarkers, out var paragraph);
            ErrorIf(unresolved.EvidencePageMarkers is null, "EvidencePageMarkers",
                "未確定項目のevidencePageMarkers配列がありません。");
            ErrorIf(
                unresolved.EvidencePageMarkers is { Count: > 1 }
                && unresolved.EvidencePageMarkers.Distinct(StringComparer.Ordinal).Count()
                    != unresolved.EvidencePageMarkers.Count,
                "EvidencePageMarkersUnique",
                "未確定項目のevidencePageMarkersに同じページを重複して指定できません。");
            var unresolvedEnd = (long)unresolved.Start + unresolved.Length;
            if (paragraph is not null && unresolved.Start >= 0 && unresolved.Length >= 1
                && unresolvedEnd <= paragraph.PlainText.Length
                && IsSplitSurrogate(paragraph.PlainText, unresolved.Start, unresolved.Length))
                issues.Add(new RubyValidationIssue("Utf16Range", "未確定項目の範囲がUTF-16文字の途中を指しています。", true));
            ErrorIf(string.IsNullOrWhiteSpace(unresolved.Reason), "UnresolvedReason",
                "未確定項目のreasonは空にできません。");
        }

        return new RubyImportPreview(document, issues);

        void ValidateImageCandidate(
            RubyAnnotationProposal annotation,
            StructuredParagraph paragraph)
        {
            var annotationEnd = (long)annotation.Start + annotation.Length;
            var sourcePageMarkers = paragraph.SourceSpans
                .Where(span =>
                    span.Start < annotationEnd
                    && (long)span.Start + span.Length > annotation.Start)
                .Select(span => span.PageMarker)
                .ToHashSet(StringComparer.Ordinal);
            var evidencePageMarkers = annotation.EvidencePageMarkers ?? [];
            if (sourcePageMarkers.Count == 0
                || evidencePageMarkers.Any(marker => !sourcePageMarkers.Contains(marker)))
                AddFor(
                    annotation,
                    "EvidencePageDoesNotMatchSourceSpan",
                    "The image evidence page does not own the annotation source text range.",
                    false);

            var pageCandidates = (context.OcrCandidates ?? [])
                .Where(candidate =>
                    sourcePageMarkers.Contains(candidate.PageMarker)
                    && evidencePageMarkers.Contains(candidate.PageMarker, StringComparer.Ordinal))
                .ToArray();
            var reading = RubyTextNormalizer.NormalizeReading(annotation.Reading);
            var readingMatches = pageCandidates
                .Where(candidate => string.Equals(
                    RubyTextNormalizer.NormalizeReading(candidate.ReadingCandidate),
                    reading,
                    StringComparison.Ordinal))
                .ToArray();
            if (readingMatches.Length == 0)
            {
                AddFor(
                    annotation,
                    "ImageCandidateMismatch",
                    "No OCR candidate on the source page matches the proposed reading.",
                    false);
                return;
            }

            var baseMatches = readingMatches
                .Where(candidate =>
                    candidate.BaseTextCandidate is not null
                    && string.Equals(
                        candidate.BaseTextCandidate,
                        annotation.BaseText,
                        StringComparison.Ordinal))
                .ToArray();
            if (baseMatches.Length == 0)
            {
                if (sourcePageMarkers.SetEquals(evidencePageMarkers))
                {
                    AddCandidateMismatchIssue(annotation);
                    return;
                }
                AddFor(
                    annotation,
                    readingMatches.All(candidate => candidate.BaseTextCandidate is null)
                        ? "BaseTextCandidateUnknown"
                        : "ImageCandidateMismatch",
                    readingMatches.All(candidate => candidate.BaseTextCandidate is null)
                        ? "The OCR evidence has no linked parent-text candidate."
                        : "The OCR parent-text candidate does not match the annotation source range.",
                    false);
                return;
            }

            var rightSideCandidates = baseMatches
                .Where(candidate => !candidate.ReturnedToBody)
                .ToArray();
            if (rightSideCandidates.Length == 0)
            {
                AddFor(
                    annotation,
                    "WrongSideCandidate",
                    "The matching OCR candidate was returned to the body-text side.",
                    false);
                return;
            }

            var confidentCandidates = rightSideCandidates
                .Where(candidate =>
                    candidate.Confidence
                        >= RubyBulkConfirmationPolicy.MinBulkConfirmOcrConfidence)
                .ToArray();
            if (confidentCandidates.Length == 0)
            {
                AddFor(
                    annotation,
                    "LowOcrCandidateConfidence",
                    "The matching OCR candidate confidence is below the bulk-confirmation threshold.",
                    false);
                return;
            }

            if (confidentCandidates.All(candidate =>
                    candidate.LinkConfidence is null
                    || candidate.LinkConfidence
                        < RubyBulkConfirmationPolicy.MinBulkConfirmLinkConfidence))
                AddFor(
                    annotation,
                    "LowLinkConfidence",
                    "The matching OCR parent-text link confidence is below the bulk-confirmation threshold.",
                    false);
        }

        void AddCandidateMismatchIssue(RubyAnnotationProposal annotation)
        {
            var pageCandidates = (context.OcrCandidates ?? [])
                .Where(candidate => (annotation.EvidencePageMarkers ?? [])
                    .Contains(candidate.PageMarker, StringComparer.Ordinal))
                .ToArray();
            var reading = RubyTextNormalizer.NormalizeReading(annotation.Reading);
            var readingMatches = pageCandidates
                .Where(candidate => string.Equals(
                    RubyTextNormalizer.NormalizeReading(candidate.ReadingCandidate),
                    reading,
                    StringComparison.Ordinal))
                .ToArray();
            if (readingMatches.Length == 0)
            {
                AddFor(annotation, "ImageCandidateMismatch",
                    "画像根拠の読みがOCRルビ候補または根拠ページと一致しません。", false);
                return;
            }
            if (readingMatches.Any(candidate =>
                    candidate.BaseTextCandidate is not null
                    && string.Equals(
                        candidate.BaseTextCandidate,
                        annotation.BaseText,
                        StringComparison.Ordinal)))
                return;
            if (readingMatches.All(candidate => candidate.BaseTextCandidate is null))
            {
                AddFor(annotation, "BaseTextCandidateUnknown",
                    "OCR座標から親文字候補を一意に特定できません。画像で確認してください。", false);
                return;
            }
            AddFor(annotation, "ImageCandidateMismatch",
                "OCRルビ候補の親文字候補が指定範囲の本文と一致しません。", false);
        }

        void ValidateRange(
            string? paragraphId,
            int start,
            int length,
            string? baseText,
            IReadOnlyList<string>? evidenceMarkers,
            out StructuredParagraph? paragraph)
        {
            paragraph = null;
            if (!string.IsNullOrWhiteSpace(paragraphId))
                paragraphs.TryGetValue(paragraphId, out paragraph);
            ErrorIf(paragraph is null, "ParagraphId", $"paragraphId '{paragraphId}' が存在しません。");
            ErrorIf(start < 0, "Start", "startは0以上で指定してください。");
            ErrorIf(length < 1, "Length", "lengthは1以上で指定してください。");
            if (paragraph is not null && start >= 0 && length >= 1)
            {
                var end = (long)start + length;
                ErrorIf(end > paragraph.PlainText.Length, "Range", "指定範囲が本文長を超えています。");
                if (end <= paragraph.PlainText.Length)
                    ErrorIf(!string.Equals(paragraph.PlainText.Substring(start, length), baseText, StringComparison.Ordinal),
                        "BaseText", "baseTextが指定範囲の本文と一致しません。");
                ErrorIf(!string.Equals(paragraph.TextHash, DocumentTextHash.Compute(paragraph.PlainText), StringComparison.Ordinal),
                    "ParagraphTextHash", "段落のtextHashが現在の本文と一致しません。");
            }
            foreach (var marker in evidenceMarkers ?? [])
                ErrorIf(!context.BatchPageMarkers.Contains(marker), "EvidencePage", $"evidencePageMarker '{marker}' は対象バッチにありません。");
        }

        void ErrorIf(bool condition, string code, string message)
        {
            if (condition) issues.Add(new RubyValidationIssue(code, message, true));
        }

        void ErrorFor(
            RubyAnnotationProposal annotation,
            bool condition,
            string code,
            string message)
        {
            if (condition) AddFor(annotation, code, message, true);
        }

        void AddFor(
            RubyAnnotationProposal annotation,
            string code,
            string message,
            bool isError)
        {
            issues.Add(new RubyValidationIssue(
                code,
                message,
                isError,
                annotation.ParagraphId,
                annotation.Start,
                annotation.Length,
                annotation.AnnotationId == Guid.Empty ? null : annotation.AnnotationId));
        }
    }

    private static bool IsSplitSurrogate(string text, int start, int length)
    {
        var end = start + length;
        return (start > 0 && start < text.Length && char.IsHighSurrogate(text[start - 1]) && char.IsLowSurrogate(text[start]))
            || (end > 0 && end < text.Length && char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end]));
    }

    private static bool IsKana(char value) =>
        value is >= '\u3040' and <= '\u30ff' or 'ー' or '・' or 'ゝ' or 'ゞ' or 'ヽ' or 'ヾ';

    private static bool AllowedByPolicy(RubySource source, RubyPolicy policy) => policy switch
    {
        RubyPolicy.PreserveOriginalOnly => source == RubySource.ImageConfirmed,
        RubyPolicy.OriginalAndTextConfirmed =>
            source is RubySource.ImageConfirmed or RubySource.TextConfirmed or RubySource.UserConfirmed,
        RubyPolicy.SuggestDifficultReadings => true,
        _ => false,
    };

    private static RubyImportPreview Invalid(string code, string message) =>
        new(null, [new RubyValidationIssue(code, message, true)]);
}
