using System.Text;
using TateScribe.Core.Denden;
using TateScribe.Core.Export;
using TateScribe.Core.Ruby;

namespace TateScribe.Infrastructure.Denden;

public sealed class DendenExportService : IDendenExportService
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public async Task ExportAsync(
        StructuredDocument document,
        DendenExportOptions options,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(destinationDirectory) || File.Exists(destinationDirectory))
            throw new IOException($"出力先は既に存在します: {destinationDirectory}");
        ValidateGlobalRubies(document, options.ApprovedGlobalRubies);
        Directory.CreateDirectory(destinationDirectory);
        try
        {
            await WriteAsync("book.md", options.SplitByChapter ? string.Empty : BuildBook(document));
            if (options.SplitByChapter)
            {
                var chapters = SplitByChapter(document);
                for (var index = 0; index < chapters.Count; index++)
                    await WriteAsync($"chapter-{index + 1:000}.md", BuildBook(
                        document with { Paragraphs = chapters[index] }));
            }
            await WriteAsync("ddconv.yml", BuildYaml(options));
            await WriteAsync("default.css", BuildCss(options.VerticalWriting));
            await WriteAsync("README.txt", Readme);
            if (!string.IsNullOrWhiteSpace(options.CoverImagePath))
                File.Copy(options.CoverImagePath, Path.Combine(destinationDirectory, "cover.jpg"));
            if (options.IllustrationImagePaths is { Count: > 0 })
            {
                var imagesDirectory = Path.Combine(destinationDirectory, "images");
                Directory.CreateDirectory(imagesDirectory);
                for (var index = 0; index < options.IllustrationImagePaths.Count; index++)
                {
                    var source = options.IllustrationImagePaths[index];
                    var extension = Path.GetExtension(source).ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(extension)) extension = ".png";
                    File.Copy(
                        source,
                        Path.Combine(imagesDirectory, $"illustration-{index + 1:000}{extension}"));
                }
            }
            if (options.ApprovedGlobalRubies is { Count: > 0 })
                await WriteAsync("ruby.csv", string.Join("\n", options.ApprovedGlobalRubies
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{Csv(pair.Key)},{Csv(pair.Value)}")) + "\n");
        }
        catch
        {
            if (Directory.Exists(destinationDirectory))
                Directory.Delete(destinationDirectory, recursive: true);
            throw;
        }

        async Task WriteAsync(string fileName, string text)
        {
            var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            await File.WriteAllTextAsync(Path.Combine(destinationDirectory, fileName), normalized, Utf8NoBom, cancellationToken);
        }
    }

    private static string BuildBook(StructuredDocument document)
    {
        var result = new StringBuilder();
        foreach (var paragraph in document.Paragraphs)
        {
            var prefix = paragraph.Role switch
            {
                DocumentElementRole.ChapterTitle => "# ",
                DocumentElementRole.SectionTitle or DocumentElementRole.SectionNumber => "## ",
                DocumentElementRole.SceneBreak => "***",
                _ => string.Empty,
            };
            if (paragraph.Role == DocumentElementRole.SceneBreak)
                result.AppendLine(prefix);
            else
            {
                result.Append(prefix);
                foreach (var inline in paragraph.Inlines)
                {
                    if (inline is TextInline text) result.Append(Escape(text.Text));
                    else if (inline is RubyInline ruby)
                        result.Append('{').Append(EscapeRuby(ruby.BaseText)).Append('|').Append(EscapeRuby(ruby.Reading)).Append('}');
                }
                result.AppendLine();
            }
            result.AppendLine();
        }
        return result.ToString();
    }

    private static IReadOnlyList<IReadOnlyList<StructuredParagraph>> SplitByChapter(StructuredDocument document)
    {
        var result = new List<IReadOnlyList<StructuredParagraph>>();
        var current = new List<StructuredParagraph>();
        foreach (var paragraph in document.Paragraphs)
        {
            if (paragraph.Role == DocumentElementRole.ChapterTitle && current.Count > 0)
            {
                result.Add(current);
                current = [];
            }
            current.Add(paragraph);
        }
        if (current.Count > 0) result.Add(current);
        return result;
    }

    private static string BuildYaml(DendenExportOptions options) => $"""
        titles:
          - {Yaml(options.Title)}
        creators:
          - {Yaml(options.Creator)}
        language: {Yaml(options.Language)}
        pageDirection: {(options.VerticalWriting ? "rtl" : "ltr")}
        options:
          titlePage: {options.GenerateTitlePage.ToString().ToLowerInvariant()}
          tableOfContents: {options.GenerateTableOfContents.ToString().ToLowerInvariant()}
          tableOfContentsDepth: {options.TableOfContentsDepth}
          autoTcy: {options.AutoTcy.ToString().ToLowerInvariant()}
          tcyDigitCount: {options.TcyDigitCount}
        """;

    private static string BuildCss(bool verticalWriting) => $$"""
        @charset "UTF-8";
        body { writing-mode: {{(verticalWriting ? "vertical-rl" : "horizontal-tb")}}; }
        ruby rt { font-size: 0.5em; }
        """;

    private static string Escape(string value)
    {
        var result = value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("{", "\\{", StringComparison.Ordinal)
            .Replace("}", "\\}", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);
        if (result.StartsWith('#') || result.StartsWith('>') || result.StartsWith('-')
            || result.StartsWith('*') || result.StartsWith('+'))
            result = "\\" + result;
        return result.Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal)
            .Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("!", "\\!", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal);
    }

    private static string EscapeRuby(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("{", "\\{", StringComparison.Ordinal)
            .Replace("}", "\\}", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);

    private static void ValidateGlobalRubies(
        StructuredDocument document,
        IReadOnlyDictionary<string, string>? approved)
    {
        if (approved is null) return;
        var readings = document.Paragraphs
            .SelectMany(paragraph => paragraph.Inlines.OfType<RubyInline>())
            .GroupBy(ruby => ruby.BaseText, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(ruby => ruby.Reading).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        foreach (var pair in approved)
            if (readings.TryGetValue(pair.Key, out var values) && (values.Length != 1 || values[0] != pair.Value))
                throw new InvalidOperationException($"「{pair.Key}」は複数の読み、または承認内容と異なる読みを含むためruby.csvへ出力できません。");
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    private static string Yaml(string value) => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private const string Readme = """
        このフォルダーを、でんでんコンバーターへ渡してください。
        book.mdはTateScribeが確定本文と確定ルビから生成したものです。
        原書スクリーンショットは電子書籍本文に含まれていません。
        """;
}
