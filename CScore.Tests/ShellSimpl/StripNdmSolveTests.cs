using CScore.ShellSimpl;
using Xunit;
using static CScore.Tests.ShellSimpl.StripNdmFixture;

namespace CScore.Tests.ShellSimpl;

/// <summary>Равновесие полосы при заданных (N, M): <see cref="StripNdm.Solve"/>.</summary>
public sealed class StripNdmSolveTests
{
    // Несущая способность ряда на растяжение: 5,65 см²/м · 435 МПа ≈ 246 кН/м; M_ult изгиба ≈ 38 кН·м/м.
    [Theory]
    [InlineData(0.0, 30.0, DiagrammType.L3)]      // изгиб, верх растянут
    [InlineData(0.0, -25.0, DiagrammType.L3)]     // изгиб, низ растянут
    [InlineData(-800.0, 20.0, DiagrammType.L3)]   // внецентренное сжатие
    [InlineData(-2500.0, 5.0, DiagrammType.L3)]   // сжатие почти по всей высоте
    [InlineData(100.0, 20.0, DiagrammType.L3)]    // внецентренное растяжение
    [InlineData(300.0, 5.0, DiagrammType.L3)]     // сквозное растяжение
    [InlineData(0.0, 30.0, DiagrammType.SP63)]
    [InlineData(-800.0, 25.0, DiagrammType.SP63)]
    public void Solve_ReproducesTargetForces(double n, double m, DiagrammType type)
    {
        var s = Strip(CalcType.C, type);
        var st = StripNdm.Solve(s, n, m, tensionConcrete: false);

        Assert.True(st.Converged, st.Reason);
        var f = s.Integrate(st.Eps0, st.Kappa, false);
        Assert.Equal(n, f.N, 1e-3);
        Assert.Equal(m, f.M, 1e-4);
    }

    [Fact]
    public void Solve_ThroughTension_BothRowsTensile_NoCompressionZone()
    {
        var s = Strip(CalcType.C);
        var st = StripNdm.Solve(s, 300.0, 5.0, false);
        Assert.True(st.Converged, st.Reason);
        Assert.True(st.EpsAt(Z) > 0 && st.EpsAt(-Z) > 0);
        Assert.Equal(0.0, st.CompressionHeight(H));

        // Бетон не работает — усилия делят ряды: F_t + F_c = N, (F_t − F_c)·z = M.
        double ft = (300.0 + 5.0 / Z) / 2.0, fc = 300.0 - ft;
        Assert.Equal(ft / AsPerMeter / 200_000_000.0, st.EpsAt(Z), 1e-7);
        Assert.Equal(fc / AsPerMeter / 200_000_000.0, st.EpsAt(-Z), 1e-7);
    }

    [Fact]
    public void Solve_Bending_CompressionHeightIsPositiveAndOnCompressedFace()
    {
        var st = StripNdm.Solve(Strip(CalcType.C), 0.0, 30.0, false);
        Assert.True(st.EpsAt(H / 2) > 0 && st.EpsAt(-H / 2) < 0);
        double x = st.CompressionHeight(H);
        Assert.InRange(x, 0.005, 0.05);
        Assert.Equal(-H / 2 + x, st.NeutralAxis, 1e-9);
    }

    [Theory]
    [InlineData(0.0, 500.0)]      // момент за пределом несущей способности
    [InlineData(800.0, 0.0)]      // растяжение больше, чем выдерживают оба ряда (≈ 492 кН/м)
    [InlineData(-6000.0, 0.0)]    // сжатие больше несущей способности
    public void Solve_BeyondCapacity_NotConvergedWithReason(double n, double m)
    {
        var st = StripNdm.Solve(Strip(CalcType.C), n, m, false);
        Assert.False(st.Converged);
        Assert.False(string.IsNullOrWhiteSpace(st.Reason));
    }
}
