namespace CScore.Planar;

/// <summary>Структурная семантика внутреннего constraint-объекта. В текущем срезе она
/// описывается и проверяется, но не преобразуется в solver equations.</summary>
public enum PlanarStructuralKind
{
    None,
    RigidBody,
    Tie,
    EmbeddedMember,
    PointMpc,
    Support,
    Symmetry
}

/// <summary>Mesh-политика отображения локуса в производную сетку Gmsh.</summary>
public enum PlanarMeshKind
{
    None,
    EmbeddedPoint,
    EmbeddedCurve,
    EmbeddedRegion,
    ConformingPartition
}

/// <summary>Маска шести оболочечных степеней свободы для будущего structural adapter-а.</summary>
[Flags]
public enum PlanarDofMask
{
    None = 0,
    UX = 1,
    UY = 2,
    UZ = 4,
    RX = 8,
    RY = 16,
    RZ = 32
}

/// <summary>Ссылка на внешний master/reference объект без зависимости от конкретного solver-а.</summary>
public sealed record PlanarMasterReference(string Provider, string Key);

/// <summary>Независимая structural facet constraint-объекта.</summary>
public sealed record PlanarStructuralFacet(
    PlanarStructuralKind Kind,
    PlanarMasterReference? MasterReference = null,
    PlanarDofMask DofMask = PlanarDofMask.None,
    Frame3D? Frame = null);

/// <summary>Независимая mesh facet constraint-объекта.</summary>
public sealed record PlanarMeshFacet(PlanarMeshKind Kind);

/// <summary>Источник геометрии constraint-а в FEM topology.</summary>
public sealed record PlanarSourceReference(
    int MemberId,
    string MemberTag,
    IReadOnlyList<int> ElementIds,
    IReadOnlyList<string> ElementTags,
    IReadOnlyList<int> NodeIds,
    IReadOnlyList<string> NodeTags);

/// <summary>Structural relation constraint-а с исходным FEM-member.</summary>
public sealed record PlanarStructuralRelation(
    int SourceMemberId,
    string SourceMemberTag,
    IReadOnlyList<int> SourceElementIds,
    IReadOnlyList<string> SourceElementTags,
    PlanarMasterReference? MasterReference,
    PlanarStructuralKind Kind,
    PlanarDofMask DofMask);

/// <summary>Внутренняя точка, линия или область PlanarRegion, которая может быть включена в
/// топологию производной Gmsh-сетки и позднее получить structural interpretation.</summary>
public sealed class PlanarConstraintObject
{
    public string Id { get; set; } = "";
    public string Tag { get; set; } = "";
    public PlanarConstraintGeometry Geometry { get; set; } =
        new(PlanarConstraintGeometryKind.Point, []);
    public PlanarStructuralFacet StructuralFacet { get; set; } =
        new(PlanarStructuralKind.None);
    public PlanarMeshFacet MeshFacet { get; set; } = new(PlanarMeshKind.None);
    public PlanarMasterReference? MasterReference { get; set; }
    public PlanarDofMask DofMask { get; set; }
    public Frame3D? StructuralFrame { get; set; }
    public double ToleranceM { get; set; } = 1e-9;
    public string? Provenance { get; set; }
    /// <summary>Признак объекта, полученного автоматически из FEM topology.</summary>
    public bool IsDerived { get; set; }
    /// <summary>Все исходные FEM-объекты, вошедшие в общий geometry locus.</summary>
    public List<PlanarSourceReference> SourceReferences { get; set; } = [];
    /// <summary>Все structural relations общего geometry locus.</summary>
    public List<PlanarStructuralRelation> StructuralRelations { get; set; } = [];

    public static PlanarConstraintObject Point(
        string id,
        PlanarPoint2D point,
        PlanarStructuralFacet structuralFacet,
        PlanarMeshFacet meshFacet,
        string tag = "") => Create(id, tag, PlanarConstraintGeometryKind.Point, [point], structuralFacet, meshFacet);

    public static PlanarConstraintObject Curve(
        string id,
        IEnumerable<PlanarPoint2D> points,
        PlanarStructuralFacet structuralFacet,
        PlanarMeshFacet meshFacet,
        string tag = "") => Create(id, tag, PlanarConstraintGeometryKind.Curve, points, structuralFacet, meshFacet);

    public static PlanarConstraintObject Region(
        string id,
        IEnumerable<PlanarPoint2D> points,
        PlanarStructuralFacet structuralFacet,
        PlanarMeshFacet meshFacet,
        string tag = "") => Create(id, tag, PlanarConstraintGeometryKind.Region, points, structuralFacet, meshFacet);

    static PlanarConstraintObject Create(
        string id,
        string tag,
        PlanarConstraintGeometryKind kind,
        IEnumerable<PlanarPoint2D> points,
        PlanarStructuralFacet structuralFacet,
        PlanarMeshFacet meshFacet) => new()
        {
            Id = id,
            Tag = tag,
            Geometry = new(kind, points.ToArray()),
            StructuralFacet = structuralFacet,
            MeshFacet = meshFacet
        };
}
