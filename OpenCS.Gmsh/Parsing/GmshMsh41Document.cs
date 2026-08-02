using CScore.Fem;

namespace OpenCS.Gmsh.Parsing;

/// <summary>Узел с исходным tag из MSH 4.1 до уплотнения в dense index.</summary>
public sealed record GmshMsh41Node(long RawId, double X, double Y, double Z);

/// <summary>Элемент MSH 4.1 с entity и physical provenance.</summary>
public sealed record GmshMsh41Element(
    long RawId,
    int EntityDimension,
    long EntityTag,
    int ElementType,
    IReadOnlyList<long> RawNodeIds,
    int PhysicalGroup,
    string PhysicalName);

/// <summary>Прочитанные sections MSH 4.1 и ошибки, не позволяющие использовать mesh.</summary>
public sealed class GmshMsh41Document
{
    public IReadOnlyList<GmshMsh41Node> Nodes { get; init; } = [];
    public IReadOnlyList<GmshMsh41Element> Elements { get; init; } = [];
    public IReadOnlyList<FemValidationDiagnostic> Diagnostics { get; init; } = [];
    public bool IsCalculable => !Diagnostics.Any(diagnostic => diagnostic.IsError);
}
