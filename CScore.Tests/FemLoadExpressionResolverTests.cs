using CScore.Fem;
using CScore.Fem.Combinations;
using Xunit;

public class FemLoadExpressionResolverTests
{
    static List<FemNodeLoad> Loads() =>
    [
        new() { Id = 1, LoadCaseId = 1, NodeId = 2, Fz = -1000 },
        new() { Id = 2, LoadCaseId = 2, NodeId = 2, Fz = -500 },
    ];
    static List<FemLoadCase> Cases() => [new() { Id = 1 }, new() { Id = 2 }];

    [Fact]
    public void Resolve_Single_ReturnsOneCase()
    {
        var expr = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [1] };
        var r = FemLoadExpressionResolver.Resolve(expr, Cases(), Loads());
        Assert.Equal(-1000, r.Single(l => l.NodeId == 2).Fz, 6);
    }

    [Fact]
    public void Resolve_All_SumsCases()
    {
        var expr = new FemLoadExpression { Mode = FemLoadExpressionMode.All };
        var r = FemLoadExpressionResolver.Resolve(expr, Cases(), Loads());
        Assert.Equal(-1500, r.Single(l => l.NodeId == 2).Fz, 6);
    }

    [Fact]
    public void Resolve_Sp20_Throws()
    {
        var expr = new FemLoadExpression { Mode = FemLoadExpressionMode.Sp20 };
        Assert.Throws<NotSupportedException>(() => FemLoadExpressionResolver.Resolve(expr, Cases(), Loads()));
    }
}
