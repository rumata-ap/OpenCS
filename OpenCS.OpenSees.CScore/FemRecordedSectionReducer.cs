using CScore;

namespace OpenCS.OpenSees.CScore;

/// <summary>Строка арматурного стержня в сводке записанного состояния сечения.</summary>
public sealed record FemRebarStateRow(int Num, double X, double Y, double Eps, double SigmaMpa);

/// <summary>Сводные значения состояния сечения по записанным волокнам OpenSees.</summary>
public sealed record FemRecordedSectionSummary(
    Kurvature Plane,
    double N,
    double Mx,
    double My,
    double EpsMin,
    double EpsMax,
    IReadOnlyList<FemRebarStateRow> Rebar);

/// <summary>Подгоняет плоскость деформаций и интегрирует усилия по записанным состояниям
/// волокон нелинейного FEM-расчёта OpenSees.</summary>
public static class FemRecordedSectionReducer
{
    /// <summary>Плоскость ε = e0 + kz·x + ky·y подгоняется МНК (нормальные уравнения 3×3);
    /// усилия интегрируются как N = Σσ·A, Mx = Σσ·A·y, My = Σσ·A·x
    /// (σ в Па, координаты в м, площадь в м²). При вырожденной системе — только e0.</summary>
    public static FemRecordedSectionSummary Reduce(
        CrossSection section,
        CalcType calcType,
        IReadOnlyDictionary<int, (double StressPa, double Strain)> recorded)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(recorded);

        double s00 = 0, s10 = 0, s01 = 0, s20 = 0, s11 = 0, s02 = 0;
        double r0 = 0, r1 = 0, r2 = 0;
        double n = 0, mx = 0, my = 0;
        double epsMin = double.PositiveInfinity, epsMax = double.NegativeInfinity;
        var rebar = new List<FemRebarStateRow>();
        int pointNum = 0;

        foreach (var (_, fiber, index) in section.EnumerateRecordedFibers(new Kurvature(), calcType))
        {
            if (!recorded.TryGetValue(index, out var rec)) continue;
            double x = fiber.X, y = fiber.Y, a = fiber.Area;
            double eps = rec.Strain, sig = rec.StressPa;

            n += sig * a;
            mx += sig * a * y;
            my += sig * a * x;
            if (eps < epsMin) epsMin = eps;
            if (eps > epsMax) epsMax = eps;
            if (fiber.TypeFiber == FiberType.point)
                rebar.Add(new FemRebarStateRow(++pointNum, x, y, eps, sig / 1e6));

            s00 += 1; s10 += x; s01 += y;
            s20 += x * x; s11 += x * y; s02 += y * y;
            r0 += eps; r1 += eps * x; r2 += eps * y;
        }

        var plane = FitPlane(s00, s10, s01, s20, s11, s02, r0, r1, r2);
        if (recorded.Count == 0) { epsMin = 0; epsMax = 0; }
        return new FemRecordedSectionSummary(plane, n, mx, my, epsMin, epsMax, rebar);
    }

    static Kurvature FitPlane(
        double s00, double s10, double s01, double s20, double s11, double s02,
        double r0, double r1, double r2)
    {
        double det = s00 * (s20 * s02 - s11 * s11)
                   - s10 * (s10 * s02 - s01 * s11)
                   + s01 * (s10 * s11 - s01 * s20);
        if (s00 <= 0 || Math.Abs(det) < 1e-12)
            return new Kurvature { e0 = s00 > 0 ? r0 / s00 : 0, ky = 0, kz = 0 };

        double de0 = r0 * (s20 * s02 - s11 * s11)
                   - s10 * (r1 * s02 - s11 * r2)
                   + s01 * (r1 * s11 - s20 * r2);
        double dkz = s00 * (r1 * s02 - s11 * r2)
                   - r0 * (s10 * s02 - s01 * s11)
                   + s01 * (s10 * r2 - s01 * r1);
        double dky = s00 * (s20 * r2 - s11 * r1)
                   - s10 * (s10 * r2 - s01 * r1)
                   + r0 * (s10 * s11 - s01 * s20);

        return new Kurvature { e0 = de0 / det, kz = dkz / det, ky = dky / det };
    }
}
