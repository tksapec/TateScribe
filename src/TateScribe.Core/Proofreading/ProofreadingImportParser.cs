using System.Text.RegularExpressions;

namespace TateScribe.Core.Proofreading;

public static partial class ProofreadingImportParser
{
    [GeneratedRegex("^\\[\\[PAGE:(?<marker>\\d{4})\\]\\]$")]
    private static partial Regex PageMarkerPattern();

    [GeneratedRegex("^\\[\\[JOIN_TO_NEXT:(?<join>[A-Za-z]+)\\]\\]$")]
    private static partial Regex JoinMarkerPattern();

    public static ProofreadingImportDocument Parse(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        var lines = RemoveOuterMarkdownFence(NormalizeNewlines(content)).Split('\n');
        var formatText = ReadSingleHeader(lines, "TATESCRIBE_FORMAT");
        var projectText = ReadSingleHeader(lines, "PROJECT_ID");
        var batchText = ReadSingleHeader(lines, "BATCH_ID");
        if (!int.TryParse(formatText, out var formatVersion) || formatVersion is not (1 or 2))
            throw new InvalidDataException("The proofreading text has an unsupported or missing TateScribe format header.");
        if (!Guid.TryParse(projectText, out var projectId) || !Guid.TryParse(batchText, out var batchId))
            throw new InvalidDataException("The proofreading text is missing a valid project or batch identifier.");

        return formatVersion == 1
            ? ParseFormat1(lines, projectId, batchId)
            : ParseFormat2(lines, projectId, batchId);
    }

    private static ProofreadingImportDocument ParseFormat1(string[] lines, Guid projectId, Guid batchId)
    {
        var pages = new List<ProofreadingImportPage>();
        string? currentMarker = null;
        var text = new List<string>();
        foreach (var line in lines)
        {
            var match = PageMarkerPattern().Match(line.Trim());
            if (match.Success)
            {
                AddFormat1Page(pages, currentMarker, text);
                currentMarker = match.Groups["marker"].Value;
                text.Clear();
                continue;
            }
            if (currentMarker is not null && !IsFormat1Metadata(line)) text.Add(line);
        }
        AddFormat1Page(pages, currentMarker, text);
        if (pages.Count == 0) throw new InvalidDataException("The proofreading text contains no page markers.");
        return new ProofreadingImportDocument(1, projectId, batchId, pages);
    }

    private static ProofreadingImportDocument ParseFormat2(string[] lines, Guid projectId, Guid batchId)
    {
        var pages = new List<ProofreadingImportPage>();
        var seenMarkers = new HashSet<string>(StringComparer.Ordinal);
        var joinedMarkers = new HashSet<string>(StringComparer.Ordinal);
        string? currentMarker = null;
        List<string>? currentText = null;
        List<string>? report = null;
        var reportClosed = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (currentText is not null)
            {
                if (trimmed == "[[TEXT_END]]")
                {
                    pages.Add(new ProofreadingImportPage(currentMarker!, string.Join("\n", currentText)));
                    currentText = null;
                    continue;
                }
                if (trimmed == "[[TEXT_BEGIN]]")
                    throw new InvalidDataException($"Nested or duplicate structural marker inside TEXT for PAGE {currentMarker}.");
                if (JoinMarkerPattern().IsMatch(trimmed))
                    throw new InvalidDataException($"JOIN_TO_NEXT must appear after TEXT_END for PAGE {currentMarker}.");
                if (trimmed is "[[REPORT_BEGIN]]" or "[[REPORT_END]]" || PageMarkerPattern().IsMatch(trimmed))
                    throw new InvalidDataException($"Missing TEXT_END for PAGE {currentMarker}.");
                currentText.Add(line);
                continue;
            }

            if (report is not null && !reportClosed)
            {
                if (trimmed == "[[REPORT_END]]")
                {
                    reportClosed = true;
                    continue;
                }
                if (trimmed is "[[REPORT_BEGIN]]" or "[[TEXT_BEGIN]]" or "[[TEXT_END]]"
                    || PageMarkerPattern().IsMatch(trimmed))
                    throw new InvalidDataException("Nested or duplicate structural marker inside REPORT.");
                report.Add(line);
                continue;
            }

            var pageMatch = PageMarkerPattern().Match(trimmed);
            if (pageMatch.Success)
            {
                if (report is not null) throw new InvalidDataException("PAGE marker cannot appear after REPORT_BEGIN.");
                currentMarker = pageMatch.Groups["marker"].Value;
                if (!seenMarkers.Add(currentMarker))
                    throw new InvalidDataException($"Duplicate PAGE marker: {currentMarker}.");
                continue;
            }

            if (trimmed == "[[TEXT_BEGIN]]")
            {
                if (currentMarker is null) throw new InvalidDataException("TEXT_BEGIN must follow a PAGE marker.");
                if (pages.Any(page => page.PageMarker == currentMarker))
                    throw new InvalidDataException($"Duplicate TEXT block for PAGE {currentMarker}.");
                currentText = [];
                continue;
            }

            var joinMatch = JoinMarkerPattern().Match(trimmed);
            if (joinMatch.Success)
            {
                if (currentMarker is null || pages.Count == 0 || pages[^1].PageMarker != currentMarker)
                    throw new InvalidDataException("JOIN_TO_NEXT must follow a completed page text block.");
                if (!joinedMarkers.Add(currentMarker))
                    throw new InvalidDataException($"Duplicate JOIN_TO_NEXT marker for PAGE {currentMarker}.");
                if (!Enum.TryParse<BoundaryJoinType>(joinMatch.Groups["join"].Value, out var join))
                    throw new InvalidDataException($"Unknown JOIN_TO_NEXT value: {joinMatch.Groups["join"].Value}.");
                pages[^1] = pages[^1] with { JoinToNext = join };
                continue;
            }

            if (trimmed == "[[REPORT_BEGIN]]")
            {
                if (report is not null) throw new InvalidDataException("Duplicate REPORT_BEGIN marker.");
                report = [];
                currentMarker = null;
                continue;
            }

            if (trimmed is "[[TEXT_END]]" or "[[REPORT_END]]")
                throw new InvalidDataException($"Unexpected closing marker {trimmed}.");
            if (string.IsNullOrWhiteSpace(line) || IsHeader(trimmed)) continue;
            throw new InvalidDataException($"Unexpected text outside PAGE/REPORT blocks: {line}");
        }

