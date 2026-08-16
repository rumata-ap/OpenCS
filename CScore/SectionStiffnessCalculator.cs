using System;
using System.Collections.Generic;
using System.Linq;

namespace CScore;

/// <summary>Результат расчёта секущих и упругих жёсткостей сечения.</summary>
public readonly record struct SectionStiffnessResult(
    double Xc_mm, double Yc_mm,
    double EA_kN, double EIy0_kNm2, double EIz0_kNm2,
    double EIyc_kNm2, double EIzc_kNm2,
    double EAel_kN, double EIyel_kNm2, double EIzel_kNm2,
    double PhiEA, double PhiEIy, double PhiEIz);

/// <summary>
/// Рассчитывает жёсткости сечения по секущему модулю σ/ε.
/// Колбэки позволяют передать эффективные напряжения и деформации отдельных
/// фибр, не меняя сохранённое состояние сечения. Это используется, в частности,
/// для фиктивного напряжения растянутой арматуры σ/ψs по п. 8.2.32 СП 63.13330.
/// </summary>
public static class SectionStiffnessCalculator
{
    /// <summary>
    /// Рассчитывает секущие и упругие жёсткости.
    /// <paramref name="effectiveStressKpaByFiber"/> применяется к точечным фибрам
    /// арматуры; для остальных фибр используются значения диаграммы.
    /// </summary>
    public static SectionStiffnessResult? Compute(
        CrossSection section,
        Kurvature k,
        CalcType calcType,
        int gridDensity = 20,
        bool ten = true,
        Func<Fiber, double>? effectiveStressKpaByFiber = null,
        Func<Fiber, double>? effectiveStrainByFiber = null)
    {
        ArgumentNullException.ThrowIfNull(section);

        // Единицы ввода: площадь [м²], координаты [м], E [МПа=Н/мм²]
        // Единицы вывода: EA [кН], EI [кН·м²], ц.т. [мм]
        double EA = 0, ESy = 0, ESz = 0, EIy = 0, EIz = 0;
        double EAe = 0, ESye = 0, ESze = 0, EIye = 0, EIze = 0;

        foreach (var (area, ka) in section.EnumerateAreas(k))
        {
            if (!area.Diagramms.TryGetValue(calcType, out var dgr))
                continue;

            // SigValue возвращает кПа; переводим в МПа и делим на ε.
            double E0 = Math.Abs(dgr.SigValue(1e-7)) / 1e-7 / 1000.0;
            bool hasMesh = area.Fibers.Any(f => f.TypeFiber != FiberType.point);

            if (hasMesh)
            {
                foreach (var f in area.Fibers.Where(f => f.TypeFiber != FiberType.point))
                {
                    double strain = effectiveStrainByFiber?.Invoke(f) ?? f.Eps;
                    double stressKpa = f.Sig;
                    double Es = Math.Abs(strain) > 1e-9
                        ? Math.Abs(stressKpa / 1000.0 / strain) : E0;
                    double areaMm2 = f.Area * 1e6;
                    double xMm = f.X * 1000;
                    double yMm = f.Y * 1000;
                    Acc(Es, E0, areaMm2, xMm, yMm,
                        ref EA, ref ESy, ref ESz, ref EIy, ref EIz,
                        ref EAe, ref ESye, ref ESze, ref EIye, ref EIze);
                }
            }
            else if (area.Hull != null && area.Category == AreaCategory.Region)
            {
                var hullMm = area.Hull.X
                    .Zip(area.Hull.Y, (x, y) => (X: x * 1000, Y: y * 1000))
                    .SkipLast(1).ToList();
                var holesMm = area.Holes.Select(h =>
                    h.X.Zip(h.Y, (x, y) => (X: x * 1000, Y: y * 1000))
                       .SkipLast(1).ToList()).ToList();
                double hmXMin = hullMm.Min(p => p.X), hmXMax = hullMm.Max(p => p.X);
                double hmYMin = hullMm.Min(p => p.Y), hmYMax = hullMm.Max(p => p.Y);
                double step = Math.Max(hmXMax - hmXMin, hmYMax - hmYMin)
                    / Math.Max(gridDensity, 1);
                if (step < 1.0) step = 1.0;
                var xs = BuildSteps(hmXMin, hmXMax, step);
                var ys = BuildSteps(hmYMin, hmYMax, step);

                for (int xi = 0; xi < xs.Count - 1; xi++)
                for (int yi = 0; yi < ys.Count - 1; yi++)
                {
                    var cell = GridSplit.ClipByRect(
                        hullMm, xs[xi], xs[xi + 1], ys[yi], ys[yi + 1]);
                    if (cell.Count < 3) continue;
                    double cxMm = cell.Average(p => p.X);
                    double cyMm = cell.Average(p => p.Y);
                    if (holesMm.Any(h => PointInPolyMm(cxMm, cyMm, h))) continue;

                    double eps = ka.e0 + ka.ky * (cyMm / 1000) + ka.kz * (cxMm / 1000);
                    double sig = dgr.SigValue(eps, ten) / 1000.0;
                    double Es = Math.Abs(eps) > 1e-9 ? Math.Abs(sig / eps) : E0;
                    double cellAreaMm2 = PolygonAreaMm2(cell);
                    Acc(Es, E0, cellAreaMm2, cxMm, cyMm,
                        ref EA, ref ESy, ref ESz, ref EIy, ref EIz,
                        ref EAe, ref ESye, ref ESze, ref EIye, ref EIze);
                }
            }

            foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
            {
                double stressKpa = effectiveStressKpaByFiber?.Invoke(f) ?? f.Sig;
                double strain = effectiveStrainByFiber?.Invoke(f) ?? f.Eps;
                double Es = Math.Abs(strain) > 1e-9
                    ? Math.Abs(stressKpa / 1000.0 / strain) : E0;
                double areaMm2 = f.Area * 1e6;
                double xMm = f.X * 1000;
                double yMm = f.Y * 1000;
                Acc(Es, E0, areaMm2, xMm, yMm,
                    ref EA, ref ESy, ref ESz, ref EIy, ref EIz,
                    ref EAe, ref ESye, ref ESze, ref EIye, ref EIze);
            }
        }

        if (EA < 1e-6) return null;

        double xc = ESy / EA;
        double yc = ESz / EA;
        double EIyc = EIy - ESy * ESy / EA;
        double EIzc = EIz - ESz * ESz / EA;
        double EIyelc = EAe > 1e-6 ? EIye - ESye * ESye / EAe : 0;
        double EIzec = EAe > 1e-6 ? EIze - ESze * ESze / EAe : 0;

        static double Ratio(double a, double b) =>
            b > 1e-6 ? a / b : double.NaN;

        return new SectionStiffnessResult(
            Xc_mm: xc,
            Yc_mm: yc,
            EA_kN: EA / 1e3,
            EIy0_kNm2: EIy / 1e9,
            EIz0_kNm2: EIz / 1e9,
            EIyc_kNm2: EIyc / 1e9,
            EIzc_kNm2: EIzc / 1e9,
            EAel_kN: EAe / 1e3,
            EIyel_kNm2: EIyelc / 1e9,
            EIzel_kNm2: EIzec / 1e9,
            PhiEA: Ratio(EA, EAe),
            PhiEIy: Ratio(EIyc, EIyelc),
            PhiEIz: Ratio(EIzc, EIzec));
    }

