using System.Collections.Generic;
using CScore;

namespace OpenCS.Tasks;

/// <summary>
/// Проверка центрального растяжения: прочность 8.1.1.
/// </summary>
public class SteelCentralTensionHandler : SteelTaskHandlerBase
{
    public override string Kind => "steel_central_tension";

    protected override CalcResult Execute(
        CalcTask task, string created, CrossSection section,
        SteelSection steelSection, InternalForces forces, DesignContext context)
    {
        var fy = steelSection.Steel.C?.Ry ?? 235e6;
        var details = new List<CheckDetail>();
        details.Add(SteelStrengthCheck.CheckAxialTension(
            forces.N, steelSection.Area, fy, context.GammaM));
        return Ok(task, created, section.Tag, steelSection.Steel.Tag, [.. details], forces, context);
    }
}
