namespace OpenCS.Tasks;

/// <summary>Обработчик задачи steel_check через единый диспетчер СП 16.</summary>
public sealed class SteelCheckHandler : SteelTaskHandlerBase
{
    public override string Kind => "steel_check";
}
