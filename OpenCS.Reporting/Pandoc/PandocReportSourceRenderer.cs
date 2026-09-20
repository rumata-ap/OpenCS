using System.Security.Cryptography;
using System.Text;

namespace OpenCS.Reporting.Pandoc;

/// <summary>Преобразует нейтральную модель отчёта в Markdown Pandoc и локальные ресурсы.</summary>
public sealed class PandocReportSourceRenderer
{
    /// <summary>Создаёт исходник в указанном временном каталоге.</summary>
    public async Task<PandocReportSource> RenderAsync(
        ReportDocument document,
        string directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        cancellationToken.ThrowIfCancellationRequested();

        string root = Path.GetFullPath(directory);
        string assets = Path.Combine(root, "assets");
        Directory.CreateDirectory(assets);
        var markdown = new StringBuilder();
        markdown.Append("% ").Append(EscapeText(document.Title)).Append("\n\n");
        var assetPaths = new List<string>();
        int imageOrdinal = 0;

        foreach (ReportBlock block in document.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            imageOrdinal = await RenderBlockAsync(markdown, block, assets, assetPaths,
                imageOrdinal, cancellationToken).ConfigureAwait(false);
        }

        string markdownPath = Path.Combine(root, "report.md");
        await File.WriteAllTextAsync(markdownPath, markdown.ToString(), Encoding.UTF8,
            cancellationToken).ConfigureAwait(false);
        return new PandocReportSource(root, markdownPath, assetPaths);
    }

    static async Task<int> RenderBlockAsync(
        StringBuilder markdown,
        ReportBlock block,
        string assets,
        ICollection<string> assetPaths,
        int imageOrdinal,
        CancellationToken cancellationToken)
    {
        switch (block)
        {
            case ReportHeading heading:
                markdown.Append(new string('#', Math.Clamp(heading.Level, 1, 6)))
                    .Append(' ').Append(EscapeText(heading.Text)).Append("\n\n");
                break;
            case ReportParagraph paragraph:
                markdown.Append(EscapeText(paragraph.Text)).Append("\n\n");
                break;
            case ReportKeyValueTable table:
                RenderTable(markdown, [table.KeyHeader, table.ValueHeader],
                    table.Rows.Select(row => (IReadOnlyList<string>)[row.Key, row.Value]).ToList());
                break;
            case ReportTable table:
                RenderTable(markdown, table.Headers, table.Rows);
                break;
            case ReportFormula formula:
                markdown.Append("> **").Append(EscapeText(formula.Reference)).Append("**\n>\n")
                    .Append("> ").Append(ConvertInlineMarkup(formula.Formula)).Append("\n>\n")
                    .Append("> ").Append(ConvertInlineMarkup(formula.Substitution)).Append("\n>\n")
                    .Append("> ").Append(ConvertInlineMarkup(formula.Result)).Append("\n\n");
                break;
            case ReportCalculationStep step:
                markdown.Append("### ").Append(EscapeText(step.Title));
                if (!string.IsNullOrWhiteSpace(step.Reference))
                    markdown.Append(" (").Append(EscapeText(step.Reference)).Append(')');
                markdown.Append("\n\n");
                AppendMath(markdown, step.Formula);
                AppendMath(markdown, step.Substitution);
                AppendMath(markdown, step.Result, step.Unit);
                if (!string.IsNullOrWhiteSpace(step.Note))
                    markdown.Append(EscapeText(step.Note)).Append("\n\n");
                if (!string.IsNullOrWhiteSpace(step.StatusText))
                    markdown.Append("> ").Append(EscapeText(step.StatusText)).Append("\n\n");
                break;
            case ReportImage image:
                if (!SvgSizing.LooksLikeSvg(image.Svg))
                    throw new PandocExportException(
                        PandocExportFailureReason.InvalidAsset,
                        $"Иллюстрация отчёта не является корректным SVG: {image.Name}");
                const string extension = ".svg";
                string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(image.Svg)))
                    [..12].ToLowerInvariant();
                string fileName = $"image-{imageOrdinal++}-{hash}{extension}";
                string fullPath = Path.Combine(assets, fileName);
                await File.WriteAllTextAsync(fullPath, SvgSizing.EnsureExplicitDimensions(image.Svg),
                    Encoding.UTF8, cancellationToken).ConfigureAwait(false);
                assetPaths.Add(fullPath);
                string relativePath = $"assets/{fileName}";
                markdown.Append("![").Append(EscapeText(image.Name)).Append("](")
                    .Append(relativePath).Append(")\n\n");
                break;
            case ReportWarning warning:
                markdown.Append("> ⚠️ ").Append(EscapeText(warning.Text)).Append("\n\n");
                break;
            case ReportPageBreak:
                markdown.Append("::: page-break\n:::\n\n");
                break;
            default:
                throw new InvalidOperationException($"Неподдерживаемый блок отчёта: {block.GetType().Name}");
        }

        return imageOrdinal;
    }

    static void AppendMath(StringBuilder markdown, ReportMathExpression expression, string? unit = null)
    {
        if (string.IsNullOrWhiteSpace(expression.Source)
            && string.IsNullOrWhiteSpace(expression.FallbackText))
            return;

        string value = expression.SourceKind switch
        {
            MathSourceKind.Latex => "$" + expression.Source + "$",
            MathSourceKind.InlineMarkup => ConvertInlineMarkup(expression.Source),
            _ => EscapeText(expression.FallbackText ?? expression.Source)
        };
        markdown.Append(value);
        if (!string.IsNullOrWhiteSpace(unit))
            markdown.Append(' ').Append(EscapeText(unit));
        markdown.Append("\n\n");
    }

    static string ConvertInlineMarkup(string source)
    {
        var result = new StringBuilder();
        foreach (FormulaSegment segment in FormulaMarkup.Parse(source))
        {
            string text = EscapeInline(segment.Text);
            switch (segment.Kind)
            {
                case FormulaSegmentKind.Subscript:
                    result.Append('~').Append(text).Append('~');
                    break;
                case FormulaSegmentKind.Superscript:
                    result.Append('^').Append(text).Append('^');
                    break;
                default:
                    result.Append(text);
                    break;
            }
        }
        return result.ToString();
    }

    static void RenderTable(StringBuilder markdown, IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        markdown.Append("| ").Append(string.Join(" | ", headers.Select(EscapeCell))).Append(" |\n")
            .Append("| ").Append(string.Join(" | ", headers.Select(_ => "---"))).Append(" |\n");
        foreach (IReadOnlyList<string> row in rows)
            markdown.Append("| ").Append(string.Join(" | ", row.Select(EscapeCell))).Append(" |\n");
        markdown.Append('\n');
    }

    static string EscapeText(string? value)
        => string.Join("  \n", (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n').Select(EscapeLine));

    static string EscapeCell(string? value)
        => string.Join("<br>", (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n').Select(line => EscapeLine(line).Replace("|", "\\|")));

    static string EscapeInline(string text)
        => text.Replace("\\", "\\\\").Replace("`", "\\`")
            .Replace("*", "\\*").Replace("_", "\\_")
            .Replace("[", "\\[").Replace("]", "\\]");

    static string EscapeLine(string line)
    {
        var result = new StringBuilder();
        foreach (char c in line)
        {
            switch (c)
            {
                case '&': result.Append("&amp;"); break;
                case '<': result.Append("&lt;"); break;
                case '>': result.Append("&gt;"); break;
                case '\\': result.Append("\\\\"); break;
                case '`': case '*': case '_': case '[': case ']':
                    result.Append('\\').Append(c); break;
                default: result.Append(c); break;
            }
        }
        return result.ToString();
    }
}
