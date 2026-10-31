using CScore.Planar;
using CScore.Submodel;

namespace CScore.Fem.Fragment.Masks;

/// <summary>
/// Конец стержня фрагмента (спека разд. 4.2): перемещения и действие отброшенной части в точке КЭ стержня родителя.
/// </summary>
public static class FragmentBarCutSampler
{
    /// <summary>
    /// Перемещения в доле <paramref name="xi"/> КЭ I→J по перемещениям концов (общие оси): продольное и кручение —
    /// линейно, поперечные — Эрмит в местных осях (<see cref="BeamLocalAxisConvention"/>), повороты изгиба — производная.
    /// </summary>
    public static Dof6 Displacement(PlanarVector3 pi, PlanarVector3 pj, double rotationDeg, Dof6 ui, Dof6 uj, double xi)
    {
        var (ex, ey, ez) = BeamLocalAxisConvention.Frame(pi, pj, rotationDeg);
        double l = (pj - pi).Length;
        PlanarVector3 Loc(PlanarVector3 v) => new(v.Dot(ex), v.Dot(ey), v.Dot(ez));
        PlanarVector3 Glob(double x, double y, double z) => ex * x + ey * y + ez * z;
        var ti = Loc(ui.Force); var ri = Loc(ui.Moment);
        var tj = Loc(uj.Force); var rj = Loc(uj.Moment);

        double x2 = xi * xi, x3 = x2 * xi;
        double n1 = 1 - 3 * x2 + 2 * x3, n2 = xi - 2 * x2 + x3, n3 = 3 * x2 - 2 * x3, n4 = -x2 + x3;
        double d1 = (-6 * xi + 6 * x2) / l, d2 = 1 - 4 * xi + 3 * x2, d3 = (6 * xi - 6 * x2) / l, d4 = -2 * xi + 3 * x2;

        double ux = (1 - xi) * ti.X + xi * tj.X;
        double uy = n1 * ti.Y + n2 * l * ri.Z + n3 * tj.Y + n4 * l * rj.Z;
        double uz = n1 * ti.Z - n2 * l * ri.Y + n3 * tj.Z - n4 * l * rj.Y;
        double rx = (1 - xi) * ri.X + xi * rj.X;
        double rz = d1 * ti.Y + d2 * ri.Z + d3 * tj.Y + d4 * rj.Z;
        double ry = -(d1 * ti.Z - d2 * ri.Y + d3 * tj.Z - d4 * rj.Y);
        return Dof6.FromParts(Glob(ux, uy, uz), Glob(rx, ry, rz));
    }

    /// <summary>
    /// Действие части КЭ за точкой <paramref name="xi"/> (к узлу J) на часть до неё (к узлу I), общие оси, момент —
    /// относительно точки: из равновесия куска [0, ξ] — силы узла I на КЭ и нагрузки участка.
    /// </summary>
    public static Dof6 SectionAction(PlanarVector3 pi, PlanarVector3 pj, double rotationDeg, BeamEndForces f,
        IReadOnlyList<FragmentBeamSpanLoad> loads, double xi)
    {
        var (ex, ey, ez) = BeamLocalAxisConvention.Frame(pi, pj, rotationDeg);
        var d = pj - pi;
        double l = d.Length;
        var p = pi + d * xi;
        var fI = ex * f.I.X + ey * f.I.Y + ez * f.I.Z;
        var mI = ex * f.I.Rx + ey * f.I.Ry + ez * f.I.Rz;
        var sumF = fI;
        var sumM = mI + (pi - p).Cross(fI);
        const double eps = 1e-12;
        foreach (var q in loads)
        {
            if (q.IsPoint)
            {
                if (q.A > xi - eps) continue;
                var r = pi + d * q.A;
                sumF += q.Force;
                sumM += q.Moment + (r - p).Cross(q.Force);
                continue;
            }
            double a = q.A, b = Math.Min(q.B, xi);
            if (b <= a + eps) continue;
            PlanarVector3 Q(double t) => q.B - q.A < eps ? q.QA : q.QA + (q.QB - q.QA) * ((t - q.A) / (q.B - q.A));
            // Симпсон точен: q линейна, плечо линейно — подынтегральная функция момента квадратична.
            double len = (b - a) * l, m = (a + b) / 2;
            PlanarVector3 Arm(double t) => pi + d * t - p;
            sumF += (Q(a) + Q(m) * 4 + Q(b)) * (len / 6);
            sumM += (Arm(a).Cross(Q(a)) + Arm(m).Cross(Q(m)) * 4 + Arm(b).Cross(Q(b))) * (len / 6);
        }
        return Dof6.FromParts(sumF * -1, sumM * -1);
    }

