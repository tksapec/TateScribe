using System.Text;
using TateScribe.Core.Ocr;

namespace TateScribe.Core.Layout;

public static class PunctuationMerger
{
    private const string PunctuationAndQuotes = "\u3001\u3002\uFF01\uFF1F\u300C\u300D\u300E\u300F\uFF08\uFF09\u2026\u30FC\n";
    private const int MaximumAlignmentCells = 4_000_000;
    private const int MaximumTrustedSupplementaryGapLength = 12;

    public static OcrMergeProposal Propose(string primary, string auxiliary, IReadOnlyList<OcrWord> paddleWords, int lookAhead)
    {
        var suggested = Merge(primary, auxiliary, lookAhead);
        var operations = BuildOperations(primary, suggested, paddleWords);
        var reviewItems = operations
            .Where(operation => operation.AnchorWordOrdinal is null)
            .Select(operation => new ReviewItem(
                "UnanchoredSuggestion",
                $"Supplementary OCR suggestion '{operation.ProposedText}' has no PaddleOCR coordinate anchor.",
                null))
            .ToArray();
        return new OcrMergeProposal(suggested, operations, reviewItems);
    }

    public static string Merge(string primary, string auxiliary, int lookAhead)
    {
        if (string.IsNullOrEmpty(primary) || string.IsNullOrEmpty(auxiliary)) return primary;
        _ = lookAhead;
        auxiliary = auxiliary.Replace('[', '\u300C').Replace(']', '\u300D')
            .Replace("?\u30FB\u300D", "?\u300D")
            .Replace("\uFF1F\u30FB\u300D", "\uFF1F\u300D")
            .Replace("\u3002\u30FB\u300D", "\u3002\u300D");
        var matches = FindLongestCommonSubsequence(primary, auxiliary);
        if (matches.Count == 0) return primary;
        var insertions = new Dictionary<int, StringBuilder>();
        var replacements = new Dictionary<int, string>();
        var previousPrimaryIndex = -1;
        var previousAuxiliaryIndex = -1;
        for (var matchIndex = 0; matchIndex < matches.Count; matchIndex++)
        {
            var (primaryIndex, auxiliaryIndex) = matches[matchIndex];
            AddSupplementaryGap(primary, auxiliary, previousPrimaryIndex + 1, primaryIndex, previousAuxiliaryIndex + 1, auxiliaryIndex, insertions, replacements, HasReliableContext(primary, auxiliary, matches, matchIndex - 1, matchIndex));
            previousPrimaryIndex = primaryIndex;
            previousAuxiliaryIndex = auxiliaryIndex;
        }
        AddSupplementaryGap(primary, auxiliary, previousPrimaryIndex + 1, primary.Length, previousAuxiliaryIndex + 1, auxiliary.Length, insertions, replacements, HasReliableContext(primary, auxiliary, matches, matches.Count - 1, matches.Count));

        var result = new StringBuilder(primary.Length + insertions.Sum(pair => pair.Value.Length));
        for (var index = 0; index <= primary.Length; index++)
        {
            if (insertions.TryGetValue(index, out var punctuation)) result.Append(punctuation);
            if (replacements.TryGetValue(index, out var replacement))
            {
                result.Append(replacement);
                continue;
            }
            if (index < primary.Length) result.Append(primary[index]);
        }
        return NormalizeSafeDuplicateQuotes(RecoverQuotedKatakanaTitleAfterNiyoru(result.ToString()));
    }

    private static IReadOnlyList<OcrMergeOperation> BuildOperations(string primary, string suggested, IReadOnlyList<OcrWord> paddleWords)
    {
        var operations = new List<OcrMergeOperation>();
        var primaryIndex = 0;
        var suggestedIndex = 0;
        while (primaryIndex < primary.Length || suggestedIndex < suggested.Length)
        {
            if (primaryIndex < primary.Length && suggestedIndex < suggested.Length && primary[primaryIndex] == suggested[suggestedIndex])
            {
                primaryIndex++;
                suggestedIndex++;
                continue;
            }

            var next = FindNextMatchingCharacter(primary, suggested, primaryIndex, suggestedIndex);
            var primaryEnd = next.PrimaryIndex < 0 ? primary.Length : next.PrimaryIndex;
            var suggestedEnd = next.SuggestedIndex < 0 ? suggested.Length : next.SuggestedIndex;
            var original = primary[primaryIndex..primaryEnd];
            var proposed = suggested[suggestedIndex..suggestedEnd];
            var type = original.Length == 0
                ? OcrMergeOperationType.Insertion
                : proposed.Length == 0 ? OcrMergeOperationType.Deletion : OcrMergeOperationType.Replacement;
            operations.Add(new OcrMergeOperation(
                type,
                suggestedIndex,
                original,
                proposed,
                FindAnchorWordOrdinal(primary, primaryIndex, paddleWords),
                0.8,
                ContainsOnlySupplementaryCharacters(proposed.AsSpan()) ? "AuxiliarySupplement" : "AuxiliaryCorrection"));
            primaryIndex = primaryEnd;
            suggestedIndex = suggestedEnd;
        }
        return operations;
    }

