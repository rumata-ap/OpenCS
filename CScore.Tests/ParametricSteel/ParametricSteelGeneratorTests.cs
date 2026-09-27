using Xunit;
using CScore.ParametricSteel;
using CScore.Sp16;
using static CScore.Tests.ParametricSteel.ParametricSteelTestData;
using D = CScore.ParametricSteel.ParametricSteelSectionDefinition;

namespace CScore.Tests.ParametricSteel;

/// <summary>Построение сечения, ориентация, диагностика и отпечаток.</summary>
public class ParametricSteelGeneratorTests
{
    [Fact]
    public void GeneratesSingleSteelRegionCenteredAtCentroid()
    {
        var r = Generate(D.RolledAngle(0.1, 0.063, 0.008, 0.01, 0.0033) with { MaterialId = 7, Tag = "L100×63×8" });
        var area = Assert.Single(r.Section.Areas);
        Assert.Equal(AreaCategory.Region, area.Category);
        Assert.Equal(7, area.MaterialId);
        Assert.Equal("L100×63×8", r.Section.Tag);
        Assert.Same(area, Assert.Single(r.GeneratedAreas));
        var poly = Polygon(r.Section);
        Assert.Equal(0, poly.Xc, 9);
        Assert.Equal(0, poly.Yc, 9);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void HullIsCounterClockwiseAndHoleClockwise(bool rotated, bool flipped)
    {
        var box = Generate(D.BentBox(0.2, 0.1, 0.006, 0.006) with { Rotated90 = rotated }).Section.Areas[0];
        Assert.True(SignedArea(box.Hull!) > 0);
        Assert.True(SignedArea(Assert.Single(box.Holes)) < 0);
        var ch = Generate(D.RolledChannel(0.2, 0.076, 0.0052, 0.009, 0.0095, 0.0055) with { Rotated90 = rotated, Flipped = flipped }).Section.Areas[0];
        Assert.True(SignedArea(ch.Hull!) > 0);
    }

    [Fact]
    public void RotatedIBeamHasWebHorizontal()
    {
        var poly = Polygon(Generate(D.WeldedIBeam(0.4, 0.2, 0.014, 0, 0, 0.008) with { Rotated90 = true }).Section);
        Assert.Equal(0.4, poly.XMax - poly.XMin, 9);
        Assert.Equal(0.2, poly.YMax - poly.YMin, 9);
        Assert.True(poly.Iy > poly.Ix);
    }

    [Fact]
    public void FlippedChannelHasWebOnTheRight()
    {
        var poly = Polygon(Generate(D.WeldedChannel(0.2, 0.08, 0.006, 0.01) with { Flipped = true }).Section);
        // Центр тяжести ближе к стенке: справа от середины габарита.
        Assert.True(poly.Xc > (poly.XMin + poly.XMax) / 2);
    }

    public static TheoryData<D> Invalid() =>
    [
        D.RolledIBeam(0.2, 0.1, 0.0052, 0, 0.0095),
        D.WeldedIBeam(0.2, 0.1, 0.11, 0, 0, 0.006),
        D.RolledIBeam(0.2, 0.1, 0.0052, 0.0084, 0.0095, 0.004, 0.5),
        D.RolledIBeam(0.2, 0.1, 0.0052, 0.0084, 0.2),
        D.WeldedChannel(0.2, 0.005, 0.006, 0.01),
        D.BentBox(0.1, 0.1, 0.006, 0.05),
        D.Pipe(0.1, 0.05),
        D.Plate(0, 0.1),
        D.RoundBar(double.NaN),
        D.WeldedTee(0.2, 0.15, 0.01, 0.014) with { Fabrication = SteelFabrication.Rolled },
        D.Pipe(0.159, 0.006) with { Rotated90 = true },
        D.WeldedIBeam(0.3, 0.15, 0.012, 0, 0, 0.008) with { Flipped = true },
        D.RolledAngle(0.1, 0.1, 0.008, 0.012) with { Rotated90 = true },
        new D { Kind = SteelProfileKind.Generic },
    ];

    [Theory]
    [MemberData(nameof(Invalid))]
    public void InvalidDefinitionsProduceDiagnosticsWithoutGeometry(D definition)
    {
        var r = ParametricSteelSectionGenerator.Generate(definition);
        Assert.NotEmpty(r.Diagnostics);
        Assert.Empty(r.Section.Areas);
        Assert.Null(r.Profile);
    }

    [Fact]
    public void FingerprintIsStableAndSensitive()
    {
        var d = D.RolledIBeam(0.298, 0.149, 0.0055, 0.008, 0.013) with { MaterialId = 3 };
        var a = Generate(d).Section;
        string fa = ParametricSteelSectionFingerprint.Compute(a, ParametricSteelSectionGenerator.GeneratorVersion);
        Assert.Equal(fa, ParametricSteelSectionFingerprint.Compute(Generate(d).Section, ParametricSteelSectionGenerator.GeneratorVersion));
        Assert.NotEqual(fa, ParametricSteelSectionFingerprint.Compute(Generate(d with { MaterialId = 4 }).Section, ParametricSteelSectionGenerator.GeneratorVersion));
        Assert.NotEqual(fa, ParametricSteelSectionFingerprint.Compute(a, ParametricSteelSectionGenerator.GeneratorVersion + 1));
        a.Areas[0].Hull!.X[3] += 1e-4;
        Assert.NotEqual(fa, ParametricSteelSectionFingerprint.Compute(a, ParametricSteelSectionGenerator.GeneratorVersion));
    }

    [Fact]
    public void BindingReturnsProfileOnlyForUnchangedGeometry()
    {
        var r = Generate(D.BentChannel(0.2, 0.08, 0.006, 0.009) with { MaterialId = 1 });
        var section = r.Section;
        Assert.Null(section.TryGetParametricSteelProfile());
        section.ParametricSteel = new ParametricSteelBinding(r.Profile!,
            ParametricSteelSectionFingerprint.Compute(section, ParametricSteelSectionGenerator.GeneratorVersion),
            ParametricSteelSectionGenerator.GeneratorVersion);
        Assert.Same(r.Profile, section.TryGetParametricSteelProfile());
        section.Areas[0].MaterialId = 2;
        Assert.Null(section.TryGetParametricSteelProfile());
    }

    static double SignedArea(Contour c)
    {
        double a = 0;
        for (int i = 0; i + 1 < c.X.Count; i++) a += c.X[i] * c.Y[i + 1] - c.X[i + 1] * c.Y[i];
        return a / 2;
    }
}
