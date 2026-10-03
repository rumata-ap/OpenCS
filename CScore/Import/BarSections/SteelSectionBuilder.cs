using CScore.ParametricSteel;
using CScore.Sp16;

namespace CScore.Import;

/// <summary>Поиск строки собственного сортамента OpenCS, совпадающей с импортированным профилем.</summary>
public interface ISteelCatalogLookup
{
    /// <summary>Ссылка на строку сортамента (с It) при совпадении стандарта и размеров до 0,1 мм; null — нет.</summary>
    ParametricSteelCatalogRef? Find(ImportedSteelShape shape);
}

/// <summary>Ключ стального сечения стержня: одно сечение проекта на вид, размеры (до 0,1 мм) и материал.</summary>
public readonly record struct SteelSectionKey(SteelProfileKind Kind, SteelFabrication Fabrication,
    double HMm, double BMm, double TwMm, double TfMm, double R1Mm, double R2Mm, double Slope,
    bool Flipped, int MaterialId);

/// <summary>Результат построения стального сечения.</summary>
/// <param name="Definition">Определение; null — см. <paramref name="Reason"/>.</param>
/// <param name="Reason">Причина, по которой сечения нет.</param>
/// <param name="Warning">Предупреждение сверки (площадь контура против сортамента источника).</param>
public sealed record SteelSectionBuildResult(ParametricSteelSectionDefinition? Definition, string? Reason, string? Warning);

/// <summary>
/// Параметрическое МК-сечение по стальному профилю жёсткости стержня. Размеры — из сортамента источника (как в
/// расчётной программе); совпадение со строкой <c>Sortamenty.db3</c> даёт ссылку на каталог (It для СП 16).
/// Положение — каноническое (стенка ‖ Z1 = ось y сечения OpenCS), зеркальный уголок — <c>Flipped</c>.
/// </summary>
public static class SteelSectionBuilder
{
    /// <summary>Допуск сверки площади контура с площадью сортамента источника.</summary>
    public const double AreaTolerance = 0.01;

    /// <summary>Ключ группировки КЭ; null — профиль не стальной.</summary>
    public static SteelSectionKey? Key(ImportedBarProfile profile, int materialId) =>
        profile.Steel is { } s ? Key(s.Kind, s.Fabrication, s.H, s.B, s.Tw, s.Tf, s.R1, s.R2, s.FlangeSlope, s.Flipped, materialId) : null;

    /// <summary>Определение сечения либо причина; предупреждение — расхождение площади больше 1 %.</summary>
    /// <param name="profile">Профиль жёсткости со сталью.</param>
    /// <param name="materialId">Id стального материала проекта.</param>
    /// <param name="catalog">Сортамент OpenCS; null — без ссылки на каталог.</param>
    public static SteelSectionBuildResult Build(ImportedBarProfile profile, int materialId, ISteelCatalogLookup? catalog)
    {
        string who = $"жёсткость {profile.StiffnessNum} «{profile.SourceLabel}»";
        if (profile.Material != ImportedBarMaterial.Steel || profile.Steel is not { } s)
            return new(null, $"{who}: стержень не стальной", null);

        var definition = new ParametricSteelSectionDefinition
        {
            Kind = s.Kind, Fabrication = s.Fabrication,
            H = s.H, Bf1 = s.B, Tf1 = s.Tf, Tw = s.Tw, R1 = s.R1, R2 = s.R2, FlangeSlope = s.FlangeSlope,
            Flipped = s.Flipped && ParametricSteelSectionDefinition.CanFlip(s.Kind),
            MaterialId = materialId,
            Tag = $"{s.Name} {s.Standard}".Trim(),
        };
        var contour = ParametricSteelSectionGenerator.BuildCanonicalContour(definition, 8);
        if (contour is not var (outer, holes))
            return new(null, $"{who}: профиль «{s.Name}» не строится по размерам сортамента", null);
        definition = definition with { Catalog = catalog?.Find(s) };

        string? warning = null;
        if (s.ACm2 is double reference && reference > 0)
        {
            double area = (Area(outer) - holes.Sum(h => Math.Abs(Area(h)))) * 1e4;
            if (Math.Abs(area - reference) > AreaTolerance * reference)
                warning = $"{who}: площадь сечения «{s.Name}» {area:0.##} см² против {reference:0.##} см² по сортаменту";
        }
        return new(definition, null, warning);
    }

    /// <summary>
    /// Определение задаёт то же сечение, что создал бы <see cref="Build"/> для <paramref name="key"/>: вид,
    /// размеры до 0,1 мм, уклон, положение и материал. Имя и ссылка на каталог не сравниваются.
    /// </summary>
    public static bool Matches(ParametricSteelSectionDefinition d, SteelSectionKey key) =>
        !d.Rotated90 && Key(d.Kind, d.Fabrication, d.H, d.Bf1, d.Tw, d.Tf1, d.R1, d.R2, d.FlangeSlope, d.Flipped, d.MaterialId) == key;

    static SteelSectionKey Key(SteelProfileKind kind, SteelFabrication fabrication, double h, double b, double tw,
        double tf, double r1, double r2, double slope, bool flipped, int materialId) =>
        new(kind, fabrication, Mm(h), Mm(b), Mm(tw), Mm(tf), Mm(r1), Mm(r2), Math.Round(slope, 4),
            flipped && ParametricSteelSectionDefinition.CanFlip(kind), materialId);

    static double Mm(double m) => Math.Round(m * 1000, 1);

    static double Area(IReadOnlyList<(double X, double Y)> ring)
    {
        double a = 0;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            a += ring[j].X * ring[i].Y - ring[i].X * ring[j].Y;
        return a / 2;
    }
}
