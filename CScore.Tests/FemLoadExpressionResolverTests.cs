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

    [Fact]
    public void Resolve_WithMemberLoads_ScalesMemberIntensityWithoutMergingIntervals()
    {
        var expr = new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum,
            Terms = [new FemLoadTerm { LoadCaseId = 2, Coefficient = 2 }]
        };
        var memberLoads = new[]
        {
            new FemMemberLoad
            {
                Id = 5, SchemaId = 1, LoadCaseId = 2, MemberId = 10,
                StartOffsetM = 1, EndOffsetM = 2, QyStart = -300, QyEnd = -500
            }
        };

        var result = FemLoadExpressionResolver.Resolve(expr, Cases(), Loads(), memberLoads);

        var load = Assert.Single(result.MemberLoads);
        Assert.Equal(-600, load.QyStart);
        Assert.Equal(-1000, load.QyEnd);
        Assert.Equal(1, load.StartOffsetM);
        Assert.Equal(2, load.EndOffsetM);
    }

    [Fact]
    public void Resolve_Sp20Terms_AppliesMaterializedCoefficientsToMemberLoads()
    {
        var expr = new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sp20,
            Terms = [new FemLoadTerm { LoadCaseId = 1, Coefficient = 1.4 }]
        };
        var memberLoads = new[]
        {
            new FemMemberLoad
            {
                Id = 5, SchemaId = 1, LoadCaseId = 1, MemberId = 10,
                QzStart = -1000, QzEnd = -1000
            }
        };

        var result = FemLoadExpressionResolver.Resolve(expr, Cases(), Loads(), memberLoads);

        Assert.Equal(-1400, Assert.Single(result.MemberLoads).QzEnd);
    }

    [Fact]
    public void Resolve_ScalesMemberLoadMomentComponents()
    {
        var cases = new List<FemLoadCase> { new() { Id = 1, Tag = "Q" } };
        var memberLoads = new List<FemMemberLoad>
        {
            new() { Id = 10, LoadCaseId = 1, MemberId = 5, DistributionType = "point",
                    Mx = 100, My = -50, Mz = 25 }
        };
        var expr = new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum,
            Terms = [new FemLoadTerm { LoadCaseId = 1, Coefficient = 1.4 }]
        };

        var result = FemLoadExpressionResolver.Resolve(expr, cases, [], memberLoads);

        var load = Assert.Single(result.MemberLoads);
        Assert.Equal(140, load.Mx, 8);
        Assert.Equal(-70, load.My, 8);
        Assert.Equal(35, load.Mz, 8);
    }

    [Fact]
    public void Resolve_ScalesKinematicLoadsWithTheSameCombinationFactor()
    {
        var expression = new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum,
            Terms = [new FemLoadTerm { LoadCaseId = 2, Coefficient = 1.5 }]
        };
        var result = FemLoadExpressionResolver.Resolve(
            expression,
            [new FemLoadCase { Id = 2 }],
            [new FemNodeLoad { LoadCaseId = 2, NodeId = 10, Fx = 100 }],
            [],
            [new FemKinematicLoad { LoadCaseId = 2, NodeId = 10, Dof = 1, Value = 0.02 }]);

        Assert.Equal(150, Assert.Single(result.NodeLoads).Fx);
        var kinematic = Assert.Single(result.KinematicLoads);
        Assert.Equal(1, kinematic.Dof);
        Assert.Equal(0.03, kinematic.Value);
    }
}
