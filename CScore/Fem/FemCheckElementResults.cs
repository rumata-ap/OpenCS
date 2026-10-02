using System.Text.Json;

namespace CScore.Fem;

/// <summary>Итог проверки одного КЭ с одним источником армирования (строка раздела <c>elements</c> результата).</summary>
/// <param name="ElemTag">Тег КЭ сетки.</param>
/// <param name="RebarSource">Ключ источника армирования (<see cref="FemCheckRebarSource"/>); у стержней — пусто.</param>
/// <param name="Status"><c>ok</c> / <c>failed</c> / <c>not_checked</c> / <c>no_forces</c> / <c>no_rebar</c> / <c>no_section</c>.</param>
/// <param name="UtilMax">Наибольший коэффициент использования; null — конечного значения нет.</param>
public sealed record FemCheckElementResult(string ElemTag, string RebarSource, string Status, double? UtilMax)
{
    /// <summary>КЭ проверен (есть усилия и сечение), хотя бы одна строка посчитана.</summary>
    public bool IsChecked => Status is "ok" or "failed";
}

/// <summary>Строка усилий результата проверки по КЭ (раздел <c>rows</c>).</summary>
/// <param name="ElemNum">Номер КЭ.</param>
/// <param name="SectionNum">Номер сечения КЭ; null — у строки усилий его нет.</param>
/// <param name="RebarSource">Ключ источника армирования; пусто — сечение цели без выбора источников.</param>
/// <param name="Utilization">Коэффициент использования; null — конечного значения нет.</param>
/// <param name="Passed">Строка прошла проверку.</param>
/// <param name="NotChecked">Строка не проверена (нет сечения, ошибка расчёта).</param>
public sealed record FemCheckRowResult(int ElemNum, int? SectionNum, string RebarSource, double? Utilization, bool Passed, bool NotChecked);

/// <summary>
/// Строка усилий результата проверки по КЭ со всеми полями. Строки хранятся не в <c>DataJson</c>
/// (на РСУ SCAD их миллионы), а отдельной таблицей БД; в <c>DataJson</c> такого результата —
/// признак <c>rowsStored</c>.
/// </summary>
public sealed record FemCheckRow
{
    public string Label            { get; init; } = "";
    public string ForceSetTag      { get; init; } = "";
    public string CalcType         { get; init; } = "";
    public double Utilization      { get; init; }
    public bool   Passed           { get; init; }
    public string WorstFormula     { get; init; } = "";
    public string WorstDescription { get; init; } = "";
    /// <summary>Строка не проверена (ошибка расчёта, нет сечения), а не «не прошла».</summary>
    public bool   NotChecked       { get; init; }
    /// <summary>Номер КЭ во внешней схеме (проверка по КЭ).</summary>
    public int?   ElemNum          { get; init; }
    /// <summary>Номер сечения КЭ во внешней схеме.</summary>
    public int?   SectionNum       { get; init; }
    /// <summary>Подпись сечения, с которым проверена строка.</summary>
    public string SectionLabel     { get; init; } = "";
    /// <summary>Ключ армирования сечения пластины.</summary>
    public string RebarKey         { get; init; } = "";
    /// <summary>Источник армирования пластины (<see cref="FemCheckRebarSource"/>).</summary>
    public string RebarSource      { get; init; } = "";

    /// <summary>Сокращённая строка для эпюр и мозаик; null — строка без номера КЭ.</summary>
    public FemCheckRowResult? ToRowResult() => ElemNum is int n
        ? new FemCheckRowResult(n, SectionNum, RebarSource, double.IsFinite(Utilization) ? Utilization : null, Passed, NotChecked)
        : null;
}

/// <summary>Чтение агрегата по КЭ из <c>DataJson</c> результата проверки по КЭ.</summary>
public static class FemCheckElementResults
{
    /// <summary>Строки раздела <c>elements</c>; пусто — результат не поэлементный или не читается.</summary>
    public static IReadOnlyList<FemCheckElementResult> Parse(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson)) return [];
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("elements", out var elements)
                || elements.ValueKind != JsonValueKind.Array)
                return [];

            var result = new List<FemCheckElementResult>(elements.GetArrayLength());
            foreach (var e in elements.EnumerateArray())
            {
                string tag = Str(e, "elemTag");
                if (tag == "") continue;
                double? util = e.TryGetProperty("utilMax", out var u) && u.ValueKind == JsonValueKind.Number
                    ? u.GetDouble() : null;
                result.Add(new FemCheckElementResult(tag.Trim(), Str(e, "rebarSource"), Str(e, "status"), util));
            }
            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Строки результата хранятся отдельно от <c>DataJson</c> (признак <c>rowsStored</c>).</summary>
    public static bool RowsStored(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("rowsStored", out var v) && v.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Строки раздела <c>rows</c> с номером КЭ (результаты, где строки лежат в самом <c>DataJson</c>);
    /// пусто — результат не поэлементный или не читается.</summary>
    public static IReadOnlyList<FemCheckRowResult> ParseRows(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson)) return [];
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("rows", out var rows)
                || rows.ValueKind != JsonValueKind.Array)
                return [];

            var result = new List<FemCheckRowResult>(rows.GetArrayLength());
            foreach (var r in rows.EnumerateArray())
            {
                if (Int(r, "elemNum") is not int elem) continue;
                double? util = r.TryGetProperty("utilization", out var u) && u.ValueKind == JsonValueKind.Number
                    ? u.GetDouble() : null;
                result.Add(new FemCheckRowResult(elem, Int(r, "sectionNum"), Str(r, "rebarSource"), util,
                    Bool(r, "passed"), Bool(r, "notChecked")));
            }
            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : null;

    static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
