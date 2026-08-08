using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class EquivalentSectionFingerprintTests
{
    [Fact]
    public void Compute_IsStableForSameInputs()
    {
        var source = Source();
        var a = Compute(source, source);
        var b = Compute(source, source);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Compute_ChangesWhenPolicyWidthOrCenterlineSourceChanges()
    {
        var source = Source();
        var baseline = Compute(source, source);

        var otherPolicy = EquivalentSectionFingerprint.Compute(
            Analogy(), 4, "region-fp", 0.5, source, [source, source],
            ReductionPolicy.DirectUniaxial, 2);
        var wide = EquivalentSectionFingerprint.Compute(
            Analogy(3.0), 4, "region-fp", 0.5, source, [source, source],
            ReductionPolicy.ConstitutiveIntegration, 2);
        var otherSource = new ConstantLinearPlateSectionResponse(
            new[,] { { 1100.0, 0.0, 0.0 }, { 0.0, 500.0, 0.0 }, { 0.0, 0.0, 500.0 } },
            new double[3, 3], new[,] { { 300.0, 0.0, 0.0 }, { 0.0, 100.0, 0.0 }, { 0.0, 0.0, 100.0 } },
            new[,] { { 400.0, 0.0 }, { 0.0, 400.0 } }, "other-source");
        var changedCenterline = Compute(otherSource, source);

        Assert.NotEqual(baseline, otherPolicy);
        Assert.NotEqual(baseline, wide);
        Assert.NotEqual(baseline, changedCenterline);
    }

    [Fact]
    public void Compute_ChangesWhenSchemaOrRegionFingerprintOrStationChanges()
    {
        var source = Source();
        var baseline = Compute(source, source);

        var otherSchema = EquivalentSectionFingerprint.Compute(
            Analogy(), 5, "region-fp", 0.5, source, [source, source],
            ReductionPolicy.ConstitutiveIntegration, 2);
        var otherRegionFingerprint = EquivalentSectionFingerprint.Compute(
            Analogy(), 4, "region-fp-changed", 0.5, source, [source, source],
            ReductionPolicy.ConstitutiveIntegration, 2);
        var otherStation = EquivalentSectionFingerprint.Compute(
            Analogy(), 4, "region-fp", 0.75, source, [source, source],
            ReductionPolicy.ConstitutiveIntegration, 2);

        Assert.NotEqual(baseline, otherSchema);
        Assert.NotEqual(baseline, otherRegionFingerprint);
        Assert.NotEqual(baseline, otherStation);
    }

    [Fact]
    public void Compute_ChangesWhenAnyWidthSourceDiffers()
    {
        var source = Source();
        var otherSource = new ConstantLinearPlateSectionResponse(
            new[,] { { 1100.0, 0.0, 0.0 }, { 0.0, 500.0, 0.0 }, { 0.0, 0.0, 500.0 } },
            new double[3, 3], new[,] { { 300.0, 0.0, 0.0 }, { 0.0, 100.0, 0.0 }, { 0.0, 0.0, 100.0 } },
            new[,] { { 400.0, 0.0 }, { 0.0, 400.0 } }, "other-source");
        var baseline = Compute(source, source);

        var secondPointDiffers = EquivalentSectionFingerprint.Compute(
            Analogy(), 4, "region-fp", 0.5, source, [source, otherSource],
            ReductionPolicy.ConstitutiveIntegration, 2);

        Assert.NotEqual(baseline, secondPointDiffers);
    }

    static string Compute(IPlateSectionResponse centerline, IPlateSectionResponse widthEach) =>
        EquivalentSectionFingerprint.Compute(
            Analogy(), 4, "region-fp", 0.5, centerline, [widthEach, widthEach],
            ReductionPolicy.ConstitutiveIntegration, 2);

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
