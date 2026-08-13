using CScore.PlateStrip;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class StripLoadMapperTests
{
    [Fact]
    public void Map_SurfaceLocal_ScalesByExplicitWidth()
    {
        var load = new PlanarLoad
        {
            Tag = "dead",
            Kind = PlanarLoadKind.Surface,
            CoordinateSystem = PlanarLoadCoordinateSystem.Local,
            Components = new PlanarVector3(0.0, 0.0, -5.0)
        };

        var result = StripLoadMapper.Map(Frame3D.Identity, Analogy(), load);

        Assert.True(result.IsCalculable);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(StripLoadKind.DistributedUniform, result.Load!.Kind);
        Assert.Equal(0.0, result.Load.StationStartFraction);
        Assert.Equal(1.0, result.Load.StationEndFraction);
        Assert.Equal(0.0, result.Load.QxKnM, 9);
        Assert.Equal(0.0, result.Load.QyKnM, 9);
        Assert.Equal(-10.0, result.Load.QzKnM, 9); // -5 кН/м² * 2 м ширины
    }

    [Fact]
    public void Map_SurfaceGlobal_TransformsIntoStripFrame()
    {
        var analogy = Analogy();
        analogy.StripFrame = new Frame3D(
            PlanarVector3.Zero,
            new PlanarVector3(0, 1, 0),
            new PlanarVector3(-1, 0, 0),
            new PlanarVector3(0, 0, 1));
        var load = new PlanarLoad
        {
            Tag = "wind",
            Kind = PlanarLoadKind.Surface,
            CoordinateSystem = PlanarLoadCoordinateSystem.Global,
            Components = new PlanarVector3(3.0, 0.0, 0.0)
        };

        var result = StripLoadMapper.Map(Frame3D.Identity, analogy, load);

        Assert.True(result.IsCalculable);
        // Global X проецируется на StripFrame.LocalY (=(-1,0,0)) -> компонента -3, не на LocalX.
        Assert.Equal(0.0, result.Load!.QxKnM, 9);
        Assert.Equal(-6.0, result.Load.QyKnM, 9); // -3 кН/м² * 2 м
    }

    [Fact]
    public void Map_BoundaryKind_ReturnsUnsupportedDiagnostic()
    {
        var load = new PlanarLoad
        {
            Tag = "edge",
            Kind = PlanarLoadKind.Boundary,
            BoundaryKey = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 0, 1),
            Components = new PlanarVector3(1.0, 0.0, 0.0)
        };

        var result = StripLoadMapper.Map(Frame3D.Identity, Analogy(), load);

        Assert.False(result.IsCalculable);
        Assert.Null(result.Load);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_load_kind_unsupported");
    }

    [Fact]
    public void Map_DegenerateGeometry_ReturnsInvalidGeometryDiagnostic()
    {
        var analogy = Analogy();
        analogy.Geometry = new PlateStripGeometry { LengthM = 0.0 };
        var load = new PlanarLoad
        {
            Tag = "dead",
            Kind = PlanarLoadKind.Surface,
            Components = new PlanarVector3(0, 0, -1.0)
        };

        var result = StripLoadMapper.Map(Frame3D.Identity, analogy, load);

        Assert.False(result.IsCalculable);
        Assert.Null(result.Load);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_load_invalid_geometry");
    }

    [Fact]
    public void Map_InvalidPlanarLoad_ReturnsInvalidInputDiagnostic()
    {
        var load = new PlanarLoad
        {
            Tag = "bad",
            Kind = PlanarLoadKind.Surface,
            Components = new PlanarVector3(double.NaN, 0, 0)
        };

        var result = StripLoadMapper.Map(Frame3D.Identity, Analogy(), load);

        Assert.False(result.IsCalculable);
        Assert.Null(result.Load);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_load_invalid_input");
    }

    internal static PlateStripBeamAnalogy Analogy(double width = 2.0, double lengthM = 6.0) => new()
    {
        Id = "strip-1",
        SourceRegionId = 10,
        ExplicitWidthM = width,
        Fingerprint = "strip-fp",
        Geometry = new PlateStripGeometry { LengthM = lengthM }
    };
}
