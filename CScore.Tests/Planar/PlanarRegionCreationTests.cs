using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public class PlanarRegionCreationTests
{
    static Contour Square(double[] x, double[] y, string tag = "hull") => new()
    {
        Tag = tag,
        X = x,
        Y = y
    };

    [Fact]
    public void TryCreate_ReturnsRegionAndNoErrorsForValidHull()
    {
        var hull = Square([0, 1, 1, 0], [0, 0, 1, 1]);

        var (region, diagnostics) = PlanarRegionCreation.TryCreate(hull, [], Frame3D.Identity, "Плита 1");

        Assert.NotNull(region);
        Assert.DoesNotContain(diagnostics, d => d.IsError);
    }

    [Fact]
    public void TryCreate_ReturnsNullRegionAndErrorDiagnosticForSelfIntersectingHull()
    {
        var bowtie = Square([0, 1, 0, 1], [0, 1, 1, 0]);

        var (region, diagnostics) = PlanarRegionCreation.TryCreate(bowtie, [], Frame3D.Identity, "Плита 1");

        Assert.Null(region);
        Assert.Contains(diagnostics, d => d.IsError && d.Code == "planar_region_geometry_invalid");
    }

    [Fact]
    public void TryCreate_IncludesHoleInResultingRegion()
    {
        var hull = Square([0, 4, 4, 0], [0, 0, 4, 4]);
        var hole = Square([1, 2, 2, 1], [1, 1, 2, 2], "hole");

        var (region, _) = PlanarRegionCreation.TryCreate(hull, [hole], Frame3D.Identity, "Плита 1");

        Assert.NotNull(region);
        Assert.Single(region!.Holes);
    }

    [Fact]
    public void TryCreate_ReturnsRecoveredFrameWarning_WhenFrameIsExplicitAndNotRecovered()
    {
        var hull = Square([0, 1, 1, 0], [0, 0, 1, 1]);
        var explicitFrame = new Frame3D(
            PlanarVector3.Zero,
            new PlanarVector3(1, 0, 0),
            new PlanarVector3(0, 1, 0),
            new PlanarVector3(0, 0, 1));

        var (region, diagnostics) = PlanarRegionCreation.TryCreate(hull, [], explicitFrame, "Плита 1");

        Assert.NotNull(region);
        Assert.DoesNotContain(diagnostics, d => d.Code == "planar_region_frame_recovered");
    }
}
