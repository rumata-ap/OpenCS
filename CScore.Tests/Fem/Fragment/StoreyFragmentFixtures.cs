using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Planar;

namespace CScore.Tests.Fem.Fragment;

static class StoreyFragmentFixtures
{
    public static FemNode N(string tag, double x, double y, double z, int dof = 0) =>
        new() { NodeTag = tag, X = x, Y = y, Z = z, DofMask = dof };

    public static FemMember Bar(string tag, string i, string j) =>
        new() { ElemTag = tag, ElemType = "beam", NodeIdsJson = $"[{i},{j}]", CrossSectionId = 1 };

    public static Contour Box(double x0, double y0, double x1, double y1, string tag) =>
        new(new[] { x0, x1, x1, x0, x0 }, new[] { y0, y0, y1, y1, y0 }, tag);

    /// <summary>Плита 6×6 на отметке <paramref name="z"/> с центром в начале координат.</summary>
    public static (FemMember Member, PlanarRegion? Region) Slab(string tag, int id, double z, params Contour[] holes)
    {
        var frame = new Frame3D(new PlanarVector3(0, 0, z), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
        var region = PlanarRegion.CreateFromContour(Box(-3, -3, 3, 3, tag), holes, frame, tag);
        region.Id = id;
        return (new FemMember { ElemTag = tag, ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = id, Kind = "plate", ThicknessM = 0.2 }, region);
    }

    /// <summary>Стена в плоскости y = <paramref name="y0"/>: u — вдоль X от <paramref name="x0"/>, v — вверх от
    /// <paramref name="z0"/>; контур [0, L]×[0, H] в осях стены; отверстия — в тех же осях.</summary>
    public static (FemMember Member, PlanarRegion Region) Wall(
        string tag, int id, double x0, double y0, double z0, double length, double height, params Contour[] holes)
    {
        var frame = new Frame3D(new PlanarVector3(x0, y0, z0), new(1, 0, 0), new(0, 0, 1), new(0, -1, 0));
        var region = PlanarRegion.CreateFromContour(Box(0, 0, length, height, tag), holes, frame, tag);
        region.Id = id;
        return (new FemMember { ElemTag = tag, ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = id, Kind = "wall", ThicknessM = 0.2 }, region);
    }

    /// <summary>Заглушка сетки родителя: один КЭ, покрытый КонЭ <paramref name="memberTag"/>, без узлов.</summary>
    public static FemElement CoveredElement(string memberTag) =>
        new() { ElemTag = "9999", ElemType = "shell", NodeIdsJson = "[]", SourceMemberTag = memberTag };

    public static double Area(PlanarRegion region) =>
        Math.Abs(PlanarPolygonOps.SignedArea(Points(region.RequireHull())))
        - region.Holes.Sum(h => Math.Abs(PlanarPolygonOps.SignedArea(Points(h))));

    static List<PlanarPoint2D> Points(Contour c) =>
        Enumerable.Range(0, c.X.Count - 1).Select(i => new PlanarPoint2D(c.X[i], c.Y[i])).ToList();

    public static StoreyFragmentDraft Build(
        double z1, double z2, FemNode[] nodes, (FemMember Member, PlanarRegion? Region)[] members,
        FemMeshNode[]? meshNodes = null, FemElement[]? meshElements = null, FemRigidBody[]? bodies = null) =>
        StoreyFragmentBuilder.Build(new StoreyFragmentParentSnapshot
        {
            Nodes = nodes,
            Members = members.Select(m => m.Member).ToList(),
            Regions = members.Where(m => m.Region is not null).Select(m => m.Region!).ToList(),
            MeshNodes = meshNodes ?? [],
            MeshElements = meshElements ?? [CoveredElement("S1")],
            RigidBodies = bodies ?? [],
        }, new StoreyFragmentRequest(z1, z2, "Этаж 2"));
}
