using CScore;

namespace OpenCS.Reporting;

/// <summary>
/// Русские названия перечислений и кодов для текста отчёта: имена членов перечислений
/// и служебные коды (ReSteelF, RebarGroup, partial) в отчёт не выводятся.
/// </summary>
public static class ReportNames
{
    /// <summary>Вид расчёта: группа предельных состояний и длительность действия нагрузки.</summary>
    public static string Calc(CalcType calc) => calc switch
    {
        CalcType.C => "ПС1, непродолжительное действие",
        CalcType.CL => "ПС1, продолжительное действие",
        CalcType.N => "ПС2, непродолжительное действие",
        CalcType.NL => "ПС2, продолжительное действие",
        _ => "не задан",
    };

    /// <summary>Тип материала.</summary>
    public static string Material(MatType type) => type switch
    {
        MatType.Concrete => "бетон",
        MatType.ReSteelF => "арматура с физическим пределом текучести",
        MatType.ReSteelU => "арматура с условным пределом текучести",
        MatType.Steel => "конструкционная сталь",
        MatType.Custom => "произвольные диаграммы",
        _ => "не задан",
    };

    /// <summary>Категория части сечения.</summary>
    public static string Category(AreaCategory category) => category switch
    {
        AreaCategory.Region => "область",
        AreaCategory.RebarGroup => "группа стержней",
        AreaCategory.Stirrups => "хомуты",
        _ => "—",
    };

    /// <summary>Вид диаграммы материала.</summary>
    public static string Diagram(DiagrammType type) => type switch
    {
        DiagrammType.L2 => "двухлинейная (L2)",
        DiagrammType.L3 => "трёхлинейная (L3)",
        DiagrammType.EKB => "криволинейная (ЕКБ / EN 1992 §3.1.5)",
        DiagrammType.SP63 => "криволинейная (СП 63)",
        DiagrammType.SP35 => "параболическо-прямоугольная (СП 35 / EN 1992 §3.1.7)",
        DiagrammType.SP16 => "многосегментная (СП 16)",
        DiagrammType.Custom => "пользовательская",
        _ => "—",
    };

    /// <summary>Статус результата расчёта.</summary>
    public static string Status(string? status) => status switch
    {
        "ok" => "выполнен",
        "partial" => "выполнен частично (есть строки, которые не сошлись или с ошибкой)",
        "not_converged" => "решение не найдено",
        "not_passed" => "выполнен, проверка не пройдена",
        "incomplete" => "выполнен не полностью",
        "not_applicable" => "не применимо",
        "error" => "ошибка",
        "cancelled" => "отменён",
        "running" => "выполняется",
        null or "" => "—",
        _ => status,
    };
}
