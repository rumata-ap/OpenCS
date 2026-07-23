using CScore.Fem;

namespace CScore.Fem.Combinations;

/// <summary>Результат разрешения выражения: узловые и распределённые нагрузки одного расчёта.</summary>
public sealed record FemResolvedLoads(
    IReadOnlyList<FemNodeLoad> NodeLoads,
    IReadOnlyList<FemMemberLoad> MemberLoads);
