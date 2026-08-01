using CSfea.Torsion;

namespace OpenCS.Tasks;

/// <summary>Обработчик задачи кручения методом конечных элементов.</summary>
public sealed class TorsionFemHandler : TorsionHandlerBase
{
    public override string Kind => "torsion_fem";
    protected override TorsionMethod Method => TorsionMethod.Fem;
}
