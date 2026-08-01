using System;
using System.Collections.Generic;
using CScore;

namespace OpenCS.Tasks;

/// <summary>
/// Проверка центрального сжатия: прочность 8.1.1 + устойчивость 9.1.1.
/// </summary>
public class SteelCentralCompressionHandler : SteelTaskHandlerBase
{
    public override string Kind => "steel_central_compression";

    protected override CalcResult Execute(
        CalcTask task, string created, CrossSection section,
        SteelSection steelSection, InternalForces forces, DesignContext context)
    {
        var fy = steelSection.Steel.C?.Ry ?? 235e6;
        var gammaM = context.GammaM;
        var E = steelSection.Steel.E;

        var lambdaBarX = SteelStabilityCheck.ConventionalSlenderness(
            context.DesignLengthX * context.MuX, steelSection.ix, E, fy);
        var lambdaBarY = SteelStabilityCheck.ConventionalSlenderness(
            context.DesignLengthY * context.MuY, steelSection.iy, E, fy);
        var chiX = SteelStabilityCheck.Chi(lambdaBarX, context.BucklingCurveX);
        var chiY = SteelStabilityCheck.Chi(lambdaBarY, context.BucklingCurveY);

        // Прочность 8.1.1 — по наименьшему χ
        double chiMin = Math.Min(chiX, chiY);
        var details = new List<CheckDetail>();
        details.Add(SteelStrengthCheck.CheckAxialCompression(
            forces.N, chiMin, steelSection.Area, fy, gammaM));

        // Устойчивость 9.1.1 — отдельно по каждой плоскости
        details.Add(SteelStabilityCheck.CheckBuckling(
            forces.N, chiX, steelSection.Area, fy, gammaM,
            lambdaBarX, context.BucklingCurveX, "X"));
        details.Add(SteelStabilityCheck.CheckBuckling(
            forces.N, chiY, steelSection.Area, fy, gammaM,
            lambdaBarY, context.BucklingCurveY, "Y"));

        return Ok(task, created, section.Tag, steelSection.Steel.Tag, [.. details], forces, context);
    }
}
