using CScore;
using Xunit;

namespace CScore.Tests;

public class SectionStateDiagnosticsTests
{
    [Fact]
    public void SecantStiffness_UsesSp63OrderAndIsSymmetric()
    {
        var d = SecantStiffnessMatrix.FromContributions(
            area: 2.0, x: 3.0, y: 5.0, eSec: 7.0);

        Assert.Equal(350.0, d.D11, 12);
        Assert.Equal(126.0, d.D22, 12);
        Assert.Equal(210.0, d.D12, 12);
        Assert.Equal(70.0,  d.D13, 12);
        Assert.Equal(42.0,  d.D23, 12);
        Assert.Equal(14.0,  d.D33, 12);
        Assert.Equal(d.D12, d.D21, 12);
    }

    [Fact]
    public void CalculateSecantStiffness_UsesContourAndPointFibersWithoutDoubleCounting()
    {
        var section = TestSections.RectWithBottomRebarNoMesh();
        var result = section.CalculateSecantStiffness(
            new Kurvature { e0 = -0.0001 }, CalcType.C, ten: true, ca: true);

        Assert.Equal("mixed", result.Source);
        Assert.True(result.Matrix.D33 > 0.0);
        Assert.Equal(result.Matrix.D12, result.Matrix.D21, 12);
    }
}
