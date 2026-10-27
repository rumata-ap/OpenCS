using CScore.History;
using Xunit;

namespace CScore.Tests.History;

public class PartUtilizationTests
{
    [Fact]
    public void Evaluate_UsesRelativePlane_AndSp63Limits()
    {
        var s = StagedTestSections.TwoPartBeam();
        var k1 = new Kurvature { e0 = -1e-4, ky = 1e-3 };
        s.ActivatePart(1, k1);
        var k = new Kurvature { e0 = -2e-4, ky = 3e-3 };

        var p1 = PartUtilization.Evaluate(s, 1, k, CalcType.C);
        var rel = k - k1; // ε = -1e-4 + 2e-3·y
        // бетон части 1: y ∈ [0,25; 0,35] → ε ∈ [4e-4; 6e-4], сжатия нет
        Assert.Equal(-1e-4 + 2e-3 * 0.25, p1.ConcreteEpsMin, 12);
        Assert.Equal(0.0, p1.EtaConcrete);
        // арматура части 1: y = 0,3 → ε = 5e-4, εs2 = Et2 материала (у тестовой A500 — 0,025)
        Assert.Equal(rel.e0 + rel.ky * 0.3, p1.RebarEpsMax, 12);
        Assert.True(p1.EtaRebar > 0);

        var p0 = PartUtilization.Evaluate(s, 0, k, CalcType.C);
        double epsBottom = k.e0 + k.ky * -0.25; // -9,5e-4
        Assert.Equal(epsBottom, p0.ConcreteEpsMin, 12);
        double eb2 = PartUtilization.LimitConcrete(s.Parts[0].Areas[0], CalcType.C);
        Assert.Equal(epsBottom / eb2, p0.EtaConcrete, 12);
        Assert.Equal(-0.25, p0.ConcreteMinPoint.Y, 12);
    }
}
