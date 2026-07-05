using CScore;
using CScore.Fire.Entities;

namespace CScore.Fire;

/// <summary>R-проверка по фибровому методу (поэлементная γ-редукция).</summary>
public static class FireRCheckFiber
{
    /// <summary>
    /// Выполняет R-проверку для одной комбинации усилий.
    /// </summary>
    public static FireCheckResult Run(
        FireThermalResult thermal,
        CrossSection section,
        double n,
        double mx,
        double my,
        CalcType calc = CalcType.C,
        int snapshotIndex = -1,
        FireSectionDef? fireDef = null,
        int? thermalResultId = null,
        double sp63EtaMin = 0.85,
        bool rebarDifferentialDiagram = true,
        IReadOnlyList<Diagramm>? diagramPool = null)
    {
        ArgumentNullException.ThrowIfNull(thermal);
        ArgumentNullException.ThrowIfNull(section);

        section.ResolveAndBuildDiagramms(sp63EtaMin, diagramPool, rebarDifferentialDiagram);
        var fiber = FireFiberSection.FromThermalResult(thermal, section, snapshotIndex);
        var solver = new LimitForceSolver(fiber, fiber.SourceSection, calc);
        LimitForceResult res = solver.AllFactor(n, mx, my);

        var gammaBt = fiber.ConcreteElements.Select(e => e.GammaBt).ToList();
        var gammaStC = fiber.RebarElements.Select(e => e.GammaStC).ToList();
        var gammaStT = fiber.RebarElements.Select(e => e.GammaStT).ToList();

        var check = FireRCheckResultBuilder.Build(
            res,
            thermal,
            snapshotIndex,
            method: "fiber",
            n, mx, my,
            fireDef,
            thermalResultId,
            extra: new Dictionary<string, object?>
            {
                ["gamma_bt_avg"] = gammaBt.Count > 0 ? gammaBt.Average() : 1.0,
                ["gamma_bt_min"] = gammaBt.Count > 0 ? gammaBt.Min() : 1.0,
                ["gamma_bt_max"] = gammaBt.Count > 0 ? gammaBt.Max() : 1.0,
                ["gamma_st_c_min"] = gammaStC.Count > 0 ? gammaStC.Min() : 1.0,
                ["gamma_st_t_min"] = gammaStT.Count > 0 ? gammaStT.Min() : 1.0,
                ["n_concrete_elements"] = fiber.ConcreteElements.Count,
                ["n_rebar_elements"] = fiber.RebarElements.Count
            });

        if (res.StrainPlane is Kurvature sp)
            FireRCheckResultBuilder.FillActualForces(check, fiber.Integral(sp, calc));

        return check;
    }
}
