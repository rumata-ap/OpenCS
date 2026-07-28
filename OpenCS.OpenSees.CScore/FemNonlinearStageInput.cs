using CScore.Fem;

namespace OpenCS.OpenSees.CScore;

/// <summary>Одна стадия нагружения на входе резолвера: имя + разрешённые (уже свёрнутые из
/// FemLoadExpression) нагрузки этой стадии.</summary>
public sealed record FemNonlinearStageInput(
    string Tag,
    IReadOnlyList<FemNodeLoad> Loads)
{
    public IReadOnlyList<FemMemberLoad> MemberLoads { get; init; } = [];
    public IReadOnlyList<FemKinematicLoad> KinematicLoads { get; init; } = [];
}
