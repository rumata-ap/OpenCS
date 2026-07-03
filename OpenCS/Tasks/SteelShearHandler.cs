using System;
using System.Collections.Generic;
using CScore;

namespace OpenCS.Tasks;

/// <summary>
/// Проверка среза: прочность 8.5/8.6.
/// </summary>
public class SteelShearHandler : SteelTaskHandlerBase
{
    public override string Kind => "steel_shear";

    protected override CalcResult Execute(
        CalcTask task, string created, CrossSection section,
        SteelSection steelSection, InternalForces forces, DesignContext context)
    {
        var fy = steelSection.Steel.C?.Ry ?? 235e6;
        var gammaM = context.GammaM;
        var details = new List<CheckDetail>();

        double Q = Math.Max(Math.Abs(forces.Qy), Math.Abs(forces.Qz));
        if (Q > 1e-10)
        {
            double Aw = SteelStrengthCheck.EstimateWebArea(steelSection);
            details.Add(SteelStrengthCheck.CheckShear(Q, Aw, fy, gammaM));

            // Срез с учётом выпучивания (8.5)
            double yMin = double.MaxValue, yMax = double.MinValue;
            foreach (var (px, py) in steelSection.OuterContour)
            {
                if (py < yMin) yMin = py;
                if (py > yMax) yMax = py;
            }
            double h = yMax - yMin;
            if (h > 1e-6)
            {
                double tw = Aw / h;
                details.Add(SteelStrengthCheck.CheckShearBuckling(Q, Aw, h, tw, steelSection.Steel.E, fy, gammaM));
            }
        }

        return Ok(task, created, section.Tag, steelSection.Steel.Tag, [.. details], forces, context);
    }
}
