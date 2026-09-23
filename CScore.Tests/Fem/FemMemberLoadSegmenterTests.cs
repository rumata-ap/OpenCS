using System.Text.Json;
using CScore.Fem;
using Xunit;

namespace CScore.Tests.Fem;

public sealed class FemMemberLoadSegmenterTests
{
    /// <summary>Стержень 10 длиной 10 м вдоль X, 5 mesh-элементов по 2 м (узлы 1..6).</summary>
    static (List<FemMeshNode> MeshNodes, List<FemElement> Elements, List<FemNode> Nodes, List<FemMember> Members) Fixture()
    {
        var meshNodes = new List<FemMeshNode>();
        for (int i = 0; i <= 5; i++)
            meshNodes.Add(new FemMeshNode { NodeTag = (i + 1).ToString(), X = 2 * i, SourceMemberTag = "10" });
        var elements = new List<FemElement>();
        for (int i = 1; i <= 5; i++)
            elements.Add(new FemElement
            {
                ElemTag = i.ToString(), NodeIdsJson = JsonSerializer.Serialize(new[] { i, i + 1 }),
                SourceMemberTag = "10"
            });
        return (meshNodes, elements,
            [new FemNode { Id = 1, NodeTag = "1", X = 0 }, new FemNode { Id = 2, NodeTag = "2", X = 10 }],
            [new FemMember { Id = 10, ElemTag = "10", NodeIdsJson = "[1,2]" }]);
    }

    static MemberLoadSegmentation Segment(FemMemberLoad load, Action<List<FemElement>>? edit = null)
    {
        var f = Fixture();
        edit?.Invoke(f.Elements);
        return FemMemberLoadSegmenter.Segment(f.MeshNodes, f.Elements, f.Nodes, f.Members, [load]);
    }

    [Fact]
    public void Uniform_SplitsOverAllElements_KeepsSourceSystemValues()
    {
        var result = Segment(new FemMemberLoad
        {
            Id = 7, MemberId = 10, DistributionType = "uniform", CoordinateSystem = "local", QzStart = -5
        });

        Assert.Empty(result.Errors);
        Assert.Equal(5, result.Distributed.Count);
        Assert.All(result.Distributed, p =>
        {
            Assert.Equal((0.0, 1.0), (p.AOverL, p.BOverL));
            Assert.Equal(-5, p.QAtA.Z);
            Assert.Equal(-5, p.QAtB.Z);
            Assert.Equal("local", p.CoordinateSystem);
            Assert.Equal(7, p.LoadId);
            Assert.Equal("10", p.MemberTag);
        });
    }

    [Fact]
    public void Trapezoid_WithOffsets_InterpolatesAtPieceBoundaries()
    {
        var result = Segment(new FemMemberLoad
        {
            MemberId = 10, StartOffsetM = 3, EndOffsetM = 3, DistributionType = "trapezoidal",
            CoordinateSystem = "global", QyStart = -1000, QyEnd = -3000
        });

        Assert.Empty(result.Errors);
        // Участок 3..7 м: элементы 2 (2..4), 3 (4..6), 4 (6..8).
        Assert.Equal(new[] { "2", "3", "4" }, result.Distributed.Select(p => p.MeshElementTag));
        var first = result.Distributed[0];
        Assert.Equal((0.5, 1.0), (first.AOverL, first.BOverL));
        Assert.Equal(-1000, first.QAtA.Y, 8);
        Assert.Equal(-1500, first.QAtB.Y, 8);
        var last = result.Distributed[2];
        Assert.Equal((0.0, 0.5), (last.AOverL, last.BOverL));
        Assert.Equal(-2500, last.QAtA.Y, 8);
        Assert.Equal(-3000, last.QAtB.Y, 8);
    }

    [Fact]
    public void ReversedMeshElement_SwapsIntensitiesToElementIOrder()
    {
        var result = Segment(new FemMemberLoad
        {
            MemberId = 10, DistributionType = "trapezoidal", QyStart = 0, QyEnd = -10
        }, elements => elements[1].NodeIdsJson = "[3,2]");

        var piece = result.Distributed.Single(p => p.MeshElementTag == "2");
        // Элемент 2 идёт от x=4 к x=2: в его узле i (x=4) интенсивность -4, в узле j (x=2) — -2.
        Assert.Equal((0.0, 1.0), (piece.AOverL, piece.BOverL));
        Assert.Equal(-4, piece.QAtA.Y, 8);
        Assert.Equal(-2, piece.QAtB.Y, 8);
    }

