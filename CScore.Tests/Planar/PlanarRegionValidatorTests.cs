using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public class PlanarRegionValidatorTests
{
    static PlanarRegion ValidRegion() => PlanarRegion.CreateFromContour(new Contour
    {
        X = [0, 1, 1, 0],
        Y = [0, 0, 1, 1]
    });

    [Fact]
    public void Validate_ReturnsRecoveredFrameWarning_WhenFrameWasAutoComputed()
    {
        var region = ValidRegion();
        var diagnostics = PlanarRegionValidator.Validate(region);

        Assert.Contains(diagnostics, d => d.Code == "planar_region_frame_recovered" && !d.IsError);
    }

    [Fact]
    public void Validate_DoesNotWarnAboutRecoveredFrame_WhenFrameWasExplicit()
    {
        var explicitFrame = new Frame3D(
            PlanarVector3.Zero,
            new PlanarVector3(1, 0, 0),
            new PlanarVector3(0, 1, 0),
            new PlanarVector3(0, 0, 1));
        var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 1, 1, 0], Y = [0, 0, 1, 1] }, frame: explicitFrame);

        var diagnostics = PlanarRegionValidator.Validate(region);

        Assert.DoesNotContain(diagnostics, d => d.Code == "planar_region_frame_recovered");
    }

    [Fact]
    public void Validate_WarnsAboutUnclassifiedBoundary_WhenNoSegmentsDefined()
    {
        var region = ValidRegion();
        var diagnostics = PlanarRegionValidator.Validate(region);

        Assert.Contains(diagnostics, d => d.Code == "planar_region_boundary_unclassified" && !d.IsError);
    }

    [Fact]
    public void Validate_DoesNotWarnAboutUnclassifiedBoundary_WhenSegmentsDefined()
    {
        var region = ValidRegion();
        region.BoundarySegments.Add(new BoundarySegment { StartVertex = 0, EndVertex = 1, Role = BoundaryRole.Support });

        var diagnostics = PlanarRegionValidator.Validate(region);

        Assert.DoesNotContain(diagnostics, d => d.Code == "planar_region_boundary_unclassified");
    }

    [Fact]
    public void Validate_ReturnsNoBlockingErrors_ForValidRegion()
    {
        var region = ValidRegion();
        var diagnostics = PlanarRegionValidator.Validate(region);

        Assert.DoesNotContain(diagnostics, d => d.IsError);
    }
}
