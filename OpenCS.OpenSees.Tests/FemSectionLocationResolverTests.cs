using CScore.Fem;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Structural;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public sealed class FemSectionLocationResolverTests
{
    [Fact]
    public void Resolve_TwoMeshSegments_UsesCumulativeMemberDistance()
    {
        var nodes = new List<FemMeshNode>
        {
            new() { NodeTag = "1", X = 0 },
            new() { NodeTag = "2", X = 1 },
            new() { NodeTag = "3", X = 2 }
        };
        var elements = new List<FemElement>
        {
            new() { ElemTag = "10", NodeIdsJson = "[1,2]", SourceMemberTag = "M" },
            new() { ElemTag = "11", NodeIdsJson = "[2,3]", SourceMemberTag = "M" }
        };
        var members = new List<FemMember>
        {
            new() { ElemTag = "M", NodeIdsJson = "[1,3]" }
        };
        var locations = new List<FemNonlinearSectionLocation>
        {
            new(10, 1, 1, 5, 0.5, 1.0, 0.5),
            new(11, 1, 1, 5, 0.5, 1.0, 0.5)
        };

        var rows = new FemSectionLocationResolver().Resolve(
            nodes, elements, members, locations,
            new HashSet<(int ElementTag, int IntegrationPoint)> { (10, 1), (11, 1) });

        Assert.Equal(2, rows.Count);
        Assert.Equal(0.5, rows[0].PositionFromMemberStartM, 8);
        Assert.Equal(1.5, rows[1].PositionFromMemberStartM, 8);
        Assert.Equal(2.0, rows[0].MemberLengthM, 8);
        Assert.Equal(0.25, rows[0].RelativePosition, 8);
        Assert.Equal(0.75, rows[1].RelativePosition, 8);
    }

    [Fact]
    public void Resolve_ReversedMeshSegment_InvertsLocalDistance()
    {
        var nodes = new List<FemMeshNode>
        {
            new() { NodeTag = "1", X = 0 },
            new() { NodeTag = "2", X = 1 }
        };
        var elements = new List<FemElement>
        {
            new() { ElemTag = "10", NodeIdsJson = "[2,1]", SourceMemberTag = "M" }
        };
        var members = new List<FemMember>
        {
            new() { ElemTag = "M", NodeIdsJson = "[1,2]" }
        };
        var locations = new List<FemNonlinearSectionLocation>
        {
            new(10, 1, 1, 5, 0.25, 1.0, 0.25)
        };

        var row = Assert.Single(new FemSectionLocationResolver().Resolve(
            nodes, elements, members, locations,
            new HashSet<(int ElementTag, int IntegrationPoint)> { (10, 1) }));

        Assert.Equal(0.75, row.PositionFromMemberStartM, 8);
        Assert.Equal(0.75, row.RelativePosition, 8);
    }
}
