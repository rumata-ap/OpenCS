using CScore.Fem;

namespace OpenCS.OpenSees.CScore;

/// <summary>Одна стадия нагружения на входе резолвера: имя + разрешённые (уже свёрнутые из
/// FemLoadExpression) нагрузки этой стадии + собственные шаг/предел коэффициента нагрузки λ.</summary>
public sealed record FemNonlinearStageInput(
    string Tag,
    IReadOnlyList<FemNodeLoad> Loads,
    double LoadFactorStep = 0.1,
    double MaxLoadFactor = 10.0)
{
    public IReadOnlyList<FemMemberLoad> MemberLoads { get; init; } = [];
    public IReadOnlyList<FemKinematicLoad> KinematicLoads { get; init; } = [];
    /// <summary>Способ управления траекторией этой стадии; null → LoadControl без
    /// continuation (совпадает с default FemPathControlInput).</summary>
    public FemPathControlInput? PathControl { get; init; }
}
