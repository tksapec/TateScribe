using System.Text;

namespace TateScribe.Core.Layout;

public static class PunctuationMerger
{
    private const string PunctuationAndQuotes = "\u3001\u3002\uFF01\uFF1F\u300C\u300D\u300E\u300F\uFF08\uFF09\u2026\u30FC";
    private const int MaximumAlignmentCells = 4_000_000;

    public static string Merge(string primary, string auxiliary, int lookAhead)
    {
        if (string.IsNullOrEmpty(primary) || string.IsNullOrEmpty(auxiliary)) return primary;
        _ = lookAhead;
        auxiliary = auxiliary.Replace('[', '\u300C').Replace(']', '\u300D');
        var matches = FindLongestCommonSubsequence(primary, auxiliary);
        if (matches.Count == 0) return primary;
        var insertions = new Dictionary<int, StringBuilder>();
        var previousPrimaryIndex = -1;
        var previousAuxiliaryIndex = -1;
        for (var matchIndex = 0; matchIndex < matches.Count; matchIndex++)
        {
            var (primaryIndex, auxiliaryIndex) = matches[matchIndex];
            AddSupplementaryGap(primary, auxiliary, previousPrimaryIndex + 1, primaryIndex, previousAuxiliaryIndex + 1, auxiliaryIndex, insertions, HasReliableContext(primary, auxiliary, matches, matchIndex - 1, matchIndex));
            previousPrimaryIndex = primaryIndex;
            previousAuxiliaryIndex = auxiliaryIndex;
        }
        AddSupplementaryGap(primary, auxiliary, previousPrimaryIndex + 1, primary.Length, previousAuxiliaryIndex + 1, auxiliary.Length, insertions, HasReliableContext(primary, auxiliary, matches, matches.Count - 1, matches.Count));

        var result = new StringBuilder(primary.Length + insertions.Sum(pair => pair.Value.Length));
        for (var index = 0; index <= primary.Length; index++)
        {
            if (insertions.TryGetValue(index, out var punctuation)) result.Append(punctuation);
            if (index < primary.Length) result.Append(primary[index]);
        }
        return result.ToString();
    }

    private static void AddSupplementaryGap(string primary, string auxiliary, int primaryStart, int primaryEnd, int auxiliaryStart, int auxiliaryEnd, Dictionary<int, StringBuilder> insertions, bool hasReliableContext)
    {
        var primaryGap = primary.AsSpan(primaryStart, primaryEnd - primaryStart);
        var auxiliaryGap = auxiliary.AsSpan(auxiliaryStart, auxiliaryEnd - auxiliaryStart);
        if (!hasReliableContext || auxiliaryGap.IsEmpty) return;
        if (ContainsOnlyWhitespace(primaryGap) && ContainsOnlySupplementaryCharacters(auxiliaryGap))
        {
            AddInsertion(insertions, primaryStart, auxiliaryGap);
            return;
        }

        if (ContainsOnlyWhitespace(primaryGap) && auxiliaryGap.Length == 1 && auxiliaryGap[0] == '\u4E00' && HasClosingQuoteAhead(auxiliary, auxiliaryEnd))
        {
            AddInsertion(insertions, primaryStart, "\u300C".AsSpan());
            return;
        }

        var leadingSupplementaryCharacterCount = LeadingSupplementaryCharacterCount(auxiliaryGap);
        if (primaryGap.Length == auxiliaryGap.Length - leadingSupplementaryCharacterCount && leadingSupplementaryCharacterCount > 0 && ContainsOnlyOpeningQuoteMarkers(auxiliaryGap[..leadingSupplementaryCharacterCount]))
            AddInsertion(insertions, primaryStart, auxiliaryGap[..leadingSupplementaryCharacterCount]);
    }

    private static void AddInsertion(Dictionary<int, StringBuilder> insertions, int primaryStart, ReadOnlySpan<char> text)
    {
        if (!insertions.TryGetValue(primaryStart, out var supplementaryCharacters))
            insertions[primaryStart] = supplementaryCharacters = new StringBuilder();
        supplementaryCharacters.Append(text);
    }

    private static int LeadingSupplementaryCharacterCount(ReadOnlySpan<char> text)
    {
        var count = 0;
        while (count < text.Length && IsSupplementaryCharacter(text[count])) count++;
        return count;
    }

