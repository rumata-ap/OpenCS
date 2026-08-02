namespace OpenCS.OpenSees.Structural;

/// <summary>Стадия пропорционального нагружения shell-модели — аналог FemNonlinearStage, с
/// узловыми силами и предписанными узловыми перемещениями. Распределённые/точечные нагрузки
/// на элементах для существующих BeamElements остаются вне объёма.</summary>
public sealed class ShellNonlinearStage
{
    public string Tag { get; init; } = "";
    public IReadOnlyList<ShellNodalLoad> Loads { get; init; } = [];
    /// <summary>Предписанные перемещения/вращения shell-узлов в DOF 1..6.</summary>
    public IReadOnlyList<ShellKinematicLoad> KinematicLoads { get; init; } = [];
    /// <summary>Шаг коэффициента нагрузки λ этой стадии.</summary>
    public double LoadFactorStep { get; init; } = 0.1;
    /// <summary>Максимальный коэффициент нагрузки λ этой стадии.</summary>
    public double MaxLoadFactor { get; init; } = 1.0;
}
