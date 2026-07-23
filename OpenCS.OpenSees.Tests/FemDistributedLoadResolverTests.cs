using System.Text.Json;
using CScore.Fem;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public sealed class FemDistributedLoadResolverTests
{
    static (List<FemMeshNode> MeshNodes, List<FemElement> MeshElements,
        List<FemNode> SourceNodes, List<FemMember> SourceMembers) Fixture()
    {
        var meshNodes = new List<FemMeshNode>();
        var meshElements = new List<FemElement>();
        for (int i = 0; i <= 10; i += 2)
            meshNodes.Add(new FemMeshNode { NodeTag = (i / 2 + 1).ToString(), X = i, SourceMemberTag = "10" });
        for (int i = 0; i < 5; i++)
        {
            int first = i + 1;
            meshElements.Add(new FemElement
            {
                ElemTag = (i + 1).ToString(),
                NodeIdsJson = JsonSerializer.Serialize(new[] { first, first + 1 }),
                SourceMemberTag = "10"
            });
        }

        return (
            meshNodes,
            meshElements,
            [
                new FemNode { Id = 1, NodeTag = "1", X = 0 },
                new FemNode { Id = 2, NodeTag = "2", X = 10 }
            ],
            [new FemMember
            {
                Id = 10, ElemTag = "10", NodeIdsJson = "[1,2]", RotationDeg = 0
            }]);
    }

    [Fact]
    public void Resolve_PartialTrapezoidSplitsAcrossElements()
    {
        var fixture = Fixture();
        var load = new FemMemberLoad
        {
            MemberId = 10, StartOffsetM = 2, EndOffsetM = 1,
            DistributionType = "trapezoidal", CoordinateSystem = "local",
            QyStart = -1000, QyEnd = -3000
        };

        var result = new FemDistributedLoadResolver().Resolve(
            fixture.MeshNodes, fixture.MeshElements, fixture.SourceNodes, fixture.SourceMembers, [load]);

        Assert.Empty(result.Errors);
        Assert.Equal(4, result.Loads.Count);
        Assert.Equal((0.0, 1.0), (result.Loads[0].AOverL, result.Loads[0].BOverL));
        Assert.Equal((0.0, 1.0), (result.Loads[1].AOverL, result.Loads[1].BOverL));
        Assert.Equal((0.0, 1.0), (result.Loads[2].AOverL, result.Loads[2].BOverL));
        Assert.Equal((0.0, 0.5), (result.Loads[3].AOverL, result.Loads[3].BOverL));
        Assert.Equal(-1000, result.Loads[0].WyStart, 8);
        Assert.Equal(-3000, result.Loads[3].WyEnd, 8);
    }

    [Fact]
    public void Resolve_GlobalLoadProjectsIntoElementLocalAxes()
    {
        var fixture = Fixture();
        var load = new FemMemberLoad
        {
            MemberId = 10, CoordinateSystem = "global", DistributionType = "uniform",
            QyStart = 2000, QyEnd = 2000
        };

        var result = new FemDistributedLoadResolver().Resolve(
            fixture.MeshNodes, fixture.MeshElements, fixture.SourceNodes, fixture.SourceMembers, [load]);

        Assert.Empty(result.Errors);
        Assert.Equal(-2000, result.Loads[0].WzStart, 8);
        Assert.Equal(0, result.Loads[0].WyStart, 8);
    }

    [Fact]
    public void Resolve_ReversedMeshElementKeepsLoadDirectionAndSwapsEnds()
    {
        var fixture = Fixture();
        fixture.MeshElements[1].NodeIdsJson = "[3,2]";
        var load = new FemMemberLoad
        {
            MemberId = 10, StartOffsetM = 2, EndOffsetM = 1,
            DistributionType = "trapezoidal", QyStart = -1000, QyEnd = -3000
        };

        var result = new FemDistributedLoadResolver().Resolve(
            fixture.MeshNodes, fixture.MeshElements, fixture.SourceNodes, fixture.SourceMembers, [load]);

        Assert.Contains(result.Loads, item => item.ElementTag == 2);
        var reversed = result.Loads.Single(item => item.ElementTag == 2);
        Assert.Equal(-1571.4285714285713, reversed.WyStart, 8);
        Assert.Equal(-1000, reversed.WyEnd, 8);
    }
}
