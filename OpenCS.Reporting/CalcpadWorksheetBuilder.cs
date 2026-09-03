using System.Text;

namespace OpenCS.Reporting;

/// <summary>Преобразует нейтральный отчёт в worksheet CalcpadCE.</summary>
public sealed class CalcpadWorksheetBuilder
{
    /// <summary>
    /// Создаёт CPD, содержащий уже рассчитанные OpenCS значения как HTML-вывод.
    /// CalcpadCE отвечает только за оформление и конвертацию, повторный расчёт не выполняется.
    /// </summary>
    public string Build(ReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        string html = new HtmlReportRenderer().Render(document);
        int mainStart = html.IndexOf("<main", StringComparison.OrdinalIgnoreCase);
        int bodyStart = mainStart >= 0 ? html.IndexOf('>', mainStart) + 1 : 0;
        int bodyEnd = html.LastIndexOf("</main>", StringComparison.OrdinalIgnoreCase);
        if (bodyStart <= 0 || bodyEnd < bodyStart)
            throw new InvalidOperationException("HTML отчёта не содержит ожидаемого контейнера main.");

        string body = html[bodyStart..bodyEnd];
        var worksheet = new StringBuilder();
        worksheet.AppendLine("'<!-- OpenCS report: values were calculated by OpenCS; CalcpadCE performs presentation export only. -->");
        worksheet.AppendLine("'<style>body{font-family:Segoe UI,Arial,sans-serif} table{width:100%;border-collapse:collapse} th,td{border:1px solid #d9e1ea;padding:5px 7px;text-align:left} th{background:#eaf2f9} .formula{margin:10px 0;padding:8px 12px;border-left:3px solid #1769aa;background:#f5f8fb}</style>");
        foreach (string line in body.Split('\n'))
        {
            string safeLine = line.TrimEnd('\r').Replace("'", "&apos;", StringComparison.Ordinal);
            worksheet.Append('\'').AppendLine(safeLine);
        }

        return worksheet.ToString();
    }
}
