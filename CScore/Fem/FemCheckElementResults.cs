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

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
