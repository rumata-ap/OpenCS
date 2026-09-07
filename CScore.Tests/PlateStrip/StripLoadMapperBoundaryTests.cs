using System.Linq;
using CScore.Planar;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

/// <summary>Срез 7, Task 6: перенос краевых действий на полосу.</summary>
public sealed class StripLoadMapperBoundaryTests
{
    const double Width = 2.0;
    const double Length = 6.0;

    [Fact]
    public void BoundaryAcrossStrip_BecomesPointLoadAtItsStation()
    {
        var boundary = Boundary(points: [new(1.5, -1.0), new(1.5, 1.0)], action: ForceAction(-10_000.0));

        var result = StripLoadMapper.Map(Frame3D.Identity, Analogy(), Load(), 1e-6, [boundary]);

        Assert.True(result.IsCalculable, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var load = result.Load!;
        Assert.Equal(StripLoadKind.Point, load.Kind);
        Assert.Equal(0.25, load.StationFraction, 9);
        // -10 000 Н/м -> -10 кН/м, на длине коридора 2 м = -20 кН.
        Assert.Equal(-20.0, load.PzKn, 9);
        Assert.Equal(0.0, load.PxKn, 12);
    }

    [Fact]
    public void SiToKilonewtonConversion_IsApplied()
    {
        var boundary = Boundary(points: [new(1.5, -1.0), new(1.5, 1.0)], action: ForceAction(-7_500.0));

        var result = StripLoadMapper.Map(Frame3D.Identity, Analogy(), Load(), 1e-6, [boundary]);

        // Без конверсии Н -> кН здесь стояло бы -15 000, то есть расхождение ровно в 1000 раз.
        Assert.Equal(-15.0, result.Load!.PzKn, 9);
    }

    [Fact]
    public void BoundaryAlongStrip_WithConstantIntensity_BecomesUniformLoad()
    {
        var boundary = Boundary(
            points: [new(1.5, 0.5), new(4.5, 0.5)], action: ForceAction(-10_000.0));

        var result = StripLoadMapper.Map(Frame3D.Identity, Analogy(), Load(), 1e-6, [boundary]);

        Assert.True(result.IsCalculable);
        var load = result.Load!;
        Assert.Equal(StripLoadKind.DistributedUniform, load.Kind);
        Assert.Equal(0.25, load.StationStartFraction, 9);
        Assert.Equal(0.75, load.StationEndFraction, 9);
        Assert.Equal(-10.0, load.QzKnM, 9);
        Assert.Equal(0.0, load.QzEndKnM, 12);
    }

    [Fact]
    public void BoundaryAlongStrip_WithVaryingIntensity_BecomesLinearLoad()
    {
        var boundary = Boundary(
            points: [new(1.5, 0.5), new(4.5, 0.5)],
            action: ForceAction(-10_000.0, -30_000.0));

        var result = StripLoadMapper.Map(Frame3D.Identity, Analogy(), Load(), 1e-6, [boundary]);

        Assert.True(result.IsCalculable);
        var load = result.Load!;
        Assert.Equal(StripLoadKind.DistributedLinear, load.Kind);
        Assert.Equal(-10.0, load.QzKnM, 9);
        Assert.Equal(-30.0, load.QzEndKnM, 9);
        Assert.Equal(-20.0, load.IntensityAt(0.5).Qz, 9);
    }

    [Fact]
    public void MissingInterface_IsAnError_AndTransfersNothing()
    {
        var result = StripLoadMapper.Map(Frame3D.Identity, Analogy(), Load(), 1e-6, []);

        Assert.False(result.IsCalculable);
        Assert.Null(result.Load);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_boundary_interface_missing");
    }

    [Fact]
    public void InterfaceOfAnotherStrip_IsNotUsed()
    {
        var foreign = Boundary(points: [new(1.5, -1.0), new(1.5, 1.0)], action: ForceAction(-10_000.0));
        var alien = new StripBoundaryInterface
        {
            Id = foreign.Id,
            StripId = "another-strip",
            BoundaryKey = foreign.BoundaryKey,
            Geometry = foreign.Geometry,
            NormalFromReplacedToRetained = foreign.NormalFromReplacedToRetained,
            ModeByDof = foreign.ModeByDof,
            ForceAction = foreign.ForceAction,
        };

        var result = StripLoadMapper.Map(Frame3D.Identity, Analogy(), Load(), 1e-6, [alien]);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_boundary_interface_missing");
    }

    [Fact]
    public void KinematicMode_IsReportedAndNotTransferred()
    {
        var boundary = Boundary(
            points: [new(1.5, -1.0), new(1.5, 1.0)],
            modes: PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.Kinematic));

        var result = StripLoadMapper.Map(Frame3D.Identity, Analogy(), Load(), 1e-6, [boundary]);

        Assert.True(result.IsCalculable);
        Assert.Null(result.Load);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_boundary_kinematic_not_transferred");
    }

