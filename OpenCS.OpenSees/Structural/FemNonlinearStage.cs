namespace OpenCS.OpenSees.Structural;

/// <summary>Одна стадия нагружения нелинейного расчёта схемы: добавочный набор нагрузок,
/// действующий начиная с этой стадии и до конца расчёта (предыдущие стадии зафиксированы
/// через loadConst — см. FemNonlinearTclGenerator).</summary>
public sealed class FemNonlinearStage
{
    public string Tag { get; init; } = "";
    public IReadOnlyList<FemLinearNodalLoad> Loads { get; init; } = [];
    public IReadOnlyList<FemLinearDistributedLoad> DistributedLoads { get; init; } = [];
    public IReadOnlyList<FemLinearPointLoad> PointLoads { get; init; } = [];
    public IReadOnlyList<FemLinearKinematicLoad> KinematicLoads { get; init; } = [];
}