    /// <summary>
    /// КЭ родителя, на оси которого лежит <paramref name="point"/> (в пределах <paramref name="tol"/>); точка в узле двух
    /// КЭ — КЭ на отброшенной стороне. <paramref name="outward"/> — нормаль разреза от фрагмента к отброшенной части
    /// (<see cref="FragmentCut.Outward"/>). null — такого КЭ нет.
    /// </summary>
    public static FragmentBarCutHit? Locate(PlanarVector3 point, PlanarVector3 outward, IEnumerable<FragmentBarElement> bars,
        double tol)
    {
        FragmentBarCutHit? best = null;
        foreach (var b in bars)
        {
            var d = b.PJ - b.PI;
            double l = d.Length;
            if (l < 1e-12) continue;
            double xi = (point - b.PI).Dot(d) / (l * l);
            if (xi < -tol / l || xi > 1 + tol / l) continue;
            xi = Math.Clamp(xi, 0, 1);
            if ((b.PI + d * xi - point).Length > tol) continue;
            bool towardJ = d.Dot(outward) > 0;
            // В узле: КЭ должен уходить в отброшенную сторону.
            bool atI = xi * l <= tol, atJ = (1 - xi) * l <= tol;
            if ((atI && !towardJ) || (atJ && towardJ)) continue;
            var hit = new FragmentBarCutHit(b, atI ? 0 : atJ ? 1 : xi, towardJ);
            if (best == null || !(atI || atJ)) best = hit;
        }
        return best;
    }

    /// <summary>
    /// Сэмпл маски конца стержня (s = 0): перемещения в точке и действие отброшенной части на фрагмент.
    /// <paramref name="loadsUnknown"/> — источник не знает нагрузок КЭ, а КЭ рассечён внутри (усилие — линейно).
    /// </summary>
    public static FragmentMaskSample Sample(FragmentBarCutHit hit, IFragmentParentResult r, FragmentMaskKey key,
        out bool loadsUnknown)
    {
        var e = hit.Element;
        Dof6? u = r.TryNodeDisplacement(key, e.NodeI, out var ui) && r.TryNodeDisplacement(key, e.NodeJ, out var uj)
            ? Displacement(e.PI, e.PJ, e.RotationDeg, ui, uj, hit.Xi)
            : null;
        Dof6? f = null;
        loadsUnknown = false;
        if (r.TryBeamEndForces(key, e.Tag, out var ends))
        {
            loadsUnknown = !r.KnowsBeamLoads && hit.Xi is > 0 and < 1;
            var action = SectionAction(e.PI, e.PJ, e.RotationDeg, ends, r.BeamLoads(key, e.Tag), hit.Xi);
            f = hit.DiscardedTowardJ ? action : -action;
        }
        return new FragmentMaskSample(0, u, f);
    }
}

/// <summary>КЭ стержня сетки родителя: узлы, координаты концов, угол поворота сечения (град).</summary>
public sealed record FragmentBarElement(string Tag, string NodeI, string NodeJ, PlanarVector3 PI, PlanarVector3 PJ,
    double RotationDeg);

/// <summary>
/// КЭ родителя у точки рассечения: доля <see cref="Xi"/> по длине от I; <see cref="DiscardedTowardJ"/> — отброшенная
/// часть лежит со стороны узла J.
/// </summary>
public sealed record FragmentBarCutHit(FragmentBarElement Element, double Xi, bool DiscardedTowardJ)
{
    public string ElemTag => Element.Tag;
}
