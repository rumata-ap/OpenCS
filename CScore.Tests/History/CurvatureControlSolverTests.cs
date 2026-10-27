using CScore.History;
using Xunit;

namespace CScore.Tests.History;

public class CurvatureControlSolverTests
{
    [Theory]
    [InlineData(0.0, -300.0)]
    [InlineData(0.3, 0.0)]
    public void Solution_HoldsN_AndMomentInPlane(double phi, double n0)
    {
        var s = StagedTestSections.SinglePart();
        var o = new LoadHistoryOptions { Calc = CalcType.C, Ten = false, Tol = 0.01, MaxIter = 100 };
        var solver = new CurvatureControlSolver(s, o);
        // u = (cos ph, sin ph) при ph = phi + π смотрит в сторону Mx < 0 (растянут низ, где арматура);
        // c > 0 вдоль u ⇒ ky = c·cos ph < 0 — растягивается арматурная грань.
        double ph = phi + Math.PI;
        double c = 0.004;
        Assert.True(solver.TrySolve(n0, ph, c, new Kurvature(), out var k, out _, out _));
        var f = s.Integral(k, CalcType.C, false);
        Assert.Equal(n0, f.N, 1);
        Assert.Equal(0.0, -f.Mx * Math.Sin(ph) + f.My * Math.Cos(ph), 1);
        Assert.Equal(c, k.ky * Math.Cos(ph) + k.kz * Math.Sin(ph), 12);
        Assert.True(f.Mx * Math.Cos(ph) + f.My * Math.Sin(ph) > 0); // момент вдоль u положителен
    }
}
