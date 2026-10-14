namespace OpenCS.Tasks;

/// <summary>Обработчик задачи steel_tension_bending через единый диспетчер СП 16.</summary>
public sealed class SteelTensionBendingHandler : SteelTaskHandlerBase
{
    public override string Kind => "steel_tension_bending";
}
