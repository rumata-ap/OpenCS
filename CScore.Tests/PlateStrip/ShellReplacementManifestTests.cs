using CScore.PlateStrip;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class ShellReplacementManifestTests
{
    [Fact]
    public void From_BuildsManifestFromAnalogyAndLoads()
    {
        var analogy = StripLoadMapperTests.Analogy();
        analogy.Policy = ShellReplacementPolicy.ReplaceShellRegion;
        analogy.Geometry = new PlateStripGeometry
        {
            LengthM = analogy.Geometry.LengthM,
            Polygon =
            [
                new PlanarPoint2D(0, -1),
                new PlanarPoint2D(6, -1),
                new PlanarPoint2D(6, 1),
                new PlanarPoint2D(0, 1)
            ]
        };
        var loads = new StripLoadSet(
        [
            new StripLoad { SourceTag = "dead", StationStartFraction = 0.0, StationEndFraction = 1.0 },
            new StripLoad { SourceTag = "live", StationStartFraction = 0.0, StationEndFraction = 1.0 }
        ]);

        var manifest = ShellReplacementManifest.From(analogy, loads);

        Assert.Equal("strip-1", manifest.StripId);
        Assert.Equal(10, manifest.SourceRegionId);
        Assert.Equal(ShellReplacementPolicy.ReplaceShellRegion, manifest.Policy);
        Assert.Same(analogy.Geometry.Polygon, manifest.ReplacedRegionPolygon);
        Assert.Equal(new[] { "dead", "live" }, manifest.StripLoadSourceTags);
    }

    [Fact]
    public void From_NullAnalogy_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ShellReplacementManifest.From(null!, new StripLoadSet([])));

    [Fact]
    public void From_NullStripLoads_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            ShellReplacementManifest.From(StripLoadMapperTests.Analogy(), null!));
}
