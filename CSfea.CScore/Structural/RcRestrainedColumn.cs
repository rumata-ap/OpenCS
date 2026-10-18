using CSfea.Core;

namespace CSfea.CScoreBridge.Structural;

/// <summary>
/// Эквивалентная стойка элемента в плоскости (спека 2026-10-18, п. 3Е): пружины <paramref name="Restraint"/> (r₁, r₂ —
/// поворот концов, Н·м/рад; r₃ — смещение верха, Н/м), безразмерные ρ₁ = r₁·l/EI, ρ₂ = r₂·l/EI, ρ₃ = r₃·l³/EI, корень
/// определителя u = l·√(N_cr/EI) (NaN — корня нет) и μ = π/u.
/// </summary>
public sealed record RcEquivalentColumn(PlaneRestraint Restraint, double Rho1, double Rho2, double Rho3, double U, double Mu);

/// <summary>
/// Стойка постоянного сечения длиной l, сжатая силой N, низ несмещаем, пружины поворота низа r₁ и верха r₂, поперечная
/// пружина верха r₃ (Масленников А.М. Динамика и устойчивость сооружений, 2017, с. 218; при r₃ = 0 — ур. (D.3) ASCE
/// 1997 для рам со смещением, при r₃ = ∞ — для рам без смещения). Прогиб y = A·sin kx + B·(cos kx − 1) + C·x,
/// k = u/l, u = l·√(N/EI); граничные условия из Π = ½∫EI·y″² − ½∫N·y′² + ½r₁y′(0)² + ½r₂y′(l)² + ½r₃y(l)²:
/// −EI·y″(0) + r₁y′(0) = 0, EI·y″(l) + r₂y′(l) = 0, r₃y(l) − (EI·y‴ + N·y′)(l) = 0, где EI·y‴ + N·y′ = EI·k²·C.
/// Каждая пружина входит углом φ = arctg ρ (ρ = ∞ — закрепление, отрицательная — окружение нагружает элемент).
/// </summary>
public static class RcRestrainedColumn
{
    /// <summary>Определитель системы граничных условий по (A, B, C·l) при параметре u.</summary>
    public static double Determinant(double u, double rho1, double rho2, double rho3)
    {
        var (s1, c1) = Angle(rho1);
        var (s2, c2) = Angle(rho2);
        var (s3, c3) = Angle(rho3);
        double su = Math.Sin(u), cu = Math.Cos(u), u2 = u * u;
        double a11 = u * s1, a12 = u2 * c1, a13 = s1;
        double a21 = -u2 * su * c2 + u * cu * s2, a22 = -u2 * cu * c2 - u * su * s2, a23 = s2;
        double a31 = s3 * su, a32 = s3 * (cu - 1), a33 = s3 - c3 * u2;
        return a11 * (a22 * a33 - a23 * a32) - a12 * (a21 * a33 - a23 * a31) + a13 * (a21 * a32 - a22 * a31);
    }

    /// <summary>
    /// Корни определителя по u на [π/μmax, π/μmin] (сетка в логарифмическом масштабе, уточнение делением пополам) по
    /// возрастанию.
    /// </summary>
    public static List<double> Roots(double rho1, double rho2, double rho3, double muMin = 0.25, double muMax = 100)
    {
        const int steps = 4000;
        double lo = Math.PI / muMax, hi = Math.PI / muMin, q = Math.Pow(hi / lo, 1.0 / steps);
        var roots = new List<double>();
        double a = lo, fa = Determinant(a, rho1, rho2, rho3);
        for (int i = 1; i <= steps; i++)
        {
            double b = lo * Math.Pow(q, i), fb = Determinant(b, rho1, rho2, rho3);
            if (fa == 0) roots.Add(a);
            else if (fa * fb < 0)
            {
                double x0 = a, x1 = b, f0 = fa;
                for (int it = 0; it < 100 && x1 - x0 > 1e-13 * x1; it++)
                {
                    double m = 0.5 * (x0 + x1), fm = Determinant(m, rho1, rho2, rho3);
                    if (f0 * fm <= 0) x1 = m;
                    else (x0, f0) = (m, fm);
                }
                roots.Add(0.5 * (x0 + x1));
            }
            (a, fa) = (b, fb);
        }
        return roots;
    }

    /// <summary>
    /// Эквивалентная стойка по пружинам <paramref name="r"/>: корень, ближайший к <paramref name="uNear"/> (параметр
    /// найденной критической силы), без него — наименьший.
    /// </summary>
    public static RcEquivalentColumn Solve(PlaneRestraint r, double ei, double l, double uNear = double.NaN)
    {
        double rho1 = r.R1 * l / ei, rho2 = r.R2 * l / ei, rho3 = r.R3 * l * l * l / ei;
        var roots = Roots(rho1, rho2, rho3);
        double u = roots.Count == 0 ? double.NaN
            : double.IsFinite(uNear) ? roots.MinBy(x => Math.Abs(Math.Log(x / uNear))) : roots[0];
        return new RcEquivalentColumn(r, rho1, rho2, rho3, u, Math.PI / u);
    }

    static (double S, double C) Angle(double rho)
    {
        if (double.IsPositiveInfinity(rho)) return (1, 0);
        double phi = Math.Atan(rho);
        return (Math.Sin(phi), Math.Cos(phi));
    }
}
