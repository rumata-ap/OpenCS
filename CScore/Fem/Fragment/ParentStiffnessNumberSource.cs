namespace CScore.Fem.Fragment;

/// <summary>
/// Жёсткости КЭ фрагмента по программе родителя (SCAD/ЛИРА): у КЭ фрагмента номера жёсткости нет, поэтому он берётся
/// из опорного КЭ родителя (<see cref="FragmentParentMap"/>), а свойства отдаёт вложенный источник — с таблицей
/// жёсткостей фрагмента (копия родительской). КЭ без пары или с уже заданным номером — без изменений.
/// </summary>
public sealed class ParentStiffnessNumberSource(IFemElementStiffnessSource inner, FragmentParentMap map) : IFemElementStiffnessSource
{
    /// <inheritdoc/>
    public FemShellStiffness? Shell(FemElement element) => inner.Shell(Mapped(element));

    /// <inheritdoc/>
    public FemBarStiffness? Bar(FemElement element) => inner.Bar(Mapped(element));

    /// <inheritdoc/>
    public double? UnitWeight(FemElement element) => inner.UnitWeight(Mapped(element));

    /// <inheritdoc/>
    public double? BarArea(FemElement element) => inner.BarArea(Mapped(element));

    /// <inheritdoc/>
    public bool IsWithoutStiffness(FemElement element) => inner.IsWithoutStiffness(Mapped(element));

    /// <summary>КЭ с номером жёсткости опорного КЭ родителя (копия; исходный объект не меняется).</summary>
    FemElement Mapped(FemElement element)
    {
        if (element.StiffnessNum is not null || !map.ByTag.TryGetValue(element.ElemTag, out var reference)
            || reference.StiffnessNum is not int number) return element;
        var copy = element.ShallowCopy();
        copy.StiffnessNum = number;
        return copy;
    }
}
