using System;
using System.Collections.Generic;
using System.Linq;

namespace CScore;

/// <summary>
/// Геометрия сечения для ширины раскрытия трещин по направлению плоскости трещинообразования
/// (п. 8.2.17 СП 63, обобщение на косой изгиб, принятое в OpenCS). Плоскость
/// ε = e0 + ky·y + kz·x; градиент g = (kz, ky), n = g/|g| — направление роста растяжения,
/// координата волокна s = n·(x, y), так что ε = e0 + |g|·s. Норма описывает одноосный
/// изгиб; при kz = 0 правило совпадает с ним.
/// </summary>
internal static class CrackSectionGeometry
{
    /// <summary>Перепад деформации по сечению, ниже которого плоскость считается равномерной (центральное растяжение/сжатие).</summary>
    const double UniformStrainTol = 1e-9;

    /// <summary>Геометрические величины п. 8.2.17 по направлению n.</summary>
    /// <param name="H">Высота сечения по n: s_t − s_c, м.</param>
    /// <param name="A">Расстояние от крайнего растянутого волокна до центра тяжести растянутой арматуры, м.</param>
    /// <param name="H0">Рабочая высота s̄ − s_c (для вывода), м.</param>
    /// <param name="Xt">Высота растянутой зоны плоскости трещинообразования s_t − s0, м (+∞ — сечение растянуто целиком при |g| = 0).</param>
    /// <param name="XtEff">Высота растянутой зоны для Abt: xt в пределах [2a; 0,5h], м.</param>
    /// <param name="Abt">Площадь растянутого бетона, м².</param>
    /// <param name="WholeSectionInTension">Плоскость трещинообразования растягивает всё сечение — Abt равна всей площади бетона.</param>
    internal readonly record struct Result(double H, double A, double H0, double Xt, double XtEff, double Abt,
        bool WholeSectionInTension);

    /// <summary>
    /// Вычисляет h, a, h0, xt и Abt.
    /// <para>
    /// Abt — площадь бетона в полосе s ≥ s_t − xt,eff, xt,eff = xt в пределах [2a; 0,5h] (при
    /// 2a &gt; 0,5h — 2a, как и прежде). Если плоскость трещинообразования растягивает всё
    /// сечение (xt ≥ h, в том числе центральное растяжение |g| ≈ 0), Abt — вся площадь бетона:
    /// растянутой считается и арматура обеих граней, и ls по (8.136) остаётся непрерывным при
    /// переходе к центральному растяжению (при 0,5h площадь на всю арматуру обеих граней
    /// занизила бы ls вдвое).
    /// </para>
    /// </summary>
    /// <param name="crackPlane">Плоскость в момент трещинообразования (ten = true).</param>
    /// <param name="servicePlane">Пост-трещинная плоскость: направление n, если плоскость трещинообразования равномерна.</param>
    /// <param name="tensileCentroid">Центр тяжести растянутой арматуры (полная площадь стержней).</param>
    public static Result Compute(CrossSection section, Kurvature crackPlane, Kurvature servicePlane,
        (double X, double Y) tensileCentroid)
    {
        var areas = section.Areas
            .Where(a => a.Material?.Type == MatType.Concrete && a.Hull != null && a.Hull.X.Count >= 3)
            .ToList();
        if (areas.Count == 0) return default;

        var allVertices = areas
            .SelectMany(a => a.Holes.Prepend(a.Hull!))
            .SelectMany(c => c.X.Zip(c.Y, (x, y) => (X: x, Y: y)))
            .ToList();

        var (nx, ny, g) = Direction(crackPlane, allVertices);
        if (g == 0.0)
        {
            var (snx, sny, sg) = Direction(servicePlane, allVertices);
            (nx, ny) = sg > 0.0 ? (snx, sny) : (0.0, -1.0);
        }

        double sT = allVertices.Max(p => nx * p.X + ny * p.Y);
        double sC = allVertices.Min(p => nx * p.X + ny * p.Y);
        double h = sT - sC;
        double sBar = nx * tensileCentroid.X + ny * tensileCentroid.Y;
        double a = sT - sBar;
        double h0 = sBar - sC;

        double xt = g > 0.0 ? sT + crackPlane.e0 / g : (crackPlane.e0 > 0.0 ? double.PositiveInfinity : 0.0);
        double totalArea = areas.Sum(NetArea);

        if (xt >= h)
            return new Result(h, a, h0, xt, h, totalArea, WholeSectionInTension: true);

        double xtEff = Math.Max(2.0 * a, Math.Min(Math.Max(xt, 0.0), 0.5 * h));
        double sCut = sT - xtEff;
        double abt = 0.0;
        foreach (var area in areas)
        {
            abt += ClippedArea(area.Hull!, nx, ny, sCut);
            foreach (var hole in area.Holes)
                abt -= ClippedArea(hole, nx, ny, sCut);
        }

        return new Result(h, a, h0, xt, xtEff, Math.Max(abt, 0.0), WholeSectionInTension: false);
    }

    /// <summary>
    /// Единичное направление градиента плоскости и его модуль; модуль 0 — перепад деформации
    /// по сечению меньше <see cref="UniformStrainTol"/> (плоскость равномерна).
    /// </summary>
    static (double Nx, double Ny, double G) Direction(Kurvature plane, List<(double X, double Y)> vertices)
    {
        double g = Math.Sqrt(plane.kz * plane.kz + plane.ky * plane.ky);
        if (g <= 0.0 || !double.IsFinite(g)) return (0.0, 0.0, 0.0);
        double nx = plane.kz / g, ny = plane.ky / g;
        double spread = vertices.Max(p => nx * p.X + ny * p.Y) - vertices.Min(p => nx * p.X + ny * p.Y);
        return g * spread <= UniformStrainTol ? (0.0, 0.0, 0.0) : (nx, ny, g);
    }

    static double NetArea(MaterialArea area) =>
        RingArea(area.Hull!) - area.Holes.Sum(RingArea);

    /// <summary>Площадь части контура в полуплоскости s ≥ sCut.</summary>
    static double ClippedArea(Contour ring, double nx, double ny, double sCut)
    {
        var verts = ring.X.Zip(ring.Y, (x, y) => (x, y)).ToList();
        var clipped = GridSplit.ClipByHalfPlane(verts, nx * sCut, ny * sCut, nx, ny);
        return ShoelaceArea(clipped);
    }

    static double RingArea(Contour ring) =>
        ShoelaceArea(ring.X.Zip(ring.Y, (x, y) => (x, y)).ToList());

    /// <summary>Площадь многоугольника с замыканием последнего ребра (повтор первой вершины в конце не мешает).</summary>
    static double ShoelaceArea(List<(double X, double Y)> v)
    {
        if (v.Count < 3) return 0.0;
        double s = 0.0;
        for (int i = 0; i < v.Count; i++)
        {
            var p = v[i];
            var q = v[(i + 1) % v.Count];
            s += p.X * q.Y - q.X * p.Y;
        }
        return Math.Abs(0.5 * s);
    }
}
