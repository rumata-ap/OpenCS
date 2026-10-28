using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using Xunit;
using static CScore.Tests.Fem.Fragment.Masks.FragmentMaskFixtures;

namespace CScore.Tests.Fem.Fragment.Masks;

public sealed class FragmentCutMeshNodesTests
{
    const double Tol = 1e-3;

    static FemMeshNode N(string tag, double x, double y, double z, string? source = null) =>
        new() { NodeTag = tag, X = x, Y = y, Z = z, SourceNodeTag = source };

    static FragmentMaskCutInput Edge(params (double X0, double X1)[] spans) => new(
        new FragmentCut("W1", FragmentCutKind.Edge, FragmentCutSide.Bottom, 1.5, null,
            spans.Select(s => (V(s.X0, 0, 1.5), V(s.X1, 0, 1.5))).ToList()), null, V(0, 1, 0));

    /// <summary>
    /// Кромка 0…4 с проёмом 1,5…2,5: отрезки 0…1,5 и 2,5…4; узлы через 0,5 плюс узлы выше кромки и в проёме (не
    /// попадают). s второго отрезка начинается с длины первого (1,5), а не с координаты x.
    /// </summary>
    [Fact]
    public void Edge_WithOpening_NodesOrderedByMaskCoordinate()
    {
        var nodes = new List<FemMeshNode>();
        int k = 0;
        foreach (double x in new[] { 0, 0.5, 1, 1.5, 2.5, 3, 3.5, 4 }.Reverse())
            nodes.Add(N($"{++k}", x, 0, 1.5));
        nodes.Add(N("up", 1, 0, 2));
        nodes.Add(N("gap", 2, 0.0, 1.5 + 0.5));
        nodes.Add(N("off", 1, 0.01, 1.5));

        var r = FragmentCutMeshNodes.Find(Edge((0, 1.5), (2.5, 4)), nodes, Tol);

        Assert.Null(r.Error);
        Assert.Equal(2, r.Segments.Count);
        Assert.Equal([0, 0.5, 1, 1.5], r.Segments[0].Select(n => n.S), new Precise());
        Assert.Equal([1.5, 2.0, 2.5, 3.0], r.Segments[1].Select(n => n.S), new Precise());
        Assert.Equal(["5", "6", "7", "8"], r.Segments[0].Select(n => n.Tag).Reverse());
        Assert.Equal(8, r.Tags.Count());
    }

    /// <summary>Два отрезка с общим концом x = 2 (излом стены в плане): узел стыка — в обоих, s одна.</summary>
    [Fact]
    public void Edge_JunctionNode_InBothSegments()
    {
        var cut = new FragmentMaskCutInput(new FragmentCut("W1", FragmentCutKind.Edge, FragmentCutSide.Top, 4.5, null,
            [(V(0, 0, 4.5), V(2, 0, 4.5)), (V(2, 0, 4.5), V(2, 3, 4.5))]), null, null);
        List<FemMeshNode> nodes = [N("a", 0, 0, 4.5), N("b", 1, 0, 4.5), N("j", 2, 0, 4.5), N("c", 2, 1.5, 4.5), N("d", 2, 3, 4.5)];

        var r = FragmentCutMeshNodes.Find(cut, nodes, Tol);

        Assert.Null(r.Error);
        Assert.Equal(("j", 2.0), (r.Segments[0][^1].Tag, r.Segments[0][^1].S));
        Assert.Equal(("j", 2.0), (r.Segments[1][0].Tag, r.Segments[1][0].S));
        Assert.Equal(5.0, r.Segments[1][^1].S, 12);
        Assert.Equal(5, r.Tags.Count());
    }

    [Fact]
    public void Edge_NoNodeAtSegmentEnd_IsError()
    {
        List<FemMeshNode> nodes = [N("a", 0, 0, 1.5), N("b", 1, 0, 1.5), N("c", 1.9, 0, 1.5)];
        var r = FragmentCutMeshNodes.Find(Edge((0, 2)), nodes, Tol);
        Assert.NotNull(r.Error);
        Assert.Empty(r.Tags);
    }

    /// <summary>Конец стержня: по узлу схемы, иначе по координате; общий узел стена–колонна находится обоими разрезами.</summary>
    [Fact]
    public void End_BySourceTagThenByPoint()
    {
        List<FemMeshNode> nodes = [N("10", 0, 0, 1.5), N("11", 1, 0, 1.5), N("12", 4, 0, 1.5, source: "C1")];
        var byTag = new FragmentMaskCutInput(new FragmentCut("C1", FragmentCutKind.End, FragmentCutSide.Bottom, 1.5, "C1", []), V(9, 9, 9), null);
        var byPoint = new FragmentMaskCutInput(new FragmentCut("C2", FragmentCutKind.End, FragmentCutSide.Bottom, 1.5, "C2", []), V(0, 0, 1.5005), null);
        var none = new FragmentMaskCutInput(new FragmentCut("C3", FragmentCutKind.End, FragmentCutSide.Bottom, 1.5, "C3", []), V(2, 0, 1.5), null);

        Assert.Equal("12", FragmentCutMeshNodes.Find(byTag, nodes, Tol).EndNode);
        Assert.Equal("10", FragmentCutMeshNodes.Find(byPoint, nodes, Tol).EndNode);
        Assert.NotNull(FragmentCutMeshNodes.Find(none, nodes, Tol).Error);
        Assert.Contains("12", FragmentCutMeshNodes.Find(Edge((0, 4)), nodes, Tol).Tags);
    }

    sealed class Precise : IEqualityComparer<double>
    {
        public bool Equals(double a, double b) => Math.Abs(a - b) < 1e-12;
        public int GetHashCode(double v) => 0;
    }
}
