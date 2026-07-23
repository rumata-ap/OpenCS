using System.Text.Json;
using CScore.Fem;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public sealed class FemPointLoadResolverTests
{
    // Стержень 0..10 м, разбит на 5 mesh-элементов по 2 м (узлы сетки на 0,2,4,6,8,10),
    // как в FemDistributedLoadResolverTests.Fixture().
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
            [new FemMember { Id = 10, ElemTag = "10", NodeIdsJson = "[1,2]", RotationDeg = 0 }]);
    }

    [Fact]
    public void Resolve_ForceAtMeshNodeBecomesNodalLoad()
    {
        var f = Fixture();
        var load = new FemMemberLoad
        {
            MemberId = 10, DistributionType = "point", CoordinateSystem = "local",
            StartOffsetM = 4, QyStart = -1000
        };

        var result = new FemPointLoadResolver().Resolve(f.MeshNodes, f.MeshElements, f.SourceNodes, f.SourceMembers, [load]);

        Assert.Empty(result.Errors);
        Assert.Empty(result.ElementLoads);
        var nodal = Assert.Single(result.NodalLoads);
        Assert.Equal(3, nodal.NodeTag);   // узел сетки на X=4 — тег "3"
        // Стержень вдоль глобальной X: локальная ось Y стержня = глобальная Z (см. FemLocalAxis).
        Assert.Equal(-1000, nodal.Fz, 8);
        Assert.Equal(0, nodal.Fy, 8);
    }

    [Fact]
    public void Resolve_MomentAtMeshNodeBecomesNodalLoad()
    {
        var f = Fixture();
        var load = new FemMemberLoad
        {
            MemberId = 10, DistributionType = "point", CoordinateSystem = "global",
            StartOffsetM = 0, Mz = 500
        };

        var result = new FemPointLoadResolver().Resolve(f.MeshNodes, f.MeshElements, f.SourceNodes, f.SourceMembers, [load]);

        Assert.Empty(result.Errors);
        var nodal = Assert.Single(result.NodalLoads);
        Assert.Equal(1, nodal.NodeTag);
        Assert.Equal(500, nodal.Mz, 8);
    }

    [Fact]
    public void Resolve_ForceInsideElementBecomesBeamPointLoad()
    {
        var f = Fixture();
        var load = new FemMemberLoad
        {
            MemberId = 10, DistributionType = "point", CoordinateSystem = "local",
            StartOffsetM = 5, QyStart = -2000
        };

        var result = new FemPointLoadResolver().Resolve(f.MeshNodes, f.MeshElements, f.SourceNodes, f.SourceMembers, [load]);

        Assert.Empty(result.Errors);
        Assert.Empty(result.NodalLoads);
        var element = Assert.Single(result.ElementLoads);
        Assert.Equal(3, element.ElementTag);   // элемент [4,6] содержит X=5
        Assert.Equal(-2000, element.Py, 8);
        Assert.Equal(0.5, element.XOverL, 8);
    }

    [Fact]
    public void Resolve_MomentInsideElementReportsError()
    {
        var f = Fixture();
        var load = new FemMemberLoad
        {
            MemberId = 10, DistributionType = "point", CoordinateSystem = "local",
            StartOffsetM = 5, Mx = 100
        };

        var result = new FemPointLoadResolver().Resolve(f.MeshNodes, f.MeshElements, f.SourceNodes, f.SourceMembers, [load]);

        Assert.Empty(result.NodalLoads);
        Assert.Empty(result.ElementLoads);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Resolve_GlobalForceProjectsIntoElementLocalAxes()
    {
        var f = Fixture();
        var load = new FemMemberLoad
        {
            MemberId = 10, DistributionType = "point", CoordinateSystem = "global",
            StartOffsetM = 5, QyStart = 2000
        };

        var result = new FemPointLoadResolver().Resolve(f.MeshNodes, f.MeshElements, f.SourceNodes, f.SourceMembers, [load]);

        Assert.Empty(result.Errors);
        var element = Assert.Single(result.ElementLoads);
        // Стержень вдоль глобальной X: локальная ось Y стержня = глобальная Z (см. FemLocalAxis),
        // поэтому глобальная Qy проецируется в Pz локальных осей элемента, Py = 0.
        Assert.Equal(-2000, element.Pz, 8);
        Assert.Equal(0, element.Py, 8);
    }
}
