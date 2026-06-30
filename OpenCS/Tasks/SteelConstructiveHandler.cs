using System.Collections.Generic;
using CScore;

namespace OpenCS.Tasks;

/// <summary>
/// Конструктивные проверки: раздел 10 СП 16 (минимальная толщина, максимальная гибкость).
/// </summary>
public class SteelConstructiveHandler : SteelTaskHandlerBase
{
    public override string Kind => "steel_constructive";

    protected override CalcResult Execute(
        CalcTask task, string created, CrossSection section,
        SteelSection steelSection, InternalForces forces, DesignContext context)
    {
        var details = new List<CheckDetail>();
        details.AddRange(SteelConstructiveCheck.CheckAll(steelSection, context));
        return Ok(task, created, section.Tag, steelSection.Steel.Tag, [.. details], forces, context);
    }
}
