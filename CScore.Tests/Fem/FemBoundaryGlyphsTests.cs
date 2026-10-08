using CScore.Fem;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Глифы ГУ сеточного уровня для 3D-вида.</summary>
public sealed class FemBoundaryGlyphsTests
{
    static FemMeshNode Mesh(string tag, double x, double y = 0, double z = 0) => new() { NodeTag = tag, X = x, Y = y, Z = z };

    static FemElement Bar(string tag, string i, string j, int? ri = null, int? rj = null) =>
        new() { ElemTag = tag, ElemType = "beam", NodeIdsJson = $"[{i},{j}]", ReleaseI = ri, ReleaseJ = rj };

    static FemRigidBody Body(string master, params string[] slaves)
    {
        var body = new FemRigidBody { MasterNodeTag = master };
        body.SetSlaveNodeTags(slaves);
        return body;
    }

    static FemBoundaryGlyphSet Build(IReadOnlyList<FemMeshNode> mesh, IReadOnlyList<FemElement>? elements = null,
        IReadOnlyList<FemMeshNodeSupport>? supports = null, IReadOnlyList<FemSpring>? springs = null,
        IReadOnlyList<FemRigidBody>? bodies = null, IReadOnlyList<FemNode>? nodes = null, int maxRigidLinks = 5000) =>
        FemBoundaryGlyphs.Build(nodes ?? [], mesh, elements ?? [], supports ?? [], springs ?? [], bodies ?? [], 0.5, maxRigidLinks);

    [Fact]
    public void Supports_MasksOfOneNodeMerge_MissingNodesSkipped()
    {
        var set = Build([Mesh("1", 0), Mesh("2", 1)],
            supports: [new() { NodeTag = "1", Mask = 0b000011 }, new() { NodeTag = "1", Mask = 0b100000 },
                       new() { NodeTag = "9", Mask = 0b000111 }, new() { NodeTag = "2", Mask = 0 }]);

        Assert.Equal(1, set.SupportNodes);
        // Две поступательные связи по 4 отрезка, поворотная — 12 отрезков дуги и 2 стрелки.
        Assert.Equal(2 * 4 + 14, set.Supports.Count);
        // Стойка знака по X начинается в узле и идёт вдоль оси на размер знака.
        Assert.Contains(set.Supports, s => s.A == new CScore.Planar.PlanarVector3(0, 0, 0) && s.B == new CScore.Planar.PlanarVector3(0.5, 0, 0));
    }

    [Fact]
    public void Springs_OnlyNonZeroDirections_BothNodeKinds()
    {
        var mesh = new FemSpring { NodeTag = "1" };
        mesh.SetStiffnesses([0, 0, 1e6, 0, 0, 0]);
        var own = new FemSpring { TargetKind = FemSpringTargetKinds.Node, NodeTag = "A" };
        own.SetStiffnesses([0, 0, 0, 0, 0, 5e4]);
        var missing = new FemSpring { NodeTag = "9" };
        missing.SetStiffnesses([1, 1, 1, 1, 1, 1]);

        var set = Build([Mesh("1", 0)], springs: [mesh, own, missing],
            nodes: [new FemNode { NodeTag = "A", X = 3 }]);

        Assert.Equal(2, set.SpringNodes);
        // Зигзаг вдоль Z лежит в плоскости XZ узла 1 и не длиннее 1,3 размера знака.
        var zigzag = set.Springs.Where(s => s.A.X < 1 && s.B.X < 1).ToList();
        Assert.All(zigzag, s => Assert.InRange(s.B.Z, 0, 0.65 + 1e-12));
        // Спираль UZ — в плоскости XY узла A.
        var spiral = set.Springs.Where(s => s.A.X > 2).ToList();
        Assert.NotEmpty(spiral);
        Assert.All(spiral, s => Assert.Equal(0, s.A.Z, 12));
    }

    [Fact]
    public void Hinges_OneCirclePerReleasedEnd_NearThatEnd()
    {
        var set = Build([Mesh("1", 0), Mesh("2", 4), Mesh("3", 8)],
            elements: [Bar("10", "1", "2", rj: 0b110000), Bar("11", "2", "3"), Bar("12", "2", "3", ri: 0, rj: 0)]);

        Assert.Equal(1, set.HingeEnds);
        Assert.Equal(2 * 16, set.Hinges.Count);
        // Кружок у конца J (x = 4) первого стержня, не дальше 2r от узла.
        Assert.All(set.Hinges, s => Assert.InRange(s.A.X, 4 - 2 * 0.125 - 1e-12, 4 + 1e-12));
    }

    [Fact]
    public void RigidBodies_LinksThinnedWithCommonStep()
    {
        var mesh = Enumerable.Range(0, 21).Select(i => Mesh(i.ToString(), i)).ToList();
        var bodies = new[]
        {
            Body("0", Enumerable.Range(1, 10).Select(i => i.ToString()).ToArray()),
            Body("20", Enumerable.Range(11, 9).Select(i => i.ToString()).Append("99").ToArray()),
        };

        var all = Build(mesh, bodies: bodies);
        Assert.Equal(2, all.RigidBodyCount);
        Assert.Equal(19, all.RigidLinksTotal);
        Assert.Equal(19, all.RigidLinksShown);
        Assert.Equal(19 + 2 * 3, all.RigidBodies.Count);

        var thinned = Build(mesh, bodies: bodies, maxRigidLinks: 5);
        Assert.Equal(19, thinned.RigidLinksTotal);
        Assert.Equal(5, thinned.RigidLinksShown); // шаг 4: связи 0, 4, 8, 12, 16
    }

    [Fact]
    public void GlyphSize_FromMedianElement_Clamped()
    {
        var mesh = new[] { Mesh("1", 0), Mesh("2", 2), Mesh("3", 4), Mesh("4", 104) };
        Assert.Equal(0.7, FemBoundaryGlyphs.GlyphSize(mesh, [Bar("a", "1", "2"), Bar("b", "2", "3"), Bar("c", "3", "4")]), 12);
        Assert.Equal(1.0, FemBoundaryGlyphs.GlyphSize(mesh, [Bar("c", "3", "4")]), 12);
        Assert.Equal(0.28, FemBoundaryGlyphs.GlyphSize(mesh, []), 12);
    }
}
