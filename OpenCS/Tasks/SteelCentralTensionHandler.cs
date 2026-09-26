namespace OpenCS.Tasks;

/// <summary>Обработчик задачи steel_central_tension через единый диспетчер СП 16.</summary>
public sealed class SteelCentralTensionHandler : SteelTaskHandlerBase
{
    public override string Kind => "steel_central_tension";
}
