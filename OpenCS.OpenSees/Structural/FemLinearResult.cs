namespace OpenCS.OpenSees.Structural;

/// <summary>Перемещения узла (м, рад).</summary>
public sealed record FemNodeDisplacement(int NodeTag, double Ux, double Uy, double Uz, double Rx, double Ry, double Rz);

/// <summary>Реакции в закреплённом узле (Н, Н·м).</summary>
public sealed record FemNodeReaction(int NodeTag, double Rx, double Ry, double Rz, double Mx, double My, double Mz);

/// <summary>Концевые усилия стержня в локальной системе (Н, Н·м): концы i и j.</summary>
public sealed record FemElementEndForces(
    int ElemTag,
    double Ni, double Qyi, double Qzi, double Mxi, double Myi, double Mzi,
    double Nj, double Qyj, double Qzj, double Mxj, double Myj, double Mzj);

/// <summary>Типизированный результат линейного расчёта FEM-схемы.</summary>
public sealed class FemLinearResult
{
    public string Status { get; init; } = "created";
    public IReadOnlyList<FemNodeDisplacement> Displacements { get; init; } = [];
    public IReadOnlyList<FemNodeReaction> Reactions { get; init; } = [];
    public IReadOnlyList<FemElementEndForces> ElementForces { get; init; } = [];
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
    public string? ArtifactDirectory { get; init; }
}