    [Theory]
    [InlineData(PlanarBoundaryDofMode.PreserveSupport)]
    [InlineData(PlanarBoundaryDofMode.None)]
    [InlineData(PlanarBoundaryDofMode.Free)]
    public void NonForceModes_TransferNothingWithoutError(PlanarBoundaryDofMode mode)
    {
        var boundary = Boundary(
            points: [new(1.5, -1.0), new(1.5, 1.0)], modes: PlanarBoundaryModeByDof.All(mode));

        var result = StripLoadMapper.Map(Frame3D.Identity, Analogy(), Load(), 1e-6, [boundary]);

        Assert.True(result.IsCalculable);
        Assert.Null(result.Load);
        Assert.DoesNotContain(result.Diagnostics, d => d.IsError);
    }

    [Fact]
    public void IncompleteMode_IsAnError()
    {
        var boundary = Boundary(
            points: [new(1.5, -1.0), new(1.5, 1.0)],
            modes: PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.Incomplete));

        var result = StripLoadMapper.Map(Frame3D.Identity, Analogy(), Load(), 1e-6, [boundary]);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_boundary_mode_incomplete");
    }

    [Fact]
    public void WithoutForceAction_ConstantComponentsAreUsed()
    {
        var boundary = Boundary(points: [new(1.5, -1.0), new(1.5, 1.0)], action: null);
        var load = new PlanarLoad
        {
            Tag = "edge",
            Kind = PlanarLoadKind.Boundary,
            BoundaryKey = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 0, 1),
            Components = new PlanarVector3(0, 0, -4.0), // кН/м
        };

        var result = StripLoadMapper.Map(Frame3D.Identity, Analogy(), load, 1e-6, [boundary]);

        Assert.True(result.IsCalculable);
        Assert.Equal(-8.0, result.Load!.PzKn, 9); // -4 кН/м на 2 м коридора
    }

    /// <summary>Обратная совместимость Срезов 4–5: поверхностные и точечные нагрузки не
    /// изменились и не требуют интерфейсов.</summary>
    [Fact]
    public void SurfaceAndPointLoads_AreUnchanged()
    {
        var surface = new PlanarLoad
        {
            Tag = "q", Kind = PlanarLoadKind.Surface, Components = new PlanarVector3(0, 0, -5.0)
        };
        var point = new PlanarLoad
        {
            Tag = "p", Kind = PlanarLoadKind.Point, Components = new PlanarVector3(0, 0, -12.0),
            PointU = 3.0, PointV = 0.0
        };

        var surfaceResult = StripLoadMapper.Map(Frame3D.Identity, Analogy(), surface);
        var pointResult = StripLoadMapper.Map(Frame3D.Identity, Analogy(), point);

        Assert.True(surfaceResult.IsCalculable);
        Assert.Equal(-10.0, surfaceResult.Load!.QzKnM, 9); // -5 кН/м² × 2 м
        Assert.True(pointResult.IsCalculable);
        Assert.Equal(-12.0, pointResult.Load!.PzKn, 9);
        Assert.Equal(0.5, pointResult.Load.StationFraction, 9);
    }

    static PlanarLoad Load() => new()
    {
        Tag = "edge",
        Kind = PlanarLoadKind.Boundary,
        BoundaryKey = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 0, 1),
        Components = PlanarVector3.Zero,
    };

    static StripBoundaryInterface Boundary(
        IReadOnlyList<PlanarPoint2D> points,
        PlanarBoundaryModeByDof? modes = null,
        PlanarBoundaryForceAction? action = null) => new()
    {
        Id = "b1",
        StripId = "strip-1",
        BoundaryKey = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 0, 1),
        Geometry = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve, points),
        NormalFromReplacedToRetained = new PlanarVector3(1, 0, 0),
        ModeByDof = modes ?? PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.Force),
        ForceAction = action,
    };

    static PlanarBoundaryForceAction ForceAction(double startFz, double? endFz = null) => new()
    {
        InterfaceId = "b1",
        DofMask = PlanarDofMask.UZ,
        UnitSystem = PlanarBoundaryUnitSystem.Si,
        Samples =
        [
            new PlanarBoundaryForceSample(0.0, new PlanarVector3(0, 0, startFz), PlanarVector3.Zero),
            new PlanarBoundaryForceSample(1.0, new PlanarVector3(0, 0, endFz ?? startFz), PlanarVector3.Zero)
        ]
    };

    static PlateStripBeamAnalogy Analogy() => new()
    {
        Id = "strip-1",
        SourceRegionId = 10,
        ExplicitWidthM = Width,
        Fingerprint = "strip-fp",
        StripFrame = Frame3D.Identity,
        Geometry = new PlateStripGeometry { LengthM = Length }
    };
}
