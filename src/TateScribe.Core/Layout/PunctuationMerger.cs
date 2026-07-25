using System.Text;

namespace TateScribe.Core.Layout;

public static class PunctuationMerger
{
    private const string PunctuationAndQuotes = "\u3001\u3002\uFF01\uFF1F\u300C\u300D\u300E\u300F\uFF08\uFF09\u2026";

    public static string Merge(string primary, string auxiliary, int lookAhead)
    {
        if (string.IsNullOrEmpty(primary) || string.IsNullOrEmpty(auxiliary)) return primary;
        var insertions = new Dictionary<int, StringBuilder>();
        var primaryIndex = 0;
        var previousMatch = -1;
        var matchedCount = 0;
        var pendingSupplementaryCharacters = new StringBuilder();

        foreach (var character in auxiliary)
        {
            var match = primary.IndexOf(character, primaryIndex, Math.Min(lookAhead, primary.Length - primaryIndex));
            if (IsSupplementaryCharacter(character) && match < 0)
            {
                pendingSupplementaryCharacters.Append(character);
                continue;
            }

            if (match < 0)
            {
                pendingSupplementaryCharacters.Clear();
                continue;
            }

            if (pendingSupplementaryCharacters.Length > 0)
            {
                var insertionIndex = previousMatch >= 0 ? previousMatch + 1 : match;
                if (!insertions.TryGetValue(insertionIndex, out var supplementaryCharacters))
                    insertions[insertionIndex] = supplementaryCharacters = new StringBuilder();
                supplementaryCharacters.Append(pendingSupplementaryCharacters);
            }
            pendingSupplementaryCharacters.Clear();
            previousMatch = match;
            primaryIndex = match + 1;
            matchedCount++;
        }

        if (pendingSupplementaryCharacters.Length > 0 && previousMatch == primary.Length - 1 && matchedCount == primary.Length)
        {
            insertions[primary.Length] = pendingSupplementaryCharacters;
        }

        var result = new StringBuilder(primary.Length + insertions.Sum(pair => pair.Value.Length));
        for (var index = 0; index <= primary.Length; index++)
        {
            if (insertions.TryGetValue(index, out var punctuation)) result.Append(punctuation);
            if (index < primary.Length) result.Append(primary[index]);
        }
        return result.ToString();
    }

    private static bool IsSupplementaryCharacter(char character) =>
        PunctuationAndQuotes.Contains(character)
        || character is >= '\u3041' and <= '\u3049'
        || character is '\u3063' or >= '\u3083' and <= '\u3087' or '\u308E' or '\u3095' or '\u3096'
        || character is >= '\u30A1' and <= '\u30A9'
        || character is '\u30C3' or >= '\u30E3' and <= '\u30E7' or '\u30EE' or '\u30F5' or '\u30F6';
}
