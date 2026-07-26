namespace TateScribe.Core.Proofreading;

public enum ProofreadingDiffKind
{
    Equal,
    Added,
    Deleted,
    Changed
}

public sealed record ProofreadingDiffSpan(
    ProofreadingDiffKind Kind,
    string BeforeText,
    string AfterText);

public sealed record ProofreadingDiffResult(
    IReadOnlyList<ProofreadingDiffSpan> Spans,
    int AddedCharacterCount,
    int DeletedCharacterCount,
    int ReplacedCharacterCount,
    int ChangedCharacterCount,
    int ChangedParagraphCount);

public static class ProofreadingDiff
{
    private const long MaximumLcsCells = 4_000_000;

    public static ProofreadingDiffResult Calculate(string before, string after)
    {
        if ((long)(before.Length + 1) * (after.Length + 1) > MaximumLcsCells)
            return CalculateBounded(before, after);

        var lengths = BuildLongestCommonSubsequenceLengths(before, after);
        var raw = new List<ProofreadingDiffSpan>();
        var beforeIndex = 0;
        var afterIndex = 0;
        while (beforeIndex < before.Length || afterIndex < after.Length)
        {
            if (beforeIndex < before.Length && afterIndex < after.Length
                && before[beforeIndex] == after[afterIndex])
            {
                Append(raw, ProofreadingDiffKind.Equal, before[beforeIndex].ToString(), after[afterIndex].ToString());
                beforeIndex++;
                afterIndex++;
            }
            else if (afterIndex < after.Length
                && (beforeIndex == before.Length || lengths[beforeIndex, afterIndex + 1] >= lengths[beforeIndex + 1, afterIndex]))
            {
                Append(raw, ProofreadingDiffKind.Added, string.Empty, after[afterIndex].ToString());
                afterIndex++;
            }
            else
            {
                Append(raw, ProofreadingDiffKind.Deleted, before[beforeIndex].ToString(), string.Empty);
                beforeIndex++;
            }
        }

        var spans = MergeReplacements(raw);
        var added = spans.Where(span => span.Kind == ProofreadingDiffKind.Added).Sum(span => span.AfterText.Length);
        var deleted = spans.Where(span => span.Kind == ProofreadingDiffKind.Deleted).Sum(span => span.BeforeText.Length);
        var replaced = spans.Where(span => span.Kind == ProofreadingDiffKind.Changed)
            .Sum(span => Math.Max(span.BeforeText.Length, span.AfterText.Length));
        return new ProofreadingDiffResult(
            spans, added, deleted, replaced, added + deleted + replaced,
            CountChangedParagraphs(before, after));
    }

    private static ProofreadingDiffResult CalculateBounded(string before, string after)
    {
        var prefixLength = 0;
        var commonLength = Math.Min(before.Length, after.Length);
        while (prefixLength < commonLength && before[prefixLength] == after[prefixLength])
            prefixLength++;

        var suffixLength = 0;
        while (suffixLength < commonLength - prefixLength
               && before[before.Length - suffixLength - 1] == after[after.Length - suffixLength - 1])
            suffixLength++;

        var spans = new List<ProofreadingDiffSpan>();
        if (prefixLength > 0)
            spans.Add(new ProofreadingDiffSpan(
                ProofreadingDiffKind.Equal, before[..prefixLength], after[..prefixLength]));

        var beforeMiddle = before[prefixLength..(before.Length - suffixLength)];
        var afterMiddle = after[prefixLength..(after.Length - suffixLength)];
        var kind = beforeMiddle.Length == 0
            ? ProofreadingDiffKind.Added
            : afterMiddle.Length == 0
                ? ProofreadingDiffKind.Deleted
                : ProofreadingDiffKind.Changed;
        if (beforeMiddle.Length > 0 || afterMiddle.Length > 0)
            spans.Add(new ProofreadingDiffSpan(kind, beforeMiddle, afterMiddle));

        if (suffixLength > 0)
            spans.Add(new ProofreadingDiffSpan(
                ProofreadingDiffKind.Equal, before[^suffixLength..], after[^suffixLength..]));

        var added = kind == ProofreadingDiffKind.Added ? afterMiddle.Length : 0;
        var deleted = kind == ProofreadingDiffKind.Deleted ? beforeMiddle.Length : 0;
        var replaced = kind == ProofreadingDiffKind.Changed
            ? Math.Max(beforeMiddle.Length, afterMiddle.Length)
            : 0;
        return new ProofreadingDiffResult(
            spans, added, deleted, replaced, added + deleted + replaced,
            CountChangedParagraphs(before, after));
    }

    private static int[,] BuildLongestCommonSubsequenceLengths(string before, string after)
    {
        var lengths = new int[before.Length + 1, after.Length + 1];
        for (var beforeIndex = before.Length - 1; beforeIndex >= 0; beforeIndex--)
        {
            for (var afterIndex = after.Length - 1; afterIndex >= 0; afterIndex--)
                lengths[beforeIndex, afterIndex] = before[beforeIndex] == after[afterIndex]
                    ? lengths[beforeIndex + 1, afterIndex + 1] + 1
                    : Math.Max(lengths[beforeIndex + 1, afterIndex], lengths[beforeIndex, afterIndex + 1]);
        }
        return lengths;
    }

    private static void Append(
        IList<ProofreadingDiffSpan> spans,
        ProofreadingDiffKind kind,
        string before,
        string after)
    {
        if (spans.Count > 0 && spans[^1].Kind == kind)
        {
            var previous = spans[^1];
            spans[^1] = previous with
            {
                BeforeText = previous.BeforeText + before,
                AfterText = previous.AfterText + after
            };
            return;
        }
        spans.Add(new ProofreadingDiffSpan(kind, before, after));
    }

    private static IReadOnlyList<ProofreadingDiffSpan> MergeReplacements(IReadOnlyList<ProofreadingDiffSpan> spans)
    {
        var merged = new List<ProofreadingDiffSpan>();
        for (var index = 0; index < spans.Count; index++)
        {
            if (index + 1 < spans.Count
                && spans[index].Kind is ProofreadingDiffKind.Added or ProofreadingDiffKind.Deleted
                && spans[index + 1].Kind is ProofreadingDiffKind.Added or ProofreadingDiffKind.Deleted
                && spans[index].Kind != spans[index + 1].Kind)
            {
                var first = spans[index];
                var second = spans[index + 1];
                merged.Add(new ProofreadingDiffSpan(
                    ProofreadingDiffKind.Changed,
                    first.BeforeText + second.BeforeText,
                    first.AfterText + second.AfterText));
                index++;
                continue;
            }
            merged.Add(spans[index]);
        }
        return merged;
    }

    private static int CountChangedParagraphs(string before, string after)
    {
        var beforeParagraphs = before.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var afterParagraphs = after.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var count = Math.Max(beforeParagraphs.Length, afterParagraphs.Length);
        return Enumerable.Range(0, count).Count(index =>
            index >= beforeParagraphs.Length
            || index >= afterParagraphs.Length
            || !string.Equals(beforeParagraphs[index], afterParagraphs[index], StringComparison.Ordinal));
    }
}
