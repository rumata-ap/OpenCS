using System.Globalization;
using System.Text.RegularExpressions;

namespace CScore.Import;

/// <summary>
/// Разбор строки жёсткости SCAD — единый для txt-экспорта (запись блока "(3/...)" без номера)
/// и SCADAPIX.dll (ApiGetRigid). Вид — по ключевому слову: GE/GEI — оболочка («E ν h»),
/// S0 — брус («E B H», B ‖ Y1, H ‖ Z1), STZ — сортамент (форма не поддерживается),
/// прочее (SPRING …) — Other.
/// </summary>
public static class ScadStiffnessParams
{
    /// <summary>
    /// KindCode записи SCAD в fem_schema_stiffnesses (у ЛИРЫ коды ≥ 0): Params — исходная
    /// строка SCAD, SectionUnitM — единица размеров сечений проекта.
    /// </summary>
    public const int ScadKindCode = -1;

    static readonly Regex Keyword =
        new(@"^(GEI|GE|STZ|S0|SPRING)\b", RegexOptions.Compiled);

    static readonly char[] Separators = [' ', '\t', '\r', '\n'];

    /// <summary>
    /// Разбирает строку жёсткости.
    /// </summary>
    /// <param name="id">Номер жёсткости.</param>
    /// <param name="body">Строка SCAD без номера в начале (пустая — неиспользуемая жёсткость).</param>
    /// <param name="name">Имя жёсткости (null/пусто — нет).</param>
    /// <param name="lengthUnitM">Единица длины проекта в метрах (толщина оболочки).</param>
    /// <param name="sectionUnitM">Единица размеров сечений стержней в метрах (брус).</param>
    public static ScadStiffnessRecord Parse(int id, string body, string? name,
        double lengthUnitM, double sectionUnitM)
    {
        string text = body.Trim();
        string? nm = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var m = Keyword.Match(text);
        string keyword = m.Success ? m.Groups[1].Value : "";

        var kind = keyword switch
        {
            "GE" or "GEI" => ScadStiffnessKind.Shell,
            "S0" or "STZ" => ScadStiffnessKind.Bar,
            _             => ScadStiffnessKind.Other,
        };

        var parts = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        double? thickness = null;
        LiraBarRect? rect = null;
        if (kind == ScadStiffnessKind.Shell && Number(parts, 3) is double h && h > 0 && lengthUnitM > 0)
            thickness = Math.Round(h * lengthUnitM, 6);
        if (keyword == "S0" && sectionUnitM > 0 &&
            Number(parts, 2) is double b && Number(parts, 3) is double hh && b > 0 && hh > 0)
            rect = new LiraBarRect(Math.Round(b * sectionUnitM, 6), Math.Round(hh * sectionUnitM, 6));

        return new ScadStiffnessRecord(id, nm, kind, thickness, text, rect);
    }

    /// <summary>
    /// Брус жёсткости SCAD, сохранённой в fem_schema_stiffnesses
    /// (<see cref="ScadKindCode"/>); null — запись не SCAD или сечение не «брус».
    /// </summary>
    public static LiraBarRect? BarRect(LiraStiffnessRecord s) =>
        s.KindCode == ScadKindCode
            ? Parse(s.Id, s.Params, s.Name, lengthUnitM: 1, sectionUnitM: s.SectionUnitM).BarRect
            : null;

    /// <summary>Число на позиции <paramref name="index"/> (0 — ключевое слово); null — нет или не число.</summary>
    static double? Number(string[] parts, int index) =>
        index < parts.Length &&
        double.TryParse(parts[index].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v : null;
}
