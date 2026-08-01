using System.Collections.Generic;
using CScore;

namespace OpenCS.Tasks;

/// <summary>
/// Проверка растяжения с изгибом: прочность 8.1.5.
/// </summary>
public class SteelTensionBendingHandler : SteelTaskHandlerBase
{
    public override string Kind => "steel_tension_bending";

    protected override CalcResult Execute(
        CalcTask task, string created, CrossSection section,
        SteelSection steelSection, InternalForces forces, DesignContext context)
    {
        var fy = steelSection.Steel.C?.Ry ?? 235e6;
        var details = new List<CheckDetail>();
        details.Add(SteelStrengthCheck.CheckTensionBending(
            forces.N, forces.Mx, forces.My,
            steelSection.Area, steelSection.Wx, steelSection.Wy, fy, context.GammaM));
        return Ok(task, created, section.Tag, steelSection.Steel.Tag, [.. details], forces, context);
    }
}
