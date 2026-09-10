using CScore.Planar;

namespace CScore.Submodel;

public enum EnvironmentElementKind
{
    Beam,
    Shell
}

/// <summary>Объект родительской схемы, не входящий в выбранный набор.</summary>
public sealed record EnvironmentElement(
    string SourceKey,
    EnvironmentElementKind Kind,
    IReadOnlyList<PlanarVector3> NodePoints);
