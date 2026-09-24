using System.Text.Json;
using CScore.Planar;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class StripSupportCandidateTests
{
    [Fact]
    public void SupportLocus_DefaultsToManualWithoutSources()
    {
        var locus = new SupportLocus();

        Assert.Equal(StripSupportKind.Manual, locus.Kind);
        Assert.Empty(locus.SourceReferences);
    }

    [Fact]
    public void BuildId_IsDeterministicAndDistinguishesOrdinal()
    {
        var source = new PlanarBoundarySourceReference("planar_region", "W1");

        Assert.Equal("support:wall:planar_region:W1:0",
            StripSupportCandidate.BuildId(StripSupportKind.Wall, source, 0));
        Assert.NotEqual(
            StripSupportCandidate.BuildId(StripSupportKind.Wall, source, 0),
            StripSupportCandidate.BuildId(StripSupportKind.Wall, source, 1));
    }

    [Fact]
    public void Fingerprint_DoesNotDependOnProvenance()
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 6, 6, 0], Y = [-1, -1, 1, 1] }, frame: Frame3D.Identity);
        var plain = new SupportLocus();
        var derived = new SupportLocus
        {
            Kind = StripSupportKind.Column,
            SourceReferences = [new PlanarBoundarySourceReference("fem_member", "C1", MemberId: 5)]
        };

        Assert.Equal(
            PlateStripFingerprint.Compute(region, plain, plain, 2.0),
            PlateStripFingerprint.Compute(region, derived, derived, 2.0));
    }

    [Fact]
    public void OldJsonWithoutProvenance_ReadsAsManual()
    {
        const string json = """{"Id":"s","StartSupportLocus":{"StructuralMode":0},"EndSupportLocus":{}}""";

        var strip = JsonSerializer.Deserialize<PlateStripBeamAnalogy>(json)!;

        Assert.Equal(StripSupportKind.Manual, strip.StartSupportLocus.Kind);
        Assert.Empty(strip.StartSupportLocus.SourceReferences);
    }

    [Fact]
    public void Provenance_RoundTripsThroughJson()
    {
        var strip = new PlateStripBeamAnalogy
        {
            Id = "s",
            StartSupportLocus = new SupportLocus
            {
                Kind = StripSupportKind.Wall,
                SourceReferences = [new PlanarBoundarySourceReference("planar_region", "W1", MemberId: 3)]
            }
        };

        var copy = JsonSerializer.Deserialize<PlateStripBeamAnalogy>(JsonSerializer.Serialize(strip))!;

        Assert.Equal(StripSupportKind.Wall, copy.StartSupportLocus.Kind);
        var reference = Assert.Single(copy.StartSupportLocus.SourceReferences);
        Assert.Equal("W1", reference.SourceId);
        Assert.Equal(3, reference.MemberId);
    }
}
