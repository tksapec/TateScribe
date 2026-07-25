using System.Text.RegularExpressions;

namespace TateScribe.Core.Proofreading;

public static partial class ProofreadingImportParser
{
    [GeneratedRegex("^\\[\\[PAGE:(?<marker>\\d{4})\\]\\]$")]
    private static partial Regex PageMarkerPattern();

    public static ProofreadingImportDocument Parse(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var format = ReadHeader(lines, "TATESCRIBE_FORMAT");
        var project = ReadHeader(lines, "PROJECT_ID");
        var batch = ReadHeader(lines, "BATCH_ID");
        if (!int.TryParse(format, out var formatVersion) || formatVersion != 1)
            throw new InvalidDataException("The proofreading text has an unsupported or missing TateScribe format header.");
        if (!Guid.TryParse(project, out var projectId) || !Guid.TryParse(batch, out var batchId))
            throw new InvalidDataException("The proofreading text is missing a valid project or batch identifier.");

        var pages = new List<ProofreadingImportPage>();
        string? currentMarker = null;
        var text = new List<string>();
        foreach (var line in lines)
        {
            var match = PageMarkerPattern().Match(line.Trim());
            if (match.Success)
            {
                AddPage(pages, currentMarker, text);
                currentMarker = match.Groups["marker"].Value;
                text.Clear();
                continue;
            }
            if (currentMarker is not null && !IsPageMetadata(line)) text.Add(line);
        }
        AddPage(pages, currentMarker, text);
        if (pages.Count == 0) throw new InvalidDataException("The proofreading text contains no page markers.");
        return new ProofreadingImportDocument(formatVersion, projectId, batchId, pages);
    }

    private static string? ReadHeader(IEnumerable<string> lines, string name)
    {
        var prefix = $"[[{name}:";
        return lines.Select(line => line.Trim())
            .Where(line => line.StartsWith(prefix, StringComparison.Ordinal) && line.EndsWith("]]", StringComparison.Ordinal))
            .Select(line => line[prefix.Length..^2])
            .FirstOrDefault();
    }

    private static bool IsPageMetadata(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith("[[SOURCE_FILE:", StringComparison.Ordinal)
            || trimmed.StartsWith("[[PAGE_ROLE:", StringComparison.Ordinal)
            || trimmed.StartsWith("[[DISPLAY_PROFILE:", StringComparison.Ordinal)
            || trimmed.StartsWith("[[TATESCRIBE_FORMAT:", StringComparison.Ordinal)
            || trimmed.StartsWith("[[PROJECT_ID:", StringComparison.Ordinal)
            || trimmed.StartsWith("[[BATCH_ID:", StringComparison.Ordinal);
    }

    private static void AddPage(ICollection<ProofreadingImportPage> pages, string? marker, IEnumerable<string> lines)
    {
        if (marker is null) return;
        pages.Add(new ProofreadingImportPage(marker, string.Join("\n", lines).Trim()));
    }
}