    private static bool ContainsOnlyOpeningQuoteMarkers(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
            if (character is not ('\u300C' or '\u300E' or '\uFF08')) return false;
        return true;
    }

    private static bool HasClosingQuoteAhead(string text, int startIndex)
    {
        var closingQuoteIndex = text.IndexOf('\u300D', startIndex);
        return closingQuoteIndex >= startIndex && closingQuoteIndex - startIndex <= 80;
    }

    private static bool HasReliableContext(string primary, string auxiliary, IReadOnlyList<(int PrimaryIndex, int AuxiliaryIndex)> matches, int leftMatchIndex, int rightMatchIndex)
    {
        var contiguousLeft = 0;
        for (var index = leftMatchIndex; index >= 0; index--)
        {
            if (index < leftMatchIndex && !AreAdjacent(primary, auxiliary, matches[index], matches[index + 1])) break;
            contiguousLeft++;
        }

        var contiguousRight = 0;
        for (var index = rightMatchIndex; index < matches.Count; index++)
        {
            if (index > rightMatchIndex && !AreAdjacent(primary, auxiliary, matches[index - 1], matches[index])) break;
            contiguousRight++;
        }
        if (leftMatchIndex < 0 || rightMatchIndex >= matches.Count)
            return Math.Max(contiguousLeft, contiguousRight) >= 4 || Math.Max(contiguousLeft, contiguousRight) == matches.Count && matches.Count >= 3;
        return contiguousLeft + contiguousRight >= 3;
    }

    private static bool AreAdjacent(string primary, string auxiliary, (int PrimaryIndex, int AuxiliaryIndex) first, (int PrimaryIndex, int AuxiliaryIndex) second) =>
        first.PrimaryIndex + 1 == second.PrimaryIndex
        && ContainsOnlySupplementaryCharacters(auxiliary.AsSpan(first.AuxiliaryIndex + 1, second.AuxiliaryIndex - first.AuxiliaryIndex - 1));

    private static bool ContainsOnlyWhitespace(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
            if (!char.IsWhiteSpace(character)) return false;
        return true;
    }

    private static bool ContainsOnlySupplementaryCharacters(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
            if (!IsSupplementaryCharacter(character)) return false;
        return true;
    }

    private static IReadOnlyList<(int PrimaryIndex, int AuxiliaryIndex)> FindLongestCommonSubsequence(string primary, string auxiliary)
    {
        if ((long)primary.Length * auxiliary.Length > MaximumAlignmentCells) return [];
        var lengths = new int[primary.Length + 1, auxiliary.Length + 1];
        for (var primaryIndex = primary.Length - 1; primaryIndex >= 0; primaryIndex--)
        {
            for (var auxiliaryIndex = auxiliary.Length - 1; auxiliaryIndex >= 0; auxiliaryIndex--)
            {
                lengths[primaryIndex, auxiliaryIndex] = primary[primaryIndex] == auxiliary[auxiliaryIndex]
                    ? lengths[primaryIndex + 1, auxiliaryIndex + 1] + 1
                    : Math.Max(lengths[primaryIndex + 1, auxiliaryIndex], lengths[primaryIndex, auxiliaryIndex + 1]);
            }
        }

        var matches = new List<(int PrimaryIndex, int AuxiliaryIndex)>();
        var primaryCursor = 0;
        var auxiliaryCursor = 0;
        while (primaryCursor < primary.Length && auxiliaryCursor < auxiliary.Length)
        {
            if (primary[primaryCursor] == auxiliary[auxiliaryCursor])
            {
                matches.Add((primaryCursor++, auxiliaryCursor++));
            }
            else if (lengths[primaryCursor + 1, auxiliaryCursor] >= lengths[primaryCursor, auxiliaryCursor + 1])
            {
                primaryCursor++;
            }
            else
            {
                auxiliaryCursor++;
            }
        }
        return matches;
    }

    private static bool IsSupplementaryCharacter(char character) =>
        PunctuationAndQuotes.Contains(character)
        || character is >= '\u3041' and <= '\u3049'
        || character is '\u3063' or >= '\u3083' and <= '\u3087' or '\u308E' or '\u3095' or '\u3096'
        || character is >= '\u30A1' and <= '\u30A9'
        || character is '\u30C3' or >= '\u30E3' and <= '\u30E7' or '\u30EE' or '\u30F5' or '\u30F6';
}
