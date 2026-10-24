using CScore.Tests.LimitSurface;
using Xunit;

namespace CScore.Tests;

/// <summary>
/// Регрессия: при большом N быстрый решатель принимал плоскость, чьи моменты отличались от цели на 1–2 %.
/// Невязка считалась как |(ΔN, ΔMx, ΔMy)| с допуском 1e-3·|(N, Mx, My)| — допуск задавало N, и ошибка
/// момента (а с ней и направления) терялась на его фоне. Плоскость решения обязана давать заявленные
/// усилия по каждой компоненте.
/// </summary>
public class LimitForceSolverFastResidualTests
{
    // Точки сверки поверхности предельных усилий (asymmetric), где невязка достигала 0,3–0,6 % M_max.
    public static TheoryData<double, double, double> HighAxialPoints => new()
    {
        { -2257.6, 59.1, 80.6 },
        { -2266.0, 51.4, -85.8 },
        { -1630.5, 90.8, -41.9 },
        { -1927.9, 47.2, 88.2 },
        { -1098.4, 91.2, -41.0 },
    };

    [Theory]
    [MemberData(nameof(HighAxialPoints))]
    public void MomentFactor_PlaneReproducesTargetMoments(double n, double mx, double my)
    {
        var section = LimitSurfaceTestSections.Asymmetric();
        var r = new LimitForceSolverFast(section, CalcType.C, ten: false).MomentFactor(n, mx, my);
        Assert.True(r.Converged);
        AssertPlaneMatches(section, r, n, r.Factor * mx, r.Factor * my);
    }

    [Theory]
    [MemberData(nameof(HighAxialPoints))]
    public void AllFactor_PlaneReproducesTargetForces(double n, double mx, double my)
    {
        var section = LimitSurfaceTestSections.Asymmetric();
        var r = new LimitForceSolverFast(section, CalcType.C, ten: false).AllFactor(0.8 * n, mx, my);
        Assert.True(r.Converged);
        AssertPlaneMatches(section, r, r.Factor * 0.8 * n, r.Factor * mx, r.Factor * my);
    }

    static void AssertPlaneMatches(CrossSection section, LimitForceResult r, double nT, double mxT, double myT)
    {
        var f = section.CloneForCalc().Compute(r.StrainPlane!.Value, CalcType.C, false, computeStiffness: false);
        double mT = Math.Sqrt(mxT * mxT + myT * myT);
        double dM = Math.Sqrt(Math.Pow(f.Mx - mxT, 2) + Math.Pow(f.My - myT, 2));
        Assert.True(Math.Abs(f.N - nT) <= 2e-3 * Math.Abs(nT), $"ΔN = {f.N - nT:G4} при N = {nT:G6}");
        Assert.True(dM <= 2e-3 * mT, $"|ΔM| = {dM:G4} при |M| = {mT:G6} (Mx {f.Mx:G6}/{mxT:G6}, My {f.My:G6}/{myT:G6})");
    }
}
