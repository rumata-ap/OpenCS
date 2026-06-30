using System.Collections.Generic;
using CScore;

namespace OpenCS.Tasks;

/// <summary>
/// Проверка изгиба: прочность 8.1.2 + продольный изгиб 8.7.1.
/// </summary>
public class SteelBendingHandler : SteelTaskHandlerBase
{
    public override string Kind => "steel_bending";

    protected override CalcResult Execute(
        CalcTask task, string created, CrossSection section,
        SteelSection steelSection, InternalForces forces, DesignContext context)
    {
        var fy = steelSection.Steel.C?.Ry ?? 235e6;
        var gammaM = context.GammaM;
        var E = steelSection.Steel.E;

        // χb для бокового выпучивания
        double chiBX = 1.0, chiBY = 1.0;
        if (context.DesignLengthBit > 1e-10)
        {
            var lambdaBarBX = SteelStabilityCheck.ConventionalSlenderness(
                context.DesignLengthBit, steelSection.iy, E, fy);
            chiBX = SteelStabilityCheck.ChiB(lambdaBarBX, context.BucklingCurveX);
            chiBY = chiBX;
        }

        var details = new List<CheckDetail>();
        if (Math.Abs(forces.Mx) > 1e-10)
            details.Add(SteelStrengthCheck.CheckBending(forces.Mx, chiBX, steelSection.Wx, fy, gammaM, "X"));
        if (Math.Abs(forces.My) > 1e-10)
            details.Add(SteelStrengthCheck.CheckBending(forces.My, chiBY, steelSection.Wy, fy, gammaM, "Y"));

        // Продольный изгиб (8.7.1)
        if (Math.Abs(forces.N) > 1e-10)
        {
            if (Math.Abs(forces.Mx) > 1e-10)
                details.Add(SteelStrengthCheck.CheckLateralBending(
                    forces.N, forces.Mx, chiBX, steelSection.Area, steelSection.Wx, fy, gammaM, "X"));
            if (Math.Abs(forces.My) > 1e-10)
                details.Add(SteelStrengthCheck.CheckLateralBending(
                    forces.N, forces.My, chiBY, steelSection.Area, steelSection.Wy, fy, gammaM, "Y"));
        }

        return Ok(task, created, section.Tag, steelSection.Steel.Tag, [.. details], forces, context);
    }
}
