using System.Globalization;
using CScore.ParametricRc;

namespace CScore.Import;

/// <summary>Ключ ЖБ-сечения стержня: одно сечение проекта на форму, размеры и материалы.</summary>
/// <param name="Shape">Форма.</param>
/// <param name="WidthMm">Ширина B, мм (округлена до 0,1 мм).</param>
/// <param name="HeightMm">Высота H, мм (округлена до 0,1 мм).</param>
/// <param name="ConcreteId">Id бетона проекта.</param>
/// <param name="RebarId">Id продольной арматуры проекта.</param>
public readonly record struct RcSectionKey(ImportedBarShape Shape, double WidthMm, double HeightMm, int ConcreteId, int RebarId);

/// <summary>
/// Параметрическое ЖБ-сечение проекта по профилю жёсткости стержня: форма и размеры — из профиля, материалы —
/// материалы проекта по классам схемы. Арматуры нет: в проверке по КЭ она берётся из источников
/// «Подобранное/Заданное»; материал продольной арматуры записывается в определение, чтобы её можно было
/// задать в параметрическом редакторе. Оси — как у профиля: x ‖ Y1 (ширина B), y ‖ Z1 (высота H).
/// </summary>
public static class RcSectionBuilder
{
    /// <summary>Ключ группировки КЭ.</summary>
    public static RcSectionKey Key(ImportedBarProfile profile, int concreteId, int rebarId) =>
        new(profile.Shape, Mm(profile.WidthM), Mm(profile.HeightM), concreteId, rebarId);

    /// <summary>Определение сечения либо причина, по которой его нет.</summary>
    /// <param name="profile">Профиль жёсткости.</param>
    /// <param name="concreteId">Id бетона проекта.</param>
    /// <param name="rebarId">Id продольной арматуры проекта.</param>
    /// <param name="materialsLabel">Подпись материалов в имени сечения («B25 A500»).</param>
    public static (ParametricRcSectionDefinition? Definition, string? Reason) Build(
        ImportedBarProfile profile, int concreteId, int rebarId, string materialsLabel)
    {
        if (profile.Material != ImportedBarMaterial.Concrete)
            return (null, $"жёсткость {profile.StiffnessNum} «{profile.SourceLabel}»: стержень не железобетонный");
        if (profile.Shape != ImportedBarShape.Rectangle)
            return (null, $"жёсткость {profile.StiffnessNum} «{profile.SourceLabel}»: форма сечения не поддерживается");
        if (!(profile.WidthM > 0) || !(profile.HeightM > 0))
            return (null, $"жёсткость {profile.StiffnessNum} «{profile.SourceLabel}»: размеры сечения не заданы");

        var key = Key(profile, concreteId, rebarId);
        var definition = ParametricRcSectionDefinition.Rectangle(key.WidthMm / 1000, key.HeightMm / 1000) with
        {
            Tag = string.Format(CultureInfo.InvariantCulture, "Брус {0:0.#}×{1:0.#} {2}",
                key.WidthMm, key.HeightMm, materialsLabel).TrimEnd(),
            ConcreteMaterialId = concreteId,
            LongitudinalMaterialId = rebarId,
        };
        return (definition, null);
    }

    /// <summary>
    /// Определение задаёт то же сечение, что создал бы <see cref="Build"/> для <paramref name="key"/>:
    /// та же форма, размеры и материалы, без арматуры. Имя не сравнивается.
    /// </summary>
    public static bool Matches(ParametricRcSectionDefinition definition, RcSectionKey key) =>
        key.Shape == ImportedBarShape.Rectangle
        && definition.Shape == ParametricRcShape.Rectangle
        && Mm(definition.WidthM) == key.WidthMm && Mm(definition.HeightM) == key.HeightMm
        && definition.ConcreteMaterialId == key.ConcreteId && definition.LongitudinalMaterialId == key.RebarId
        && definition.UpperRebar is not { Enabled: true } && definition.LowerRebar is not { Enabled: true }
        && definition.PolarRebar == null && definition.StirrupCuts.Count == 0;

    static double Mm(double m) => Math.Round(m * 1000, 1);
}
