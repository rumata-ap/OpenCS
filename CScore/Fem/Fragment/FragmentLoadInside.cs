using CScore.Fem.Loads;
using CScore.Planar;

namespace CScore.Fem.Fragment;

/// <summary>Слагаемое нагрузки: сила <see cref="F"/> и момент <see cref="M"/> в точке <see cref="At"/> (общие оси).</summary>
public readonly record struct FragmentLoadTerm(PlanarVector3 At, PlanarVector3 F, PlanarVector3 M);

/// <summary>
/// Часть нагрузки КЭ внутри <see cref="FragmentBounds"/> по виду — то же правило, каким построитель переносит нагрузки
/// во фрагмент (спека 2026-11-05 «нагрузки КЭ на гранях фрагмента по виду», разд. 3): сосредоточенная — по точке
/// (<see cref="FragmentBounds.InBox"/>), погонная — обрезком по границам (две силы в концах обрезка, точны по ΣF и ΣM),
/// по площади — интегралом по отсечённому многоугольнику КЭ (Дунавант 5 по функциям формы).
/// </summary>
public static class FragmentLoadInside
{
    /// <summary>Квадратура Дунаванта степени 5 на треугольнике: барицентрические координаты и вес (доля площади).</summary>
    static readonly (double L1, double L2, double L3, double W)[] Dunavant5 =
    [
        (1.0 / 3, 1.0 / 3, 1.0 / 3, 0.225),
        (0.059715871789770, 0.470142064105115, 0.470142064105115, 0.132394152788506),
        (0.470142064105115, 0.059715871789770, 0.470142064105115, 0.132394152788506),
        (0.470142064105115, 0.470142064105115, 0.059715871789770, 0.132394152788506),
        (0.797426985353087, 0.101286507323456, 0.101286507323456, 0.125939180544827),
        (0.101286507323456, 0.797426985353087, 0.101286507323456, 0.125939180544827),
        (0.101286507323456, 0.101286507323456, 0.797426985353087, 0.125939180544827),
    ];

    /// <summary>Слагаемые всех частей <paramref name="pieces"/> внутри границ.</summary>
    public static IReadOnlyList<FragmentLoadTerm> Terms(FragmentBounds bounds, IEnumerable<FemLoadPiece> pieces)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        var result = new List<FragmentLoadTerm>();
        foreach (var p in pieces) result.AddRange(Terms(bounds, p));
        return result;
    }

    /// <summary>Слагаемые части <paramref name="piece"/> внутри границ; пусто — часть снаружи.</summary>
    public static IReadOnlyList<FragmentLoadTerm> Terms(FragmentBounds bounds, FemLoadPiece piece)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(piece);
        switch (piece)
        {
            case FemPointPiece p:
                return bounds.InBox(p.At) ? [new FragmentLoadTerm(p.At, p.F, p.M)] : [];

            case FemLinePiece l:
            {
                var d = l.B - l.A;
                var (lo, hi) = bounds.ClipRange(l.A, d, 0, 1);
                if (hi - lo <= 1e-12) return [];
                PlanarVector3 a = l.A + d * lo, b = l.A + d * hi;
                PlanarVector3 qa = l.QA + (l.QB - l.QA) * lo, qb = l.QA + (l.QB - l.QA) * hi;
                double len = (b - a).Length;
                return
                [
                    new FragmentLoadTerm(a, (qa * 2 + qb) * (len / 6), PlanarVector3.Zero),
                    new FragmentLoadTerm(b, (qa + qb * 2) * (len / 6), PlanarVector3.Zero),
                ];
            }

            case FemSurfacePiece s:
            {
                var g = s.Geometry;
                var polygon = bounds.ClipPolygon(g.Contour.Select(i => g.Points[i]).ToList());
                var result = new List<FragmentLoadTerm>();
                for (int k = 1; k + 1 < polygon.Count; k++)
                {
                    var (a, b, c) = (polygon[0], polygon[k], polygon[k + 1]);
                    double area = 0.5 * (b - a).Cross(c - a).Length;
                    if (area <= 0) continue;
                    foreach (var (l1, l2, l3, w) in Dunavant5)
                    {
                        var p = a * l1 + b * l2 + c * l3;
                        if (g.ShellShapeAt(p) is not { } n) continue;
                        var q = PlanarVector3.Zero;
                        for (int i = 0; i < n.Length; i++) q += s.Q[i] * n[i];
                        result.Add(new FragmentLoadTerm(p, q * (w * area), PlanarVector3.Zero));
                    }
                }
                return result;
            }

            default:
                throw new NotSupportedException(piece.GetType().Name);
        }
    }
}
