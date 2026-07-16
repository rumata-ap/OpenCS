using CScore.Fem;
using Xunit;

namespace CScore.Tests;

public sealed class FemCanonicalContractTests
{
    [Fact]
    public void NodeLoad_HoldsSixGlobalComponents()
    {
        var load = new FemNodeLoad
        {
            LoadCaseId = 4,
            NodeId = 12,
            Fx = 10.0,
            Fy = -2.0,
            Fz = 3.0,
            Mx = 4.0,
            My = 5.0,
            Mz = -6.0
        };

        Assert.Equal(12, load.NodeId);
        Assert.Equal(-6.0, load.Mz);
    }

    [Fact]
    public void LoadExpression_RoundTripsTermsAndMode()
    {
        var expression = new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sp20,
            LoadCaseIds = [2, 5],
            Terms =
            [
                new FemLoadTerm { LoadCaseId = 2, Coefficient = 1.1 },
                new FemLoadTerm { LoadCaseId = 5, Coefficient = 0.7 }
            ],
            CombinationType = "fundamental"
        };

        var json = expression.ToJson();
        var copy = FemLoadExpression.Parse(json);

        Assert.Equal(FemLoadExpressionMode.Sp20, copy.Mode);
        Assert.Equal([2, 5], copy.LoadCaseIds);
        Assert.Equal(0.7, copy.Terms[1].Coefficient);
        Assert.Equal("fundamental", copy.CombinationType);
    }
}
