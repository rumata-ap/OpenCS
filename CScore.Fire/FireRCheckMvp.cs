using CScore;
using CScore.Fire.Entities;

namespace CScore.Fire;

/// <summary>
/// R-проверка MVP: единые γ_bt и min γ_st по представительной температуре.
/// </summary>
public static class FireRCheckMvp
{
    /// <summary>Выполняет упрощённую R-проверку с глобальной редукцией прочностей.</summary>
    public static FireCheckResult Run(
        FireThermalResult thermal,
        CrossSection section,
        double n,
        double mx,
        double my,
        CalcType calc = CalcType.C,
        int snapshotIndex = -1,
        FireSectionDef? fireDef = null,
        int? thermalResultId = null)
    {
        ArgumentNullException.ThrowIfNull(thermal);
        ArgumentNullException.ThrowIfNull(section);

        string aggregate = thermal.AggregateType;
        double gammaBt = FireRCheckGamma.EffectiveConcreteGamma(thermal, aggregate, snapshotIndex);
        double gammaSt = FireRCheckGamma.EffectiveRebarGammaMin(thermal, section, snapshotIndex);

        CrossSection reduced = FireMaterialReducer.CreateReduced(section, gammaBt, gammaSt);
        reduced.ResolveAndBuildDiagramms();

        var limit = new CrossSectionLimitAdapter(reduced, calc);
        var solver = new LimitForceSolver(limit, reduced, calc);
        LimitForceResult res = solver.AllFactor(n, mx, my);

        var check = FireRCheckResultBuilder.Build(
            res,
            thermal,
            snapshotIndex,
            method: "mvp",
            n, mx, my,
            fireDef,
            thermalResultId,
            section,
            extra: new Dictionary<string, object?>
            {
                ["gamma_bt"] = gammaBt,
                ["gamma_st_min"] = gammaSt,
                ["non_normative"] = true,
                ["non_normative_reason"] =
                    "Глобальная редукция по представительной температуре; растянутый бетон "
                  + "не исключается по п. 8.42. Путь диагностический, для нормативного расчёта "
                  + "использовать method=fiber."
            });

        if (res.StrainPlane is Kurvature sp)
            FireRCheckResultBuilder.FillActualForces(check, limit.Integral(sp, calc));

        return check;
    }
}