    [Fact]
    public void NoOverlap_ReportsErrorWithMemberTag()
    {
        var result = Segment(new FemMemberLoad
        {
            Id = 3, MemberId = 10, DistributionType = "uniform", QzStart = -1
        }, elements => elements.ForEach(e => e.SourceMemberTag = "other"));

        var error = Assert.Single(result.Errors);
        Assert.Equal(3, error.LoadId);
        Assert.Equal("10", error.MemberTag);
        Assert.Contains("не найдено пересечение", error.Message);
    }

    [Fact]
    public void UnknownMember_ErrorHasNullMemberTag()
    {
        var result = Segment(new FemMemberLoad { Id = 4, MemberId = 99, DistributionType = "uniform" });

        var error = Assert.Single(result.Errors);
        Assert.Null(error.MemberTag);
        Assert.Equal(4, error.LoadId);
    }

    [Fact]
    public void PointAtMeshNode_IsNodalWithForceAndMoment()
    {
        var result = Segment(new FemMemberLoad
        {
            MemberId = 10, DistributionType = "point", StartOffsetM = 4, QzStart = -100, My = 50
        });

        Assert.Empty(result.Errors);
        var point = Assert.Single(result.OnNodes);
        Assert.Equal("3", point.MeshNodeTag);
        Assert.Equal(-100, point.Force.Z);
        Assert.Equal(50, point.Moment.Y);
    }

    [Fact]
    public void PointInsideElement_HasRelativePosition()
    {
        var result = Segment(new FemMemberLoad
        {
            MemberId = 10, DistributionType = "point", StartOffsetM = 5, QzStart = -100
        });

        Assert.Empty(result.Errors);
        var point = Assert.Single(result.InElements);
        Assert.Equal("3", point.MeshElementTag);
        Assert.Equal(0.5, point.XOverL, 12);
    }

    [Fact]
    public void MomentInsideElement_IsError()
    {
        var result = Segment(new FemMemberLoad
        {
            MemberId = 10, DistributionType = "point", StartOffsetM = 5, Mz = 10
        });

        Assert.Contains("момент допустим только в узле", Assert.Single(result.Errors).Message);
        Assert.Empty(result.InElements);
    }

    [Fact]
    public void UnknownCoordinateSystemAndType_AreErrors()
    {
        var f = Fixture();
        var result = FemMemberLoadSegmenter.Segment(f.MeshNodes, f.Elements, f.Nodes, f.Members,
        [
            new FemMemberLoad { Id = 1, MemberId = 10, DistributionType = "uniform", CoordinateSystem = "polar" },
            new FemMemberLoad { Id = 2, MemberId = 10, DistributionType = "parabolic" }
        ]);

        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.LoadId == 1 && e.Message.Contains("неизвестная система координат"));
        Assert.Contains(result.Errors, e => e.LoadId == 2 && e.Message.Contains("неизвестный тип"));
    }

    [Fact]
    public void ZeroLengthMember_IsErrorNotException()
    {
        var f = Fixture();
        f.Nodes[1].X = 0;
        var result = FemMemberLoadSegmenter.Segment(f.MeshNodes, f.Elements, f.Nodes, f.Members,
            [new FemMemberLoad { MemberId = 10, DistributionType = "uniform", QzStart = -1 }]);

        Assert.Contains("нулевая или некорректная длина", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public void ReadNodeTags_ConvertsIntsAndRejectsBrokenInput()
    {
        Assert.Equal(new[] { "5", "12" }, FemMeshTopology.ReadNodeTags(new FemElement { NodeIdsJson = "[5,12]" }));
        Assert.Null(FemMeshTopology.ReadNodeTags(new FemElement { NodeIdsJson = "[5" }));
        Assert.Null(FemMeshTopology.ReadNodeTags(new FemElement { NodeIdsJson = "[5,6,7]" }, 2));
        Assert.Equal("1", FemMeshTopology.CanonicalNodeTag("01"));
        Assert.Null(FemMeshTopology.CanonicalNodeTag("A1"));
    }
}
