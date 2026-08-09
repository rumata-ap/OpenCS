using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class StripResultantIntegratorTests
{
    [Fact]
    public void Integrate_ConstantLinearSource_MatchesConstitutiveIntegrationTangent()
    {
        var source = Source();
        var state = new BeamStrainState(0.001, 0.002, 0.003);

        var direct = StripResultantIntegrator.Integrate(2.0, [source, source], state);

        Assert.Equal(2.08, direct[0], 9);
        Assert.Equal(1.24, direct[1], 9);
        Assert.Equal(2.0, direct[2], 9);
    }

    [Fact]
    public void Integrate_ZeroState_ReturnsZero()
    {
        var source = Source();

        var direct = StripResultantIntegrator.Integrate(2.0, [source, source], BeamStrainState.Zero);

        Assert.Equal(new[] { 0.0, 0.0, 0.0 }, direct);
    }

    [Fact]
    public void Integrate_DifferentWidthSources_DiffersFromUniformCase()
    {
        var stiff = Source(a00: 2000.0, d00: 300.0);
        var soft = Source(a00: 200.0, d00: 30.0);
        var state = new BeamStrainState(0.001, 0.0, 0.0);

        var uniform = StripResultantIntegrator.Integrate(2.0, [stiff, stiff], state);
        var mixed = StripResultantIntegrator.Integrate(2.0, [stiff, soft], state);

        Assert.NotEqual(uniform[0], mixed[0]);
    }

    [Fact]
    public void Integrate_RejectsNonPositiveWidth()
    {
        var source = Source();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StripResultantIntegrator.Integrate(0.0, [source, source], BeamStrainState.Zero));
    }

    [Fact]
    public void Integrate_RejectsEmptyWidthSources()
    {
        Assert.Throws<ArgumentException>(() =>
            StripResultantIntegrator.Integrate(2.0, [], BeamStrainState.Zero));
    }

    [Fact]
    public void Integrate_RejectsNonFiniteState()
    {
        var source = Source();

        Assert.Throws<ArgumentException>(() =>
            StripResultantIntegrator.Integrate(2.0, [source, source], new BeamStrainState(double.NaN, 0, 0)));
    }

    static ConstantLinearPlateSectionResponse Source(double a00 = 1000.0, double d00 = 300.0)
    {
        var a = new double[3, 3];
        var b = new double[3, 3];
        var d = new double[3, 3];
        var ass = new double[2, 2];
        a[0, 0] = a00;
        b[0, 0] = 20.0;
        d[0, 0] = d00;
        a[1, 1] = 500.0;
        d[1, 1] = 100.0;
        ass[0, 0] = ass[1, 1] = 400.0;
        return new ConstantLinearPlateSectionResponse(a, b, d, ass, "source-fp");
    }
}
