using System.Text.Json;
using CScore.Fem;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Импортная (сохранённая) сетка в базе префикса волн: новые области встраивают её узлы на стыках.</summary>
public class FemPlanarMeshPlanImportedPrefixTests
{
    const double H = 3.0;

    /// <summary>Импортная плита 4×4 м (Q4 1 м) на отметке H, КонЭ «P1» с замком; КонЭ-стена «W1» по y = 0 под ней.</summary>
    static (List<FemNode> Nodes, List<FemMember> Members, List<FemMeshNode> MeshNodes, List<FemElement> MeshElements)
        ImportedSlabWithWallMember()
    {
        var meshNodes = new List<FemMeshNode>();
        var meshElements = new List<FemElement>();
        int Tag(int i, int j) => 1001 + j * 5 + i;
        for (int j = 0; j <= 4; j++)
            for (int i = 0; i <= 4; i++)
                meshNodes.Add(new FemMeshNode
                {
                    SchemaId = 1, NodeTag = Tag(i, j).ToString(), X = i, Y = j, Z = H,
                    Origin = FemMember.MeshSourceImported, SourceMemberTag = "P1",
                });
        int e = 2001;
        for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++)
                meshElements.Add(new FemElement
                {
                    SchemaId = 1, ElemTag = (e++).ToString(), ElemType = "shell", SourceMemberTag = "P1",
                    Origin = FemMember.MeshSourceImported, ThicknessM = 0.2,
                    // «1 2 4 3», как импорт ЛИРЫ/SCAD.
                    NodeIdsJson = JsonSerializer.Serialize(new[] { Tag(i, j), Tag(i + 1, j), Tag(i, j + 1), Tag(i + 1, j + 1) }),
                });
        var members = new List<FemMember>
        {
            new() { Id = 1, SchemaId = 1, ElemTag = "P1", ElemType = "shell", NodeIdsJson = "[]", MeshSource = FemMember.MeshSourceImported },
            new() { Id = 2, SchemaId = 1, ElemTag = "W1", ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = 2 },
        };
        return ([], members, meshNodes, meshElements);
    }

    /// <summary>Стена x 0…4, z 0…H в плоскости y = 0 (оси: u = x, v = z).</summary>
    static PlanarRegion Wall()
    {
        var wall = PlanarRegion.CreateFromContour(new Contour { X = [0, 4, 4, 0], Y = [0, 0, H, H] },
            frame: new Frame3D(new PlanarVector3(0, 0, 0), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 0, 1), new PlanarVector3(0, -1, 0)));
        wall.Id = 2;
        return wall;
    }

    /// <summary>Все точки выведенных ограничений (точки и вершины кривых) в осях области.</summary>
    static List<PlanarPoint2D> DerivedPoints(FemPlanarRegionConstraints constraints) =>
        constraints.Constraints.Where(c => c.IsDerived).SelectMany(c => c.Geometry.Points).ToList();

    static bool HasPoint(List<PlanarPoint2D> points, double u, double v) =>
        points.Any(p => Math.Abs(p.U - u) < 1e-6 && Math.Abs(p.V - v) < 1e-6);

    /// <summary>Старая база префикса (только стержни КонЭ) импортную плиту не видит — точек на кромке нет.</summary>
    [Fact]
    public void BeamsOnlyPrefix_WallDoesNotSeeImportedSlab()
    {
        var (nodes, members, _, _) = ImportedSlabWithWallMember();
        var (baseNodes, baseElements) = FemPlanarMeshPlan.DerivationBeams(1, nodes, members);
        var prefix = new FemPlanarMeshMaterializer.Prefix(1, baseNodes, baseElements, nodes).Build();

        var points = DerivedPoints(FemPlanarMeshPlan.Constraints(1, nodes, members, prefix, Wall()));

        Assert.False(HasPoint(points, 2, H));
    }

    [Fact]
    public void ImportedSlab_WallDerivesPointsAtSlabEdgeNodes()
    {
        var (nodes, members, meshNodes, meshElements) = ImportedSlabWithWallMember();
        var (baseNodes, baseElements) = FemPlanarMeshPlan.DerivationBeams(1, nodes, members, meshNodes, meshElements);
        var prefix = new FemPlanarMeshMaterializer.Prefix(1, baseNodes, baseElements, nodes).Build();

        var constraints = FemPlanarMeshPlan.Constraints(1, nodes, members, prefix, Wall());

        var points = DerivedPoints(constraints);
        for (int x = 0; x <= 4; x++)
            Assert.True(HasPoint(points, x, H), $"нет точки u = {x}, v = {H}");
        Assert.DoesNotContain(constraints.Diagnostics, d => d.IsError);
        // Кривая по сохранённой сетке — с сохранением вершин: Gmsh не добавит узлов между узлами плиты.
        var seam = Assert.Single(constraints.Constraints, c => c.IsDerived && c.Geometry.Kind == PlanarConstraintGeometryKind.Curve);
        Assert.True(seam.KeepVertices);
    }

    /// <summary>Без импортных строк — то же, что старая перегрузка.</summary>
    [Fact]
    public void NoImportedRows_SameAsBeamsOnly()
    {
        var nodes = new List<FemNode>
        {
            new() { SchemaId = 1, NodeTag = "1", X = 0, Y = 0, Z = 0 }, new() { SchemaId = 1, NodeTag = "2", X = 0, Y = 0, Z = H },
        };
        var members = new List<FemMember> { new() { SchemaId = 1, ElemTag = "C1", ElemType = "beam", NodeIdsJson = "[1,2]" } };

        var plain = FemPlanarMeshPlan.DerivationBeams(1, nodes, members);
        var withEmpty = FemPlanarMeshPlan.DerivationBeams(1, nodes, members, [], []);

        Assert.Equal(plain.Nodes.Select(n => (n.NodeTag, n.X, n.Y, n.Z)), withEmpty.Nodes.Select(n => (n.NodeTag, n.X, n.Y, n.Z)));
        Assert.Equal(plain.Elements.Select(e => (e.ElemTag, e.NodeIdsJson)), withEmpty.Elements.Select(e => (e.ElemTag, e.NodeIdsJson)));
    }
}
