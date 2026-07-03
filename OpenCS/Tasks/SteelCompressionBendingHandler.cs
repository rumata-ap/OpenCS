using System;
using System.Collections.Generic;
using CScore;

namespace OpenCS.Tasks;

/// <summary>
/// Проверка сжатия с изгибом: прочность 8.1.3/8.1.4 + устойчивость 9.2.2.
/// </summary>
public class SteelCompressionBendingHandler : SteelTaskHandlerBase
{
    public override string Kind => "steel_compression_bending";

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

        double chiBX = chiX, chiBY = chiY;
        if (context.DesignLengthBit > 1e-10)
        {
            var lambdaBarBX = SteelStabilityCheck.ConventionalSlenderness(
                context.DesignLengthBit, steelSection.iy, E, fy);
            chiBX = SteelStabilityCheck.ChiB(lambdaBarBX, context.BucklingCurveX);
            chiBY = chiBX;
        }

        var NcrX = SteelStabilityCheck.EulerForce(E * steelSection.Ix, context.DesignLengthX * context.MuX);
        var NcrY = SteelStabilityCheck.EulerForce(E * steelSection.Iy, context.DesignLengthY * context.MuY);

        var details = new List<CheckDetail>();

        // Прочность 8.1.3 / 8.1.4
        if (Math.Abs(forces.Mx) > 1e-10 || Math.Abs(forces.My) > 1e-10)
            details.Add(SteelStrengthCheck.CheckCompressionBending(
                forces.N, forces.Mx, forces.My, chiX, chiBX, chiBY,
                steelSection.Area, steelSection.Wx, steelSection.Wy, fy, gammaM));

        // Устойчивость 9.2.2
        if (Math.Abs(forces.Mx) > 1e-10)
            details.Add(SteelStabilityCheck.CheckBucklingBending(
                forces.N, forces.Mx, chiX, chiBX, steelSection.Area, steelSection.Wx,
                NcrX, lambdaBarX, context.BetaM, fy, gammaM, "X", context.SectionType));
        if (Math.Abs(forces.My) > 1e-10)
            details.Add(SteelStabilityCheck.CheckBucklingBending(
                forces.N, forces.My, chiY, chiBY, steelSection.Area, steelSection.Wy,
                NcrY, lambdaBarY, context.BetaM, fy, gammaM, "Y", context.SectionType));

        // Продольный изгиб 8.7.1
        if (Math.Abs(forces.Mx) > 1e-10)
            details.Add(SteelStrengthCheck.CheckLateralBending(
                forces.N, forces.Mx, chiBX, steelSection.Area, steelSection.Wx, fy, gammaM, "X"));
        if (Math.Abs(forces.My) > 1e-10)
            details.Add(SteelStrengthCheck.CheckLateralBending(
                forces.N, forces.My, chiBY, steelSection.Area, steelSection.Wy, fy, gammaM, "Y"));

        return Ok(task, created, section.Tag, steelSection.Steel.Tag, [.. details], forces, context);
    }
}
