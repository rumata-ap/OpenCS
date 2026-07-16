using CScore.Fem;
using CScore.Fem.Combinations;
using Xunit;

namespace CScore.Tests;

public sealed class FemLoadVectorBuilderTests
{
    [Fact]
    public void BuildVector_UsesProvidedNodeOrderAndSixDofsPerNode()
    {
        var nodes = new[]
        {
            new FemNode { Id = 20 },
            new FemNode { Id = 10 }
        };
        var loads = new[]
        {
            new FemNodeLoad { LoadCaseId = 1, NodeId = 10, Fx = 1, Mz = 6 },
            new FemNodeLoad { LoadCaseId = 1, NodeId = 20, Fy = 2 }
        };

        var vector = FemLoadVectorBuilder.Build(nodes, loads, [10, 20], 1);

        Assert.Equal(12, vector.Length);
        Assert.Equal(1, vector[0]);
        Assert.Equal(6, vector[5]);
        Assert.Equal(2, vector[7]);
    }

    [Fact]
    public void BuildVector_SumsDuplicateNodeLoadRows()
    {
        var nodes = new[] { new FemNode { Id = 10 } };
        var loads = new[]
        {
            new FemNodeLoad { LoadCaseId = 1, NodeId = 10, Fx = 3 },
            new FemNodeLoad { LoadCaseId = 1, NodeId = 10, Fx = 4 }
        };

        var vector = FemLoadVectorBuilder.Build(nodes, loads, [10], 1);

        Assert.Equal(7, vector[0]);
    }
}
