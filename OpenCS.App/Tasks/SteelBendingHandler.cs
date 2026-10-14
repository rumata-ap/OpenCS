namespace OpenCS.Tasks;

/// <summary>Обработчик задачи steel_bending через единый диспетчер СП 16.</summary>
public sealed class SteelBendingHandler : SteelTaskHandlerBase
{
    public override string Kind => "steel_bending";
}
