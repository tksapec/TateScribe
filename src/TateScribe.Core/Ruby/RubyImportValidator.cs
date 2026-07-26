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
            ErrorIf(string.IsNullOrWhiteSpace(annotation.Reading), "Reading", "readingは空にできません。");
            ErrorIf(string.IsNullOrWhiteSpace(annotation.Evidence), "Evidence", "evidenceは空にできません。");
            ErrorIf(annotation.EvidencePageMarkers is null, "EvidencePageMarkers",
                "evidencePageMarkers配列がありません。");
            ErrorIf(annotation.Confidence is < 0 or > 1, "Confidence", "confidenceは0.0から1.0で指定してください。");
            if (!Enum.IsDefined(annotation.Source))
                issues.Add(new RubyValidationIssue("Source", "sourceが許可された値ではありません。", true));
            ErrorIf(!AllowedByPolicy(annotation.Source, context.Policy), "RubyPolicy",
                $"source '{annotation.Source}' はrubyPolicy '{context.Policy}' の対象外です。");
            var annotationEnd = (long)annotation.Start + annotation.Length;
            if (paragraph is not null
                && annotation.Start >= 0
                && annotation.Length >= 1
                && annotationEnd <= paragraph.PlainText.Length
                && IsSplitSurrogate(paragraph.PlainText, annotation.Start, annotation.Length))
                issues.Add(new RubyValidationIssue("Utf16Range", "startまたはlengthがUTF-16文字の途中を指しています。", true));

            var paragraphKey = annotation.ParagraphId ?? string.Empty;
            var duplicateKey = $"{paragraphKey}\0{annotation.Start}\0{annotation.Length}\0{annotation.Reading}";
            ErrorIf(!duplicates.Add(duplicateKey), "Duplicate", "同じルビ注釈が重複しています。");
            if (!ranges.TryGetValue(paragraphKey, out var paragraphRanges))
            {
                paragraphRanges = [];
                ranges[paragraphKey] = paragraphRanges;
            }
            var end = (long)annotation.Start + annotation.Length;
            ErrorIf(paragraphRanges.Any(range => annotation.Start < range.End && end > range.Start),
                "Overlap", "同じ段落内でルビ範囲が重複しています。");
            if (end <= int.MaxValue)
                paragraphRanges.Add((annotation.Start, (int)end));

            if (annotation.Source is RubySource.DictionarySuggested or RubySource.ContextSuggested)
                issues.Add(new RubyValidationIssue("SuggestedReading", "辞書または文脈だけを根拠にした候補です。", false));
            if (annotation.Confidence < 0.7)
                issues.Add(new RubyValidationIssue("LowConfidence", "confidenceが低い候補です。", false));
            if ((annotation.Reading ?? string.Empty).Any(character => !IsKana(character)))
                issues.Add(new RubyValidationIssue("NonKanaReading", "readingにひらがな・カタカナ以外が含まれます。", false));
            if (annotation.Source == RubySource.ImageConfirmed
                && context.OcrCandidates is { Count: > 0 }
                && !context.OcrCandidates.Any(candidate =>
                    string.Equals(candidate.OcrText, annotation.BaseText, StringComparison.Ordinal)
                    && (annotation.EvidencePageMarkers ?? []).Contains(candidate.PageMarker, StringComparer.Ordinal)))
                issues.Add(new RubyValidationIssue("ImageCandidateMismatch", "画像根拠のルビがOCRルビ候補と一致しません。", false));
        }
        foreach (var group in (document.Annotations ?? []).GroupBy(item => item.BaseText, StringComparer.Ordinal)
            .Where(group => group.Select(item => item.Reading).Distinct(StringComparer.Ordinal).Count() > 1))
            issues.Add(new RubyValidationIssue("MultipleReadings",
                $"同一表記「{group.Key}」に複数の読みがあります。出現位置ごとに確認してください。", false));
        foreach (var unresolved in document.Unresolved ?? [])
        {
            ValidateRange(unresolved.ParagraphId, unresolved.Start, unresolved.Length, unresolved.BaseText,
                unresolved.EvidencePageMarkers, out var paragraph);
            ErrorIf(unresolved.EvidencePageMarkers is null, "EvidencePageMarkers",
                "未確定項目のevidencePageMarkers配列がありません。");
            var unresolvedEnd = (long)unresolved.Start + unresolved.Length;
            if (paragraph is not null && unresolved.Start >= 0 && unresolved.Length >= 1
                && unresolvedEnd <= paragraph.PlainText.Length
                && IsSplitSurrogate(paragraph.PlainText, unresolved.Start, unresolved.Length))
                issues.Add(new RubyValidationIssue("Utf16Range", "未確定項目の範囲がUTF-16文字の途中を指しています。", true));
            ErrorIf(string.IsNullOrWhiteSpace(unresolved.Reason), "UnresolvedReason",
                "未確定項目のreasonは空にできません。");
        }

        return new RubyImportPreview(document, issues);

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