    private static (int PrimaryIndex, int SuggestedIndex) FindNextMatchingCharacter(string primary, string suggested, int primaryStart, int suggestedStart)
    {
        var bestPrimary = -1;
        var bestSuggested = -1;
        var bestDistance = int.MaxValue;
        for (var primaryIndex = primaryStart; primaryIndex < primary.Length; primaryIndex++)
        for (var suggestedIndex = suggestedStart; suggestedIndex < suggested.Length; suggestedIndex++)
        {
            if (primary[primaryIndex] != suggested[suggestedIndex]) continue;
            var distance = primaryIndex - primaryStart + suggestedIndex - suggestedStart;
            if (distance >= bestDistance) continue;
            bestPrimary = primaryIndex;
            bestSuggested = suggestedIndex;
            bestDistance = distance;
        }
        return (bestPrimary, bestSuggested);
    }

    private static int? FindAnchorWordOrdinal(string primary, int primaryIndex, IReadOnlyList<OcrWord> paddleWords)
    {
        if (paddleWords.Count == 0) return null;
        var offset = 0;
        for (var index = 0; index < paddleWords.Count; index++)
        {
            offset += paddleWords[index].Text.Length;
            if (primaryIndex < offset) return index;
        }
        return paddleWords.Count - 1;
    }

    private static void AddSupplementaryGap(string primary, string auxiliary, int primaryStart, int primaryEnd, int auxiliaryStart, int auxiliaryEnd, Dictionary<int, StringBuilder> insertions, Dictionary<int, string> replacements, bool hasReliableContext)
    {
        var primaryGap = primary.AsSpan(primaryStart, primaryEnd - primaryStart);
        var auxiliaryGap = auxiliary.AsSpan(auxiliaryStart, auxiliaryEnd - auxiliaryStart);
        if (!hasReliableContext || auxiliaryGap.IsEmpty) return;
        if (IsKatakanaLongVowelReplacement(primaryGap, auxiliaryGap))
        {
            replacements[primaryStart] = auxiliaryGap.ToString();
            return;
        }
        if (ContainsOnlyWhitespace(primaryGap)
            && auxiliaryGap.Length > 1
            && auxiliaryGap[0] == '\u30FC'
            && auxiliaryGap[1] == '\u300D'
            && IsMixedScriptQuotedTerm(auxiliary, auxiliaryStart))
        {
            if (auxiliaryGap.Length >= 4 && auxiliaryGap[2] == '\u300D' && auxiliaryGap[3] == '\u300C')
                AddInsertion(insertions, primaryStart, "\u300D\u300C".AsSpan());
            else
                AddInsertion(insertions, primaryStart, auxiliaryGap[1..]);
            return;
        }
        if (ContainsOnlyWhitespace(primaryGap) && ContainsOnlySupplementaryCharacters(auxiliaryGap))
        {
            AddInsertion(insertions, primaryStart, auxiliaryGap);
            return;
        }

        if (ContainsOnlyWhitespace(primaryGap)
            && auxiliaryGap.Length == 2
            && auxiliaryGap[0] == '\u300D'
            && IsKatakana(auxiliaryGap[1])
            && HasClosingQuoteAhead(auxiliary, auxiliaryEnd))
        {
            AddInsertion(insertions, primaryStart, "\u300D\u300C".AsSpan());
            return;
        }

        if (ContainsOnlyWhitespace(primaryGap) && auxiliaryGap.Length == 1 && auxiliaryGap[0] == '\u4E00' && HasClosingQuoteAhead(auxiliary, auxiliaryEnd))
        {
            AddInsertion(insertions, primaryStart, "\u300C".AsSpan());
            return;
        }

        if (ContainsOnlyWhitespace(primaryGap)
            && auxiliaryGap.Length > 1
            && auxiliaryGap[^1] == '\u4E00'
            && ContainsOnlySupplementaryCharacters(auxiliaryGap[..^1])
            && HasClosingQuoteAhead(auxiliary, auxiliaryEnd))
        {
            AddInsertion(insertions, primaryStart, auxiliaryGap[..^1]);
            AddInsertion(insertions, primaryStart, "\u300C".AsSpan());
            return;
        }

        if (primaryGap.Length <= MaximumTrustedSupplementaryGapLength
            && auxiliaryGap.Length <= MaximumTrustedSupplementaryGapLength
            && TryAddTrustedSupplementaryGap(primary, primaryGap, auxiliaryGap, primaryStart, primaryEnd, insertions))
            return;

        var leadingSupplementaryCharacterCount = LeadingSupplementaryCharacterCount(auxiliaryGap);
        if (primaryGap.Length == auxiliaryGap.Length - leadingSupplementaryCharacterCount && leadingSupplementaryCharacterCount > 0 && ContainsOnlyOpeningQuoteMarkers(auxiliaryGap[..leadingSupplementaryCharacterCount]))
            AddInsertion(insertions, primaryStart, auxiliaryGap[..leadingSupplementaryCharacterCount]);
    }

