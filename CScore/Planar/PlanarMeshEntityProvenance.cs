namespace CScore.Planar;

/// <summary>Связь логического constraint-объекта с entity и physical group из MSH.</summary>
public sealed record PlanarMeshEntityProvenance(
    string LogicalConstraintId,
    int EntityDimension,
    int EntityTag,
    int PhysicalGroup,
    string PhysicalName);
