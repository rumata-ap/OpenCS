using CScore.Fem;

namespace CScore.Fem.Combinations;

/// <summary>Результат разрешения выражения: силовые и кинематические нагрузки одного расчёта.</summary>
public sealed record FemResolvedLoads(
    IReadOnlyList<FemNodeLoad> NodeLoads,
    IReadOnlyList<FemMemberLoad> MemberLoads,
    IReadOnlyList<FemKinematicLoad> KinematicLoads);