        if (currentText is not null) throw new InvalidDataException($"Missing TEXT_END for PAGE {currentMarker}.");
        if (report is not null && !reportClosed) throw new InvalidDataException("Missing REPORT_END.");
        if (report is null) throw new InvalidDataException("Missing REPORT_BEGIN/REPORT_END block.");
        if (pages.Count == 0) throw new InvalidDataException("The proofreading text contains no page markers.");
        if (seenMarkers.Any(marker => pages.All(page => page.PageMarker != marker)))
            throw new InvalidDataException("Every PAGE requires exactly one TEXT_BEGIN/TEXT_END pair.");
        var missingJoin = pages.FirstOrDefault(page => !joinedMarkers.Contains(page.PageMarker));
        if (missingJoin is not null)
            throw new InvalidDataException($"Every PAGE requires exactly one JOIN_TO_NEXT marker; missing for PAGE {missingJoin.PageMarker}.");

        return new ProofreadingImportDocument(2, projectId, batchId, pages, report is null ? string.Empty : string.Join("\n", report));
    }

    private static string NormalizeNewlines(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string RemoveOuterMarkdownFence(string content)
    {
        var lines = content.Split('\n').ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0);
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);
        if (lines.Count >= 2 && lines[0].TrimStart().StartsWith("```", StringComparison.Ordinal)
            && lines[^1].Trim() == "```")
        {
            lines.RemoveAt(lines.Count - 1);
            lines.RemoveAt(0);
        }
        return string.Join("\n", lines);
    }

    private static string? ReadSingleHeader(IEnumerable<string> lines, string name)
    {
        var prefix = $"[[{name}:";
        var values = lines.Select(line => line.Trim())
            .Where(line => line.StartsWith(prefix, StringComparison.Ordinal) && line.EndsWith("]]", StringComparison.Ordinal))
            .Select(line => line[prefix.Length..^2])
            .ToArray();
        if (values.Length > 1) throw new InvalidDataException($"Duplicate {name} header.");
        return values.SingleOrDefault();
    }

    private static bool IsHeader(string line) =>
        line.StartsWith("[[TATESCRIBE_FORMAT:", StringComparison.Ordinal)
        || line.StartsWith("[[PROJECT_ID:", StringComparison.Ordinal)
        || line.StartsWith("[[BATCH_ID:", StringComparison.Ordinal);

    private static bool IsFormat1Metadata(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith("[[SOURCE_FILE:", StringComparison.Ordinal)
            || trimmed.StartsWith("[[PAGE_ROLE:", StringComparison.Ordinal)
            || trimmed.StartsWith("[[DISPLAY_PROFILE:", StringComparison.Ordinal)
            || IsHeader(trimmed);
    }

    private static void AddFormat1Page(ICollection<ProofreadingImportPage> pages, string? marker, IEnumerable<string> lines)
    {
        if (marker is null) return;
        pages.Add(new ProofreadingImportPage(marker, string.Join("\n", lines).Trim()));
    }
}
