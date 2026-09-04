using System.Net;
using System.Text;

namespace OpenCS.Reporting;

/// <summary>Автономный HTML-рендерер отчёта для просмотра в браузере и fallback-экспорта.</summary>
public sealed class HtmlReportRenderer
{
    /// <summary>Преобразует нейтральный документ в самодостаточный HTML5.</summary>
    public string Render(ReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var html = new StringBuilder();
        html.AppendLine("<!doctype html>");
        html.AppendLine("<html lang=\"ru\"><head><meta charset=\"utf-8\">");
        html.Append("<title>").Append(E(document.Title)).AppendLine("</title>");
        html.AppendLine("<style>");
        html.AppendLine(Css);
        html.AppendLine("</style></head><body><main class=\"report\">");
        html.Append("<header class=\"report-header\"><h1>").Append(E(document.Title));
        html.AppendLine("</h1></header>");

        foreach (var block in document.Blocks)
            RenderBlock(html, block);

        html.AppendLine("</main></body></html>");
        return html.ToString();
    }

    static void RenderBlock(StringBuilder html, ReportBlock block)
    {
        switch (block)
        {
            case ReportHeading heading:
                int level = Math.Clamp(heading.Level, 1, 6);
                html.Append("<h").Append(level).Append('>')
                    .Append(E(heading.Text)).Append("</h").Append(level).AppendLine(">");
                break;
            case ReportParagraph paragraph:
                html.Append("<p>").Append(E(paragraph.Text)).AppendLine("</p>");
                break;
            case ReportKeyValueTable table:
                html.AppendLine("<table class=\"key-values\">");
                html.Append("<thead><tr><th>").Append(E(table.KeyHeader))
                    .Append("</th><th>").Append(E(table.ValueHeader)).AppendLine("</th></tr></thead>");
                html.AppendLine("<tbody>");
                foreach (var (key, value) in table.Rows)
                    html.Append("<tr><th>").Append(E(key)).Append("</th><td>")
                        .Append(E(value)).AppendLine("</td></tr>");
                html.AppendLine("</tbody></table>");
                break;
            case ReportTable table:
                RenderTable(html, table);
                break;
            case ReportFormula formula:
                // Formula/Substitution/Result проходят через FormulaMarkup: разрешены ровно
                // <sub>/<sup>, остальной текст экранируется посегментно (Inline).
                // Каждая строка формулы — отдельный <p>: блок печатается в PDF как единый
                // абзац с инлайн-индексами, а не рассыпается на строки-контейнеры.
                html.AppendLine("<section class=\"formula\">");
                html.Append("<p class=\"formula-ref\">").Append(E(formula.Reference)).AppendLine("</p>");
                html.Append("<p class=\"formula-expression\">").Append(Inline(formula.Formula)).AppendLine("</p>");
                html.Append("<p class=\"formula-substitution\">").Append(Inline(formula.Substitution)).AppendLine("</p>");
                html.Append("<p class=\"formula-result\">").Append(Inline(formula.Result)).AppendLine("</p>");
                html.AppendLine("</section>");
                break;
            case ReportImage image:
                html.AppendLine("<figure class=\"report-image\">");
                if (SvgSizing.LooksLikeSvg(image.Svg))
                {
                    var size = SvgSizing.ScaleToMaxWidth(SvgSizing.Resolve(image.Svg));
                    string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(image.Svg));
                    string alt = string.IsNullOrWhiteSpace(image.Name) ? "report.svg" : image.Name + ".svg";
                    html.Append("<img src=\"data:image/svg+xml;base64,").Append(encoded)
                        .Append("\" alt=\"").Append(E(alt)).Append("\" width=\"")
                        .Append(Number(Math.Max(1, Math.Round(size.Width)))).Append("\" height=\"")
                        .Append(Number(Math.Max(1, Math.Round(size.Height)))).AppendLine("\"/>");
                }
                else
                    html.Append("<pre>").Append(E(image.Svg)).AppendLine("</pre>");
                html.Append("<figcaption>").Append(E(image.Name)).AppendLine("</figcaption></figure>");
                break;
            case ReportWarning warning:
                html.Append("<aside class=\"warning\">").Append(E(warning.Text)).AppendLine("</aside>");
                break;
            case ReportPageBreak:
                html.AppendLine("<div class=\"page-break\"></div>");
                break;
        }
    }

    // Пересобирает inline-разметку формулы из проверенных сегментов: HTML больше не
    // доверяет строке провайдера целиком, каждый Plain-сегмент экранируется отдельно.
    static string Inline(string? value)
    {
        var html = new StringBuilder();
        foreach (var segment in FormulaMarkup.Parse(value))
        {
            switch (segment.Kind)
            {
                case FormulaSegmentKind.Subscript:
                    html.Append("<sub>").Append(E(segment.Text)).Append("</sub>"); break;
                case FormulaSegmentKind.Superscript:
                    html.Append("<sup>").Append(E(segment.Text)).Append("</sup>"); break;
                default:
                    html.Append(E(segment.Text)); break;
            }
        }
        return html.ToString();
    }

    static string Number(double value)
        => value.ToString("G8", System.Globalization.CultureInfo.InvariantCulture);

    static void RenderTable(StringBuilder html, ReportTable table)
    {
        string cssClass = table.Headers.Count >= 6 ? "report-table compact" : "report-table";
        html.Append("<table class=\"").Append(cssClass).AppendLine("\"><thead><tr>");
        foreach (var header in table.Headers)
            html.Append("<th>").Append(E(header)).AppendLine("</th>");
        html.AppendLine("</tr></thead><tbody>");
        foreach (var row in table.Rows)
        {
            html.AppendLine("<tr>");
            foreach (var cell in row)
                html.Append("<td>").Append(E(cell)).AppendLine("</td>");
            html.AppendLine("</tr>");
        }
        html.AppendLine("</tbody></table>");
    }

    static string E(string? value) => WebUtility.HtmlEncode(value ?? "");

    const string Css = """
        :root { color-scheme: light; --ink:#1f2937; --muted:#64748b; --line:#d9e1ea; --accent:#1769aa; --soft:#f5f8fb; }
        * { box-sizing:border-box; }
        body { margin:0; background:#e9eef4; color:var(--ink); font:14px/1.5 Segoe UI, Arial, sans-serif; }
        .report { width:210mm; min-height:297mm; margin:18px auto; padding:18mm 16mm; background:white; box-shadow:0 5px 24px #33415535; }
        .report-header { border-bottom:2px solid var(--accent); margin-bottom:24px; padding-bottom:12px; }
        h1,h2,h3,h4 { color:#16324f; line-height:1.2; margin:1.4em 0 .55em; }
        h1 { margin-top:0; font-size:25px; } h2 { font-size:20px; } h3 { font-size:16px; }
        .report-meta { color:var(--muted); font-size:12px; }
        p { margin:8px 0 12px; } table { width:100%; max-width:100%; table-layout:fixed; border-collapse:collapse; margin:12px 0 20px; page-break-inside:avoid; }
        th,td { border:1px solid var(--line); padding:7px 9px; text-align:left; vertical-align:top; overflow-wrap:anywhere; word-break:break-word; white-space:normal; }
        .report-table.compact { font-size:11px; } .key-values { table-layout:auto; }
        thead th { background:#eaf2f9; color:#16324f; } .key-values th { width:34%; background:var(--soft); }
        .formula { margin:13px 0; padding:11px 14px; border-left:4px solid var(--accent); background:var(--soft); page-break-inside:avoid; }
        .formula-ref { float:right; margin:0; color:var(--muted); font-weight:600; } .formula-expression { font:16px Georgia, serif; margin:0 0 7px; }
        .formula-expression sub, .formula-substitution sub, .formula-result sub,
        .formula-expression sup, .formula-substitution sup, .formula-result sup { font-size:.72em; }
        .formula-substitution { margin:0; color:#475569; } .formula-result { margin:4px 0 0; color:#075985; font-weight:700; }
        .report-image { max-width:100%; margin:18px 0; text-align:center; page-break-inside:avoid; overflow:hidden; } .report-image img { display:block; max-width:100%; height:auto; max-height:115mm; margin:0 auto; }
        figcaption { color:var(--muted); font-size:12px; margin-top:4px; } .warning { padding:10px 12px; border:1px solid #f2c36b; background:#fff7df; color:#7c4a03; margin:12px 0; }
        .page-break { break-before:page; page-break-before:always; }
        @media print { body { background:white; } .report { margin:0; box-shadow:none; } @page { size:A4; margin:0; } }
        """;
}
