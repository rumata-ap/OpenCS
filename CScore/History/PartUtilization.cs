using CScore.LimitSurface;

namespace CScore.History;

/// <summary>
/// Экстремальные деформации части относительно её включения и использование по пределам СП 63:
/// бетон — ε/εb2 по вершинам контуров (двузначная эпюра; уточнение (8.53) — подпроект B),
/// арматура — (ε + εp)/εs2 по точечным волокнам.
/// </summary>
internal static class PartUtilization
{
    /// <summary>Предельная деформация сжатия бетона εb2 (отрицательная).</summary>
    public static double LimitConcrete(MaterialArea area, CalcType calc) =>
        area.Material?.GetChars(calc)?.Ec2
        ?? throw new InvalidOperationException($"Область «{area.Tag}»: нет характеристик бетона для {calc}.");

    public static PartStepState Evaluate(StagedSection s, int partIndex, Kurvature k, CalcType calc)
    {
        var rel = s.RelativePlane(partIndex, k);
        double cMin = double.PositiveInfinity, cMax = double.NegativeInfinity, etaC = 0;
        double rMin = double.PositiveInfinity, rMax = double.NegativeInfinity, etaR = 0;
        (double X, double Y) cPt = default, rPt = default;

        foreach (var area in s.Parts[partIndex].Areas)
        {
            if (!MaterialArea.IsCalcActive(area)) continue;
            if (StrainCriterionGeometry.IsConcrete(area))
            {
                double eb2 = LimitConcrete(area, calc);
                foreach (var c in StrainCriterionGeometry.Contours(area))
                    foreach (var (x, y) in StrainCriterionGeometry.OpenVertices(c))
                    {
                        double e = rel.e0 + rel.ky * y + rel.kz * x;
                        if (e < cMin) { cMin = e; cPt = (x, y); }
                        cMax = Math.Max(cMax, e);
                        if (e < 0) etaC = Math.Max(etaC, e / eb2);
                    }
            }
            else
            {
                double esu = UlsStrainCriterion.RebarEpsSu(area, calc);
                foreach (var f in area.Fibers)
                {
                    if (f.TypeFiber != FiberType.point) continue;
                    double e = rel.e0 + rel.ky * f.Y + rel.kz * f.X + f.Eps_p;
                    rMin = Math.Min(rMin, e);
                    if (e > rMax) { rMax = e; rPt = (f.X, f.Y); }
                    if (e > 0) etaR = Math.Max(etaR, e / esu);
                }
            }
        }

        return new PartStepState
        {
            PartIndex = partIndex,
            ConcreteEpsMin = double.IsFinite(cMin) ? cMin : 0,
            ConcreteEpsMax = double.IsFinite(cMax) ? cMax : 0,
            RebarEpsMin = double.IsFinite(rMin) ? rMin : 0,
            RebarEpsMax = double.IsFinite(rMax) ? rMax : 0,
            EtaConcrete = etaC,
            EtaRebar = etaR,
            ConcreteMinPoint = cPt,
            RebarMaxPoint = rPt,
        };
    }
}
