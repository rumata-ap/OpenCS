using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class EquivalentSectionFingerprintTests
{
    [Fact]
    public void Compute_IsStableForSameInputs()
    {
        var source = Source();
        var a = EquivalentSectionFingerprint.Compute(Analogy(), source, ReductionPolicy.ConstitutiveIntegration, 2);
        var b = EquivalentSectionFingerprint.Compute(Analogy(), source, ReductionPolicy.ConstitutiveIntegration, 2);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Compute_ChangesWhenPolicyWidthOrSourceChanges()
    {
        var source = Source();
        var baseline = EquivalentSectionFingerprint.Compute(
            Analogy(), source, ReductionPolicy.ConstitutiveIntegration, 2);

        var direct = EquivalentSectionFingerprint.Compute(
            Analogy(), source, ReductionPolicy.DirectUniaxial, 2);
        var wide = EquivalentSectionFingerprint.Compute(
            Analogy(3.0), source, ReductionPolicy.ConstitutiveIntegration, 2);
        var otherSource = new ConstantLinearPlateSectionResponse(
            new[,] { { 1100.0, 0.0, 0.0 }, { 0.0, 500.0, 0.0 }, { 0.0, 0.0, 500.0 } },
            new double[3, 3], new[,] { { 300.0, 0.0, 0.0 }, { 0.0, 100.0, 0.0 }, { 0.0, 0.0, 100.0 } },
            new[,] { { 400.0, 0.0 }, { 0.0, 400.0 } }, "other-source");
        var changedSource = EquivalentSectionFingerprint.Compute(
            Analogy(), otherSource, ReductionPolicy.ConstitutiveIntegration, 2);

        Assert.NotEqual(baseline, direct);
        Assert.NotEqual(baseline, wide);
        Assert.NotEqual(baseline, changedSource);
    }

    static ConstantLinearPlateSectionResponse Source() => new(
        new[,] { { 1000.0, 0.0, 0.0 }, { 0.0, 500.0, 0.0 }, { 0.0, 0.0, 500.0 } },
        new double[3, 3],
        new[,] { { 300.0, 0.0, 0.0 }, { 0.0, 100.0, 0.0 }, { 0.0, 0.0, 100.0 } },
        new[,] { { 400.0, 0.0 }, { 0.0, 400.0 } }, "source-fp");

    static PlateStripBeamAnalogy Analogy(double width = 2.0) => new()
    {
        Id = "strip-1",
        SourceRegionId = 10,
        ExplicitWidthM = width,
        Fingerprint = "strip-fp",
        Geometry = new PlateStripGeometry { LengthM = 6.0 }
    };
}
