using System.Globalization;
using System.Text;
using CScore.Import;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Сводка переноса ГУ текстом: таблица «вид — ед. — перенесено — не перенесено — примечание» (столбцы через табуляцию,
/// вставляется в Excel) и сообщения. Общая для кнопки «Копировать» окна сводки и вывода консоли.
/// </summary>
public static class FemBoundarySummaryText
{
    public static string Format(string? header, IEnumerable<FemBoundaryTransferRow> rows, IEnumerable<string> messages)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(header)) sb.AppendLine(header);
        sb.AppendLine(string.Join('\t', Loc.S("FemBoundarySummaryKind"), Loc.S("FemBoundarySummaryUnit"),
            Loc.S("FemBoundarySummaryTransferred"), Loc.S("FemBoundarySummaryNotTransferred"), Loc.S("FemBoundarySummaryNote")));
        foreach (var r in rows)
            sb.AppendLine(string.Join('\t', r.Kind, r.Unit, r.Transferred.ToString(CultureInfo.InvariantCulture),
                r.NotTransferred.ToString(CultureInfo.InvariantCulture), r.Note));
        bool first = true;
        foreach (var m in messages)
        {
            if (first) { sb.AppendLine(); first = false; }
            sb.AppendLine(m);
        }
        return sb.ToString();
    }
}