    private static bool TryAddTrustedSupplementaryGap(string primary, ReadOnlySpan<char> primaryGap, ReadOnlySpan<char> auxiliaryGap, int primaryStart, int primaryEnd, Dictionary<int, StringBuilder> insertions)
    {
        if (primaryGap.IsEmpty)
        {
            if (primaryStart > 0
                && primaryEnd < primary.Length
                && IsKatakana(primary[primaryStart - 1])
                && IsKatakana(primary[primaryEnd])
                && auxiliaryGap.Contains('\u30FC'))
            {
                AddInsertion(insertions, primaryStart, "\u30FC".AsSpan());
                return true;
            }
            if (primaryEnd == primary.Length && auxiliaryGap.Contains('\u300D'))
            {
                AddInsertion(insertions, primaryStart, "\u300D".AsSpan());
                return true;
            }
            return false;
        }

        var leadingSupplementaryCharacterCount = LeadingSupplementaryCharacterCount(auxiliaryGap);
        if (primaryGap.Length > 2
            || leadingSupplementaryCharacterCount == 0
            || !auxiliaryGap[..leadingSupplementaryCharacterCount].Contains('\u300C')) return false;
        AddInsertion(insertions, primaryStart, auxiliaryGap[..leadingSupplementaryCharacterCount]);
        return true;
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

    private static bool IsKatakanaLongVowelReplacement(ReadOnlySpan<char> primary, ReadOnlySpan<char> auxiliary) =>
        primary.Length == 1
        && auxiliary.Length == 2
        && IsCjkIdeograph(primary[0])
        && auxiliary[0] is >= '\u30A1' and <= '\u30FA'
        && auxiliary[1] == '\u30FC';

    private static bool IsCjkIdeograph(char character) =>
        character is >= '\u3400' and <= '\u4DBF'
        || character is >= '\u4E00' and <= '\u9FFF';

    private static bool IsKatakana(char character) => character is >= '\u30A1' and <= '\u30FA';

    private static bool IsMixedScriptQuotedTerm(string text, int termEnd)
    {
        var openingQuote = text.LastIndexOf('\u300C', termEnd - 1);
        if (openingQuote < 0) return false;
        var term = text.AsSpan(openingQuote + 1, termEnd - openingQuote - 1);
        var hasHiragana = false;
        var hasKatakana = false;
        foreach (var character in term)
        {
            hasHiragana |= character is >= '\u3041' and <= '\u3096';
            hasKatakana |= IsKatakana(character);
        }
        return hasHiragana && hasKatakana;
    }

    private static string RecoverQuotedKatakanaTitleAfterNiyoru(string text)
    {
        var closingQuoteRun = text.IndexOf("\u300D\u300D\u300D", StringComparison.Ordinal);
        if (closingQuoteRun < 0) return text;
        var introducer = text.LastIndexOf("\u306B\u3088\u308B", closingQuoteRun, StringComparison.Ordinal);
        if (introducer < 0) return text;
        var titleStart = introducer + "\u306B\u3088\u308B".Length;
        if (titleStart >= closingQuoteRun || !IsKatakana(text[titleStart])) return text;
        if (text.AsSpan(titleStart, closingQuoteRun - titleStart).Contains('\u300C')) return text;

        var repaired = new StringBuilder(text.Length - 1);
        repaired.Append(text, 0, titleStart);
        repaired.Append('\u300C');
        repaired.Append(text, titleStart, closingQuoteRun - titleStart);
        repaired.Append('\u300D');
        repaired.Append(text, closingQuoteRun + 3, text.Length - closingQuoteRun - 3);
        return repaired.ToString();
    }

    private static string NormalizeSafeDuplicateQuotes(string text)
    {
        text = text.Replace("\u300C\u300C", "\u300C");
        var normalized = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\u300D'
                && index + 2 < text.Length
                && text[index + 1] == '\u300D'
                && IsBodyTextCharacter(text[index + 2]))
            {
                normalized.Append('\u300D');
                index++;
                continue;
            }
            normalized.Append(text[index]);
        }
        return normalized.ToString();
    }

    private static bool IsBodyTextCharacter(char character) =>
        character is >= '\u3041' and <= '\u3096'
        || IsKatakana(character)
        || IsCjkIdeograph(character);

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
