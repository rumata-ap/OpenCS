using System.Text;

namespace OpenCS.Reporting;

/// <summary>Преобразует нейтральный отчёт в worksheet CalcpadCE.</summary>
public sealed class CalcpadWorksheetBuilder
{
    /// <summary>
    /// Создаёт CPD, содержащий уже рассчитанные OpenCS значения как HTML-вывод.
    /// CalcpadCE отвечает только за оформление и конвертацию, повторный расчёт не выполняется.
    /// </summary>
    public string Build(ReportDocument document, bool forDocx = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        string html = new HtmlReportRenderer().Render(document);
        int mainStart = html.IndexOf("<main", StringComparison.OrdinalIgnoreCase);
        int bodyStart = mainStart >= 0 ? html.IndexOf('>', mainStart) + 1 : 0;
        int bodyEnd = html.LastIndexOf("</main>", StringComparison.OrdinalIgnoreCase);
        if (bodyStart <= 0 || bodyEnd < bodyStart)
            throw new InvalidOperationException("HTML отчёта не содержит ожидаемого контейнера main.");

        string body = NormalizeForCalcpad(html[bodyStart..bodyEnd], forDocx);
        var worksheet = new StringBuilder();
        worksheet.AppendLine("'<!-- OpenCS report: values were calculated by OpenCS; CalcpadCE performs presentation export only. -->");
        worksheet.AppendLine("'<style>body{font-family:Segoe UI,Arial,sans-serif} table{width:100%;max-width:100%;table-layout:fixed;border-collapse:collapse} th,td{border:1px solid #d9e1ea;padding:5px 7px;text-align:left;overflow-wrap:anywhere;word-break:break-word;white-space:normal;font-size:10pt} th{background:#eaf2f9} .report-table.compact{font-size:9pt} img{max-width:100%;height:auto;display:block;margin:auto} .formula{margin:10px 0;padding:8px 12px;border-left:3px solid #1769aa;background:#f5f8fb}</style>");
        foreach (string line in body.Split('\n'))
        {
            string safeLine = line.TrimEnd('\r').Replace("'", "&apos;", StringComparison.Ordinal);
            worksheet.Append('\'').Append(safeLine).AppendLine("'");
        }

        return worksheet.ToString();
    }

    private static string NormalizeForCalcpad(string body, bool forDocx)
    {
        // CalcpadCE's DOCX writer supports div, p, table, headings and img,
        // but silently drops HTML5 semantic containers such as section/figure.
        // Keep the browser HTML semantic and flatten only the worksheet copy.
        string normalized = body
            .Replace("<header class=\"report-header\">", "<div class=\"report-header\">", StringComparison.Ordinal)
            .Replace("</header>", "</div>", StringComparison.Ordinal)
            .Replace("<section class=\"formula\">", "<div class=\"formula\">", StringComparison.Ordinal)
            .Replace("</section>", "</div>", StringComparison.Ordinal)
            .Replace("<figure class=\"report-image\">", "<div class=\"report-image\">", StringComparison.Ordinal)
            .Replace("</figure>", "</div>", StringComparison.Ordinal)
            .Replace("<figcaption>", "<p>", StringComparison.Ordinal)
            .Replace("</figcaption>", "</p>", StringComparison.Ordinal)
            .Replace("<aside class=\"warning\">", "<div class=\"warning\">", StringComparison.Ordinal)
            .Replace("</aside>", "</div>", StringComparison.Ordinal);

        // CalcpadCE 10.x DOCX reader reads the subtype literally and expects
        // "svg", while browsers and wkhtmltopdf use the standard "svg+xml".
        return forDocx
            ? normalized.Replace("data:image/svg+xml;base64,", "data:image/svg;base64,", StringComparison.Ordinal)
            : normalized;
    }
}
