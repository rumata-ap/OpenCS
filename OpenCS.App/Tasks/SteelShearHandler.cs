namespace OpenCS.Tasks;

/// <summary>Обработчик задачи steel_shear через единый диспетчер СП 16.</summary>
public sealed class SteelShearHandler : SteelTaskHandlerBase
{
    public override string Kind => "steel_shear";
}
