using System.Text;

namespace OpenCS.Reporting;

/// <summary>Рендерер нейтрального документа в самодостаточный Markdown (GFM).
/// Картинки встраиваются как data URI, произвольный текст экранируется так, чтобы
/// не превращаться в разметку и не оставлять видимых обратных слешей.</summary>
public sealed class MarkdownReportRenderer
{
    /// <summary>Преобразует документ в Markdown.</summary>
    public string Render(ReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var md = new StringBuilder();
        md.Append("# ").Append(EscapeText(document.Title)).Append("\n\n");

        foreach (var block in document.Blocks)
            RenderBlock(md, block);

        return md.ToString();
    }

    static void RenderBlock(StringBuilder md, ReportBlock block)
    {
        switch (block)
        {
            case ReportHeading heading:
                int level = Math.Clamp(heading.Level, 1, 6);
                md.Append(new string('#', level)).Append(' ')
                  .Append(EscapeText(heading.Text)).Append("\n\n");
                break;

            case ReportParagraph paragraph:
                md.Append(EscapeText(paragraph.Text)).Append("\n\n");
                break;

            case ReportKeyValueTable table:
                RenderTable(md, [table.KeyHeader, table.ValueHeader],
                    table.Rows.Select(r => (IReadOnlyList<string>)new[] { r.Key, r.Value }).ToList());
                break;

            case ReportTable table:
                RenderTable(md, table.Headers, table.Rows);
                break;

            case ReportFormula formula:
                md.Append("> **").Append(EscapeText(formula.Reference)).Append("**\n>\n");
                md.Append("> ").Append(Inline(formula.Formula)).Append("\n>\n");
                md.Append("> ").Append(Inline(formula.Substitution)).Append("\n>\n");
                md.Append("> ").Append(Inline(formula.Result)).Append("\n\n");
                break;

            case ReportImage image:
                if (SvgSizing.LooksLikeSvg(image.Svg))
                {
                    string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(image.Svg));
                    md.Append("![").Append(EscapeText(image.Name))
                      .Append("](data:image/svg+xml;base64,").Append(encoded).Append(")\n\n");
                }
                else
                {
                    string fence = new('`', FenceLength(image.Svg));
                    md.Append(fence).Append('\n').Append(image.Svg).Append('\n').Append(fence).Append("\n\n");
                }
                md.Append('*').Append(EscapeText(image.Name)).Append("*\n\n");
                break;

            case ReportWarning warning:
                md.Append("> ⚠️ ").Append(EscapeText(warning.Text)).Append("\n\n");
                break;

            case ReportPageBreak:
                md.Append("\n---\n\n");
                break;
        }
    }

    static void RenderTable(StringBuilder md, IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        md.Append("| ").Append(string.Join(" | ", headers.Select(EscapeCell))).Append(" |\n");
        md.Append("| ").Append(string.Join(" | ", headers.Select(_ => "---"))).Append(" |\n");
        foreach (var row in rows)
            md.Append("| ").Append(string.Join(" | ", row.Select(EscapeCell))).Append(" |\n");
        md.Append('\n');
    }

    // Ограждение длиннее самой длинной последовательности backtick-ов в содержимом,
    // иначе содержимое закрыло бы блок кода раньше времени.
    static int FenceLength(string content)
    {
        int longest = 0, current = 0;
        foreach (char c in content)
        {
            current = c == '`' ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }
        return Math.Max(3, longest + 1);
    }

    // Формулы: raw-HTML <sub>/<sup> внутри markdown не переинтерпретируется (CommonMark),
    // но текст сегмента всё равно экранируется от & < >.
    static string Inline(string? value)
    {
        var md = new StringBuilder();
        foreach (var segment in FormulaMarkup.Parse(value))
        {
            string text = EscapeSegment(segment.Text);
            switch (segment.Kind)
            {
                case FormulaSegmentKind.Subscript: md.Append("<sub>").Append(text).Append("</sub>"); break;
                case FormulaSegmentKind.Superscript: md.Append("<sup>").Append(text).Append("</sup>"); break;
                default: md.Append(text); break;
            }
        }
        return md.ToString();
    }

    static string EscapeSegment(string text) => text
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>Экранирование заголовков, абзацев, подписей и Reference: построчно,
    /// со склейкой через markdown hard break.</summary>
    static string EscapeText(string? value)
        => string.Join("  \n", SplitLines(value).Select(EscapeLine));

    /// <summary>Экранирование ячейки GFM-таблицы: то же посимвольное экранирование,
    /// плюс <c>|</c>, но переводы строк заменяются на <c>&lt;br&gt;</c>, а не на hard break —
    /// реальный перевод строки разрушил бы таблицу.</summary>
    static string EscapeCell(string? value)
        => string.Join("<br>", SplitLines(value).Select(line => EscapeLine(line).Replace("|", "\\|")));

    static IEnumerable<string> SplitLines(string? value)
        => (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    static string EscapeLine(string line)
    {
        var md = new StringBuilder();

        int lead = 0;
        while (lead < line.Length && line[lead] == ' ') lead++;
        for (int i = 0; i < lead; i++) md.Append("&#32;");
        string rest = line[lead..];

        // Маркер начала строки экранируется отдельно: обратный слеш перед ASCII-пунктуацией.
        // Для нумерованного списка экранируется точка/скобка, а не цифра — цифра не входит
        // в ASCII punctuation и `\1` осталось бы видимым в тексте.
        int consumed = 0;
        if (rest.Length > 0 && rest[0] is '#' or '>' or '-' or '+')
        {
            md.Append('\\').Append(rest[0]);
            consumed = 1;
        }
        else
        {
            int digits = 0;
            while (digits < rest.Length && char.IsAsciiDigit(rest[digits])) digits++;
            if (digits > 0 && digits < rest.Length && rest[digits] is '.' or ')')
            {
                md.Append(rest[..digits]).Append('\\').Append(rest[digits]);
                consumed = digits + 1;
            }
        }

        foreach (char c in rest[consumed..])
        {
            switch (c)
            {
                case '&': md.Append("&amp;"); break;
                case '<': md.Append("&lt;"); break;
                case '>': md.Append("&gt;"); break;
                case '\\': md.Append("\\\\"); break;
                case '`': case '*': case '_': case '[': case ']':
                    md.Append('\\').Append(c); break;
                default: md.Append(c); break;
            }
        }

        return md.ToString();
    }
}
