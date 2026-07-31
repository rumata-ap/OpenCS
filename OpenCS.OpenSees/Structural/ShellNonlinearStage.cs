namespace OpenCS.OpenSees.Structural;

/// <summary>Стадия пропорционального нагружения shell-модели — аналог FemNonlinearStage, но
/// только с узловыми нагрузками (распределённые/точечные/кинематические нагрузки на элементах
/// не поддерживаются и для существующих BeamElements — вне объёма).</summary>
public sealed class ShellNonlinearStage
{
    public string Tag { get; init; } = "";
    public IReadOnlyList<ShellNodalLoad> Loads { get; init; } = [];
    /// <summary>Шаг коэффициента нагрузки λ этой стадии.</summary>
    public double LoadFactorStep { get; init; } = 0.1;
    /// <summary>Максимальный коэффициент нагрузки λ этой стадии.</summary>
    public double MaxLoadFactor { get; init; } = 1.0;
}
