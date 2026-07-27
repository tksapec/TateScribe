using System.Text;
using System.Net;
using TateScribe.Core.Denden;
using TateScribe.Core.Export;
using TateScribe.Core.Ruby;

namespace TateScribe.Infrastructure.Denden;

public sealed class DendenExportService : IDendenExportService
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly Dictionary<Guid, PreparedExport> preparedPlans = [];

    public void Validate(
        DendenExportDocument exportDocument,
        DendenExportOptions options)
    {
        _ = PrepareExport(exportDocument, options);
    }

    public IReadOnlyList<ExportPreflightIssue> Inspect(
        DendenExportDocument exportDocument,
        DendenExportOptions options)
    {
        try
        {
            Validate(exportDocument, options);
            return [];
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException or InvalidDataException or IOException)
        {
            return
            [
                new ExportPreflightIssue(
                    "DendenValidationFailed",
                    exception.Message,
                    true),
            ];
        }
    }

    public DendenExportPlan Prepare(
        DendenExportDocument exportDocument,
        DendenExportOptions options)
    {
        try
        {
            var planId = Guid.NewGuid();
            lock (preparedPlans)
                preparedPlans.Add(planId, PrepareExport(exportDocument, options));
            return new DendenExportPlan(planId, []);
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException or InvalidDataException or IOException)
        {
            return new DendenExportPlan(Guid.Empty,
            [new ExportPreflightIssue("DendenValidationFailed", exception.Message, true)]);
        }
    }

    public Task ExportAsync(
        DendenExportPlan plan,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        PreparedExport prepared;
        lock (preparedPlans)
        {
            if (!preparedPlans.Remove(plan.PlanId, out prepared!))
                throw new InvalidOperationException("The Denden export plan is missing or has already been used.");
        }
        return WritePreparedExportAsync(prepared, destinationDirectory, cancellationToken);
    }

    public async Task ExportAsync(
        StructuredDocument document,
        DendenExportOptions options,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        var blocks = new List<DendenContentBlock>(
            document.Paragraphs.Select(paragraph =>
                (DendenContentBlock)new DendenParagraphBlock(paragraph)));
        blocks.AddRange((options.IllustrationImagePaths ?? [])
            .Select((source, index) => new DendenIllustrationBlock(
                new DendenIllustration(
                    Guid.Empty,
                    int.MaxValue,
                    source,
                    $"挿絵 {index + 1}"))));
        await ExportAsync(
            new DendenExportDocument(document, blocks),
            options with { IllustrationImagePaths = null },
            destinationDirectory,
            cancellationToken);
    }

    public async Task ExportAsync(
        DendenExportDocument exportDocument,
        DendenExportOptions options,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        await WritePreparedExportAsync(
            PrepareExport(exportDocument, options), destinationDirectory, cancellationToken);
    }

    private static async Task WritePreparedExportAsync(
        PreparedExport prepared,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(destinationDirectory) || File.Exists(destinationDirectory))
            throw new IOException($"出力先は既に存在します: {destinationDirectory}");
        var uploadDirectory = Path.Combine(destinationDirectory, "upload");
        try
        {
            Directory.CreateDirectory(destinationDirectory);
            Directory.CreateDirectory(uploadDirectory);
            if (!prepared.Options.SplitByChapter)
                await WriteAsync(
                    "book.md",
                    BuildBook(prepared.Document.Blocks, prepared.Illustrations, prepared.Options.DisplayIllustrationList));
            if (prepared.Options.SplitByChapter)
            {
                for (var index = 0; index < prepared.Chapters.Count; index++)
                    await WriteAsync(
                        $"chapter-{index + 1:000}.md",
                        BuildBook(prepared.Chapters[index], prepared.Illustrations, prepared.Options.DisplayIllustrationList));
            }
            await WriteAsync("ddconv.yml", BuildYaml(prepared.Options));
            await WriteAsync("default.css", BuildCss(prepared.Options.VerticalWriting));
            await File.WriteAllTextAsync(Path.Combine(destinationDirectory, "README.txt"), Readme, Utf8NoBom, cancellationToken);
            if (prepared.Cover is not null)
                await File.WriteAllBytesAsync(
                    Path.Combine(uploadDirectory, prepared.Cover.FileName), prepared.Cover.Bytes, cancellationToken);
            foreach (var illustration in prepared.Illustrations)
                await File.WriteAllBytesAsync(
                    Path.Combine(uploadDirectory, illustration.Image.FileName),
                    illustration.Image.Bytes,
                    cancellationToken);
            if (prepared.Options.ApprovedGlobalRubies is { Count: > 0 })
                await WriteAsync("ruby.csv", string.Join("\n", prepared.Options.ApprovedGlobalRubies
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
            await File.WriteAllTextAsync(Path.Combine(uploadDirectory, fileName), normalized, Utf8NoBom, cancellationToken);
        }
    }

    private static PreparedExport PrepareExport(
        DendenExportDocument exportDocument,
        DendenExportOptions options)
    {
        options.Validate();
        if (exportDocument.Blocks.Count == 0)
            throw new InvalidOperationException("本文または挿絵がないため、Markdownファイルを出力できません。");
        ValidateGlobalRubies(exportDocument.Document, options.ApprovedGlobalRubies);
        var chapters = options.SplitByChapter ? SplitBlocksByChapter(exportDocument.Blocks) : [];
        var cover = string.IsNullOrWhiteSpace(options.CoverImagePath)
            ? null
            : DendenImageProcessor.Prepare(options.CoverImagePath, "cover");
        var illustrations = exportDocument.Blocks.OfType<DendenIllustrationBlock>()
            .Select((block, index) => new PreparedIllustration(
                block,
                DendenImageProcessor.Prepare(
                    block.Illustration.SourcePath,
                    $"illustration-{index + 1:000}",
                    block.Illustration.Crop,
                    block.Illustration.RotationDegrees)))
            .ToArray();
        var outputFileCount = (options.SplitByChapter ? chapters.Count : 1)
            + 2
            + (cover is null ? 0 : 1)
            + illustrations.Length
            + (options.ApprovedGlobalRubies is { Count: > 0 } ? 1 : 0);
        if (outputFileCount > 100)
            throw new InvalidOperationException(
                $"出力ファイル数は{outputFileCount}件です。でんでんコンバーターの上限100件を超えています。");
        return new PreparedExport(exportDocument, options, chapters, cover, illustrations);
    }

    private static string BuildBook(
        IReadOnlyList<DendenContentBlock> blocks,
        IReadOnlyList<PreparedIllustration> illustrations,
        bool displayIllustrationList)
    {
        var result = new StringBuilder();
        foreach (var block in blocks)
        {
            if (block is DendenIllustrationBlock illustrationBlock)
            {
                var prepared = illustrations.Single(item =>
                    ReferenceEquals(item.Block, illustrationBlock));
                if (displayIllustrationList)
                {
                    var caption = string.IsNullOrWhiteSpace(illustrationBlock.Illustration.Caption)
                        ? illustrationBlock.Illustration.AltText
                        : illustrationBlock.Illustration.Caption;
                    result.AppendLine("<figure class=\"illustration\">")
                        .Append("  <img src=\"").Append(WebUtility.HtmlEncode(prepared.Image.FileName))
                        .Append("\" alt=\"").Append(WebUtility.HtmlEncode(illustrationBlock.Illustration.AltText))
                        .AppendLine("\">")
                        .Append("  <figcaption>").Append(WebUtility.HtmlEncode(caption))
                        .AppendLine("</figcaption>")
                        .AppendLine("</figure>");
                }
                else
                {
                    result.Append("![").Append(EscapeImageAlt(illustrationBlock.Illustration.AltText))
                        .Append("](").Append(prepared.Image.FileName).AppendLine(")");
                    if (!string.IsNullOrWhiteSpace(illustrationBlock.Illustration.Caption))
                        result.Append(Escape(illustrationBlock.Illustration.Caption)).AppendLine();
                }
                result.AppendLine();
                continue;
            }
            var paragraph = ((DendenParagraphBlock)block).Paragraph;
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

    private static IReadOnlyList<IReadOnlyList<DendenContentBlock>> SplitBlocksByChapter(
        IReadOnlyList<DendenContentBlock> blocks)
    {
        var result = new List<IReadOnlyList<DendenContentBlock>>();
        var current = new List<DendenContentBlock>();
        foreach (var block in blocks)
        {
            if (block is DendenParagraphBlock
                {
                    Paragraph.Role: DocumentElementRole.ChapterTitle,
                }
                && current.Count > 0)
            {
                result.Add(current);
                current = [];
            }
            current.Add(block);
        }
        if (current.Count > 0) result.Add(current);
        return result;
    }

    private static string BuildYaml(DendenExportOptions options) => $"""
        ddconvVersion: 1.0
        titles:
          - content: {Yaml(options.Title.Trim())}
        creators:
          - content: {Yaml(options.Creator.Trim())}
            role: aut
        language: {Yaml(options.EffectiveLanguage)}
        pageDirection: {(options.VerticalWriting ? "rtl" : "ltr")}
        options:
          skipCover: {Boolean(options.SkipCover)}
          titlepage: {Boolean(options.GenerateTitlePage)}
          tocInSpine: {Boolean(options.GenerateTableOfContents)}
          tocDisplayDepth: {options.TableOfContentsDepth}
          displayLandmarksNav: {Boolean(options.DisplayLandmarksNav)}
          displayLoiNav: {Boolean(options.DisplayIllustrationList)}
          autoTcy: {Boolean(options.AutoTcy)}
          tcyDigit: {options.TcyDigitCount}
        """;

    private static string BuildCss(bool verticalWriting) => $$"""
        @charset "UTF-8";
        body { writing-mode: {{(verticalWriting ? "vertical-rl" : "horizontal-tb")}}; }
        ruby rt { font-size: 0.5em; }
        """;

    private static string Escape(string value)
    {
        var result = value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("{", "\\{", StringComparison.Ordinal)
            .Replace("}", "\\}", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);
        if (System.Text.RegularExpressions.Regex.IsMatch(result, @"^\d+\.\s"))
            result = System.Text.RegularExpressions.Regex.Replace(result, @"^(\d+)\.", "$1&#46;");
        else if (result.StartsWith("    ", StringComparison.Ordinal))
            result = "&#32;&#32;&#32;&#32;" + result[4..];
        else if (result.StartsWith('\t'))
            result = "&#9;" + result[1..];
        else if (result.StartsWith('#') || result.StartsWith('>') || result.StartsWith('-')
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

    private static string EscapeImageAlt(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);

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
    private static string Yaml(string value)
    {
        var result = new StringBuilder(value.Length + 2).Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\':
                    result.Append("\\\\");
                    break;
                case '"':
                    result.Append("\\\"");
                    break;
                case '\r':
                    result.Append("\\r");
                    break;
                case '\n':
                    result.Append("\\n");
                    break;
                case '\t':
                    result.Append("\\t");
                    break;
                default:
                    if (char.IsControl(character))
                        result.Append("\\u").Append(((int)character).ToString("X4"));
                    else
                        result.Append(character);
                    break;
            }
        }
        return result.Append('"').ToString();
    }
    private static string Boolean(bool value) => value ? "true" : "false";

    private sealed record PreparedIllustration(
        DendenIllustrationBlock Block,
        PreparedDendenImage Image);

    private sealed record PreparedExport(
        DendenExportDocument Document,
        DendenExportOptions Options,
        IReadOnlyList<IReadOnlyList<DendenContentBlock>> Chapters,
        PreparedDendenImage? Cover,
        IReadOnlyList<PreparedIllustration> Illustrations);

    private const string Readme = """
        Upload instructions

        Open the upload folder. Select every file inside upload and upload those files together.
        Never select or upload this README.txt itself. README.txt is only an instruction file.
        Markdown is generated from TateScribe's confirmed body text and confirmed rubies.
        Original screenshots are not included; only explicitly selected illustrations are included.
        """;
}
