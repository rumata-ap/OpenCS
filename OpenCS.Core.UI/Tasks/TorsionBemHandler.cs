using CSfea.Torsion;

namespace OpenCS.Tasks;

/// <summary>Обработчик задачи кручения методом граничных элементов.</summary>
public sealed class TorsionBemHandler : TorsionHandlerBase
{
    public override string Kind => "torsion_bem";
    protected override TorsionMethod Method => TorsionMethod.Bem;
}