    static List<double> BuildSteps(double lo, double hi, double step)
    {
        var result = new List<double> { lo };
        int iLo = (int)Math.Ceiling(lo / step);
        int iHi = (int)Math.Floor(hi / step);
        for (int i = iLo; i <= iHi; i++)
        {
            double value = i * step;
            if (value > lo + step * 0.01 && value < hi - step * 0.01)
                result.Add(value);
        }
        result.Add(hi);
        return result;
    }

    static double PolygonAreaMm2(List<(double X, double Y)> verts)
    {
        double area = 0;
        int n = verts.Count;
        for (int i = 0; i < n; i++)
        {
            var a = verts[i];
            var b = verts[(i + 1) % n];
            area += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(area) * 0.5;
    }

    static bool PointInPolyMm(double px, double py, List<(double X, double Y)> verts)
    {
        int n = verts.Count;
        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = verts[i].X, yi = verts[i].Y;
            double xj = verts[j].X, yj = verts[j].Y;
            if (((yi > py) != (yj > py)) &&
                px < (xj - xi) * (py - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    static void Acc(
        double Es, double E0, double areaMm2, double xMm, double yMm,
        ref double EA, ref double ESy, ref double ESz,
        ref double EIy, ref double EIz,
        ref double EAe, ref double ESye, ref double ESze,
        ref double EIye, ref double EIze)
    {
        EA += Es * areaMm2;
        ESy += Es * areaMm2 * xMm;
        ESz += Es * areaMm2 * yMm;
        EIy += Es * areaMm2 * xMm * xMm;
        EIz += Es * areaMm2 * yMm * yMm;
        EAe += E0 * areaMm2;
        ESye += E0 * areaMm2 * xMm;
        ESze += E0 * areaMm2 * yMm;
        EIye += E0 * areaMm2 * xMm * xMm;
        EIze += E0 * areaMm2 * yMm * yMm;
    }
}
