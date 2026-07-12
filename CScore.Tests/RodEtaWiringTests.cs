using System;
using CScore.Sp63;
using Xunit;

namespace CScore.Tests;

public class RodEtaWiringTests
{
    [Fact]
    public void Apply_FormulaMode_AmplifiesBothAxesIndependently()
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.3, 0.6);

        // Синтетический "решатель" не используется в режиме A (D — из геометрии),
        // но делегат обязателен по сигнатуре — не должен вызываться.
        Kurvature Solve(double mx, double my) =>
            throw new InvalidOperationException("режим A не должен обращаться к решателю");

        // BuildReinforcedRectangle(0.3, 0.6): высота в плоскости Mx (по Y) = 0.6,
        // высота в плоскости My (по X) = 0.3 — l0x/l0y подобраны так, чтобы
        // гибкость превышала порог 14 по ОБЕИМ осям (l0x/0.6>14 и l0y/0.3>14).
        var result = RodEtaWiring.Apply(
            section, n: -800, mx0: 80, my0: 40,
            l0x: 10, l0y: 6, m1lx: 40, m1ly: 20,
            iterative: false, jointSolve: Solve);

        Assert.True(result.X.Slender, "l0x/hx=10/0.6≈16.7>14 должно считаться гибким");
        Assert.True(result.Y.Slender, "l0y/hy=6/0.3=20>14 должно считаться гибким");
        Assert.True(result.X.Eta > 1.0, $"ηx={result.X.Eta}");
        Assert.True(result.Y.Eta > 1.0, $"ηy={result.Y.Eta}");
        Assert.Equal(80 * result.X.Eta, result.MxEff, precision: 6);
        Assert.Equal(40 * result.Y.Eta, result.MyEff, precision: 6);
    }

    [Fact]
    public void Apply_IterativeMode_CallsJointSolveAndAmplifiesSequentially()
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.3, 0.6);
        const double dxConst = 175_500 + 24_544; // как в EccentricityAmplifierTests
        const double dyConst = 43_875 + 3_927;

        Kurvature Solve(double mx, double my) => new Kurvature
        {
            ky = mx / dxConst,
            kz = my / dyConst,
        };

        var result = RodEtaWiring.Apply(
            section, n: -800, mx0: 80, my0: 40,
            l0x: 10, l0y: 6, m1lx: 40, m1ly: 20,
            iterative: true, jointSolve: Solve);

        Assert.True(result.X.Stable);
        Assert.True(result.Y.Stable);
        Assert.True(result.X.Eta > 1.0);
        Assert.True(result.Y.Eta > 1.0);
        Assert.True(result.X.Iterations > 0);
    }

    [Fact]
    public void Apply_NotSlender_ReturnsOriginalMoments()
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.3, 0.6);

        var result = RodEtaWiring.Apply(
            section, n: -500, mx0: 50, my0: 20,
            l0x: 3, l0y: 3, m1lx: 25, m1ly: 10,
            iterative: false, jointSolve: (_, _) => new Kurvature());

        Assert.Equal(50, result.MxEff);
        Assert.Equal(20, result.MyEff);
        Assert.False(result.X.Slender);
        Assert.False(result.Y.Slender);
    }
}
