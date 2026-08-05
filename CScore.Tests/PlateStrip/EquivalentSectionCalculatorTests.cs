using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class EquivalentSectionCalculatorTests
{
    [Fact]
    public void ConstantLinearResponse_ReturnsSixResultantsFromFullAbdMatrix()
    {
        var source = Source();

        var result = source.Forces(new ShellStrainState(0.001, 0.0, 0.0, 0.002, 0.0, 0.0));

        Assert.Equal(1.04, result.Nx, 12);
        Assert.Equal(0.62, result.Mx, 12);
        Assert.Equal(new[] { 1.04, 0.0, 0.0, 0.62, 0.0, 0.0 }, result.ToArray());
    }

    [Fact]
    public void DirectUniaxial_UsesOnlyAxxAndDxx()
    {
        var result = Build(ReductionPolicy.DirectUniaxial);
        var k = result.Section!.BeamTangent;

        Assert.Equal(2000.0, k[0, 0], 10);
        Assert.Equal(0.0, k[0, 1], 10);
        Assert.Equal(0.0, k[0, 2], 10);
        Assert.Equal(600.0, k[1, 1], 10);
        Assert.Equal(1000.0 * 8.0 / 12.0, k[2, 2], 10);
        Assert.Contains(result.Diagnostics, d => d.Code == "equivalent_section_direct_dropped_terms");
    }

    [Fact]
    public void ConstitutiveIntegration_PreservesBxxAndInPlaneSecondMoment()
    {
        var result = Build(ReductionPolicy.ConstitutiveIntegration);
        var k = result.Section!.BeamTangent;

        Assert.Equal(2000.0, k[0, 0], 10);
        Assert.Equal(40.0, k[0, 1], 10);
        Assert.Equal(40.0, k[1, 0], 10);
        Assert.Equal(600.0, k[1, 1], 10);
        Assert.Equal(1000.0 * 8.0 / 12.0, k[2, 2], 10);
        Assert.Equal(0.0, k[0, 2], 10);
        Assert.Equal(0.0, k[1, 2], 10);
    }

    [Fact]
    public void Calculator_RejectsNonPositiveWidth()
    {
        var analogy = Analogy(0.0);

        var result = EquivalentSectionCalculator.Build(
            analogy, Source(), ReductionPolicy.ConstitutiveIntegration, 2);

        Assert.False(result.IsCalculable);
        Assert.Null(result.Section);
        Assert.Contains(result.Diagnostics, d => d.Code == "equivalent_section_invalid_width");
    }

    static EquivalentSectionBuildResult Build(ReductionPolicy policy)
        => EquivalentSectionCalculator.Build(Analogy(), Source(), policy, 2);

    static ConstantLinearPlateSectionResponse Source()
    {
        var a = new double[3, 3];
        var b = new double[3, 3];
        var d = new double[3, 3];
        var ass = new double[2, 2];
        a[0, 0] = 1000.0;
        b[0, 0] = 20.0;
        d[0, 0] = 300.0;
        a[1, 1] = 500.0;
        d[1, 1] = 100.0;
        ass[0, 0] = ass[1, 1] = 400.0;
        return new ConstantLinearPlateSectionResponse(a, b, d, ass, "source-fp");
    }

    static PlateStripBeamAnalogy Analogy(double width = 2.0) => new()
    {
        Id = "strip-1",
        SourceRegionId = 10,
        ExplicitWidthM = width,
        Fingerprint = "strip-fp",
        Geometry = new PlateStripGeometry { LengthM = 6.0 }
    };
}
