using System.Text;

namespace TateScribe.Core.Layout;

public static class PunctuationMerger
{
    private const string Punctuation = "、。！？";

    public static string Merge(string primary, string auxiliary, int lookAhead)
    {
        if (string.IsNullOrEmpty(primary) || string.IsNullOrEmpty(auxiliary)) return primary;
        var insertions = new Dictionary<int, StringBuilder>();
        var primaryIndex = 0;
        var previousMatch = -1;
        var matchedCount = 0;
        var pendingPunctuation = new StringBuilder();

        foreach (var character in auxiliary)
        {
            if (Punctuation.Contains(character))
            {
                pendingPunctuation.Append(character);
                continue;
            }

            var match = primary.IndexOf(character, primaryIndex, Math.Min(lookAhead, primary.Length - primaryIndex));
            if (match < 0)
            {
                pendingPunctuation.Clear();
                continue;
            }

            if (pendingPunctuation.Length > 0 && previousMatch >= 0)
            {
                if (!insertions.TryGetValue(previousMatch + 1, out var punctuation)) insertions[previousMatch + 1] = punctuation = new StringBuilder();
                punctuation.Append(pendingPunctuation);
            }
            pendingPunctuation.Clear();
            previousMatch = match;
            primaryIndex = match + 1;
            matchedCount++;
        }

        if (pendingPunctuation.Length > 0 && previousMatch == primary.Length - 1 && matchedCount == primary.Length)
        {
            insertions[primary.Length] = pendingPunctuation;
        }

        var result = new StringBuilder(primary.Length + insertions.Sum(pair => pair.Value.Length));
        for (var index = 0; index <= primary.Length; index++)
        {
            if (insertions.TryGetValue(index, out var punctuation)) result.Append(punctuation);
            if (index < primary.Length) result.Append(primary[index]);
        }
        return result.ToString();
    }
}
