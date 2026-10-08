using CScore.ParametricSteel;
using CScore.Sp16;

namespace CScore.Import;

/// <summary>Упругие характеристики стального профиля в местных осях стержня (м): A, Iy (вокруг горизонтальной Y1), Iz, It.</summary>
public sealed record ImportedSteelElasticProps(double A, double Iy, double Iz, double It);

/// <summary>
/// Упругие характеристики стального профиля импорта (сортамент SCAD/ЛИРЫ) для линейного расчёта стержня: A, Iy, Iz —
/// по каноническому контуру профиля (как МК-сечение <see cref="SteelSectionBuilder"/>: горизонтальная ось контура —
/// Y1, вертикальная — Z1), It — по прил. Д СП 16 (двутавр, тавр, швеллер) либо по формулам замкнутых и сплошных
/// сечений. Угол главных осей несимметричных профилей (уголок) не учитывается — берутся оси контура.
/// </summary>
public static class ImportedSteelElastic
{
    /// <summary>Характеристики профиля; null — контур по размерам не строится.</summary>
    public static ImportedSteelElasticProps? Compute(ImportedSteelShape s)
    {
        var d = new ParametricSteelSectionDefinition
        {
            Kind = s.Kind, Fabrication = s.Fabrication,
            H = s.H, Bf1 = s.B, Tf1 = s.Tf, Tw = s.Tw, R1 = s.R1, R2 = s.R2, FlangeSlope = s.FlangeSlope,
            Flipped = s.Flipped && ParametricSteelSectionDefinition.CanFlip(s.Kind),
        };
        if (ParametricSteelSectionGenerator.BuildCanonicalContour(d, 8) is not var (outer, holes)) return null;
        var poly = new PolygonSection(outer, holes);
        double it = s.Kind switch
        {
            SteelProfileKind.IBeam or SteelProfileKind.Tee or SteelProfileKind.Channel =>
                new Sp16Section(poly, ParametricSteelSectionGenerator.ToSteelProfile(d), new SteelMaterialProps(0, 0, null, null, SteelMaterialProps.DefaultE)).ItFormula,
            SteelProfileKind.Round => Math.PI * Math.Pow(s.H, 4) / 32,
            SteelProfileKind.Pipe => Math.PI * (Math.Pow(s.H, 4) - Math.Pow(s.H - 2 * s.Tw, 4)) / 32,
            // Брэдт по средней линии: 4·Am²·t / периметр.
            SteelProfileKind.Box => 4 * Math.Pow((s.H - s.Tw) * (s.B - s.Tw), 2) * s.Tw / (2 * ((s.H - s.Tw) + (s.B - s.Tw))),
            SteelProfileKind.Angle => (s.H + s.B - s.Tw) * Math.Pow(s.Tw, 3) / 3,
            SteelProfileKind.Rect => RectTorsion(s.H, s.B > 0 ? s.B : s.H),
            _ => 0,
        };
        return new ImportedSteelElasticProps(poly.A, poly.Ix, poly.Iy, it);
    }

    static double RectTorsion(double h, double b)
    {
        double lo = Math.Min(b, h), hi = Math.Max(b, h);
        return lo * lo * lo * hi * (1.0 / 3 - 0.21 * lo / hi * (1 - Math.Pow(lo / hi, 4) / 12));
    }
}
