using System.Globalization;
using CScore.ParametricRc;

namespace CScore.Import;

/// <summary>Ключ ЖБ-сечения стержня: одно сечение проекта на форму, размеры и материалы.</summary>
/// <param name="Shape">Форма.</param>
/// <param name="WidthMm">Ширина B, мм (округлена до 0,1 мм).</param>
/// <param name="HeightMm">Высота H, мм (округлена до 0,1 мм).</param>
/// <param name="ConcreteId">Id бетона проекта.</param>
/// <param name="RebarId">Id продольной арматуры проекта.</param>
/// <param name="Bars">Стержни армирования по данным схемы — канонический текст (<see cref="RcSectionBuilder.BarsText"/>);
/// пусто — без арматуры.</param>
public readonly record struct RcSectionKey(ImportedBarShape Shape, double WidthMm, double HeightMm, int ConcreteId, int RebarId,
    string Bars = "");

/// <summary>
/// Параметрическое ЖБ-сечение проекта по профилю жёсткости стержня: форма и размеры — из профиля, материалы —
/// материалы проекта по классам схемы. Арматура — по данным схемы (<see cref="ImportedBarRebar"/>) отдельными
/// стержнями (<see cref="ParametricRcSectionDefinition.ExtraBars"/>) либо нет её (в проверке по КЭ она тогда
/// берётся из источников «Подобранное/Заданное»); материал продольной арматуры записывается в определение всегда. Оси — как у профиля: x ‖ Y1 (ширина B), y ‖ Z1 (высота H).
/// </summary>
public static class RcSectionBuilder
{
    /// <summary>Ключ группировки КЭ.</summary>
    /// <param name="bars">Стержни армирования; null — без арматуры.</param>
    public static RcSectionKey Key(ImportedBarProfile profile, int concreteId, int rebarId, IEnumerable<LiraBarPoint>? bars = null) =>
        new(profile.Shape, Mm(profile.WidthM), Mm(profile.HeightM), concreteId, rebarId,
            BarsText(bars?.Select(b => new ParametricRebarPoint(b.X, b.Y, b.AreaM2, b.DiameterM)) ?? []));

    /// <summary>
    /// Канонический текст стержней для сравнения сечений: координаты и диаметр — мм (0,01), площадь — мм² (0,001),
    /// в порядке x, y.
    /// </summary>
    public static string BarsText(IEnumerable<ParametricRebarPoint> bars) => string.Join(";", bars
        .Select(b => string.Format(CultureInfo.InvariantCulture, "{0:0.##},{1:0.##},{2:0.###},{3:0.##}",
            b.X * 1000 + 0.0, b.Y * 1000 + 0.0, b.AreaM2 * 1e6, b.DiameterM * 1000))
        .Order(StringComparer.Ordinal));

    /// <summary>Определение сечения либо причина, по которой его нет.</summary>
    /// <param name="profile">Профиль жёсткости.</param>
    /// <param name="concreteId">Id бетона проекта.</param>
    /// <param name="rebarId">Id продольной арматуры проекта.</param>
    /// <param name="materialsLabel">Подпись материалов в имени сечения («B25 A500»).</param>
    /// <param name="rebar">Армирование по данным схемы; null — без арматуры.</param>
    public static (ParametricRcSectionDefinition? Definition, string? Reason) Build(
        ImportedBarProfile profile, int concreteId, int rebarId, string materialsLabel, ImportedBarRebarLayout? rebar = null)
    {
        if (profile.Material != ImportedBarMaterial.Concrete)
            return (null, $"жёсткость {profile.StiffnessNum} «{profile.SourceLabel}»: стержень не железобетонный");
        if (profile.Shape != ImportedBarShape.Rectangle)
            return (null, $"жёсткость {profile.StiffnessNum} «{profile.SourceLabel}»: форма сечения не поддерживается");
        if (!(profile.WidthM > 0) || !(profile.HeightM > 0))
            return (null, $"жёсткость {profile.StiffnessNum} «{profile.SourceLabel}»: размеры сечения не заданы");

        var key = Key(profile, concreteId, rebarId);
        string tag = string.Format(CultureInfo.InvariantCulture, "Брус {0:0.#}×{1:0.#} {2}",
            key.WidthMm, key.HeightMm, materialsLabel).TrimEnd();
        if (rebar != null)
            tag += string.Format(Ru, " {0} ΣAs {1:0.##} см²", rebar.Label, rebar.TotalAreaCm2);
        var definition = ParametricRcSectionDefinition.Rectangle(key.WidthMm / 1000, key.HeightMm / 1000) with
        {
            Tag = tag,
            ConcreteMaterialId = concreteId,
            LongitudinalMaterialId = rebarId,
            ExtraBars = rebar?.Bars.Select(b => new ParametricRebarPoint(b.X, b.Y, b.AreaM2, b.DiameterM)).ToList() ?? [],
        };
        return (definition, null);
    }

    /// <summary>
    /// Определение задаёт то же сечение, что создал бы <see cref="Build"/> для <paramref name="key"/>:
    /// та же форма, размеры, материалы и стержни по данным схемы, без рядов, полярной арматуры и хомутов.
    /// Имя не сравнивается.
    /// </summary>
    public static bool Matches(ParametricRcSectionDefinition definition, RcSectionKey key) =>
        key.Shape == ImportedBarShape.Rectangle
        && definition.Shape == ParametricRcShape.Rectangle
        && Mm(definition.WidthM) == key.WidthMm && Mm(definition.HeightM) == key.HeightMm
        && definition.ConcreteMaterialId == key.ConcreteId && definition.LongitudinalMaterialId == key.RebarId
        && definition.UpperRebar is not { Enabled: true } && definition.LowerRebar is not { Enabled: true }
        && definition.PolarRebar == null && definition.StirrupCuts.Count == 0
        && BarsText(definition.ExtraBars) == (key.Bars ?? "");

    static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    static double Mm(double m) => Math.Round(m * 1000, 1);
}
