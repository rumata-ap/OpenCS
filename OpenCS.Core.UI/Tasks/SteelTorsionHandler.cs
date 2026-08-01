using System;
using System.Collections.Generic;
using CScore;

namespace OpenCS.Tasks;

/// <summary>
/// Проверка кручения: прочность 8.8.1/8.9.1 + устойчивость 9.3.1/9.4.1.
/// </summary>
public class SteelTorsionHandler : SteelTaskHandlerBase
{
    public override string Kind => "steel_torsion";

    protected override CalcResult Execute(
        CalcTask task, string created, CrossSection section,
        SteelSection steelSection, InternalForces forces, DesignContext context)
    {
        var fy = steelSection.Steel.C?.Ry ?? 235e6;
        var gammaM = context.GammaM;
        var details = new List<CheckDetail>();

        if (Math.Abs(forces.Mz) > 1e-10)
        {
            // Прочность 8.8.1
            details.Add(SteelStrengthCheck.CheckTorsion(forces.Mz, steelSection.Wt, fy, gammaM));

            // Изгиб + кручение 8.9.1
            if (Math.Abs(forces.Mx) > 1e-10 || Math.Abs(forces.My) > 1e-10)
            {
                double M = Math.Max(Math.Abs(forces.Mx), Math.Abs(forces.My));
                double W = Math.Abs(forces.Mx) > Math.Abs(forces.My) ? steelSection.Wx : steelSection.Wy;
                details.Add(SteelStrengthCheck.CheckBendingTorsion(M, forces.Mz, W, steelSection.Wt, fy, gammaM));
            }

            // Устойчивость при кручении 9.3.1
            details.Add(SteelStabilityCheck.CheckTorsionBuckling(forces.Mz, 1.0, steelSection.Wt, fy, gammaM));

            // Устойчивость изгиб + кручение 9.4.1
            if (Math.Abs(forces.Mx) > 1e-10 || Math.Abs(forces.My) > 1e-10)
            {
                double M = Math.Max(Math.Abs(forces.Mx), Math.Abs(forces.My));
                double W = Math.Abs(forces.Mx) > Math.Abs(forces.My) ? steelSection.Wx : steelSection.Wy;
                details.Add(SteelStabilityCheck.CheckBucklingBendingTorsion(
                    forces.N, M, forces.Mz, 1.0, 1.0, 1.0,
                    steelSection.Area, W, steelSection.Wt, fy, gammaM));
            }
        }

        return Ok(task, created, section.Tag, steelSection.Steel.Tag, [.. details], forces, context);
    }
}
