using CScore.Planar;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class StripKinematicMapperTests
{
    const double Length = 6.0;
    static readonly IReadOnlyList<double> Stations = [0.0, 0.25, 0.5, 0.75, 1.0];

    static PlateStripBeamAnalogy Analogy(Frame3D? stripFrame = null) => new()
    {
        Id = "strip",
        ExplicitWidthM = 2.0,
        StripFrame = stripFrame ?? Frame3D.Identity,
        Geometry = new PlateStripGeometry { LengthM = Length }
    };

    static StripBoundaryInterface Boundary(
        PlanarBoundaryModeByDof modes,
        IReadOnlyList<PlanarBoundaryKinematicSample> samples,
        IReadOnlyList<PlanarPoint2D>? points = null,
        string id = "b1") => new()
    {
        Id = id,
        StripId = "strip",
        Geometry = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve,
            points ?? [new(4.5, -1.0), new(4.5, 1.0)]),
        NormalFromReplacedToRetained = new PlanarVector3(1, 0, 0),
        ModeByDof = modes,
        KinematicAction = new PlanarBoundaryKinematicAction
        {
            InterfaceId = id,
            DofMask = modes.CoveredDofs,
            Samples = samples
        }
    };

    static PlanarBoundaryKinematicSample Sample(double s, PlanarVector3 d, PlanarVector3? r = null) =>
        new(s, d, r ?? PlanarVector3.Zero);

    static PlanarBoundaryModeByDof Kinematic(PlanarDofMask mask) =>
        PlanarBoundaryModeByDof.None.With(mask, PlanarBoundaryDofMode.Kinematic);

    [Fact]
    public void SettlementAcrossStripAtNode_IsTransferredAsW()
    {
        var boundary = Boundary(Kinematic(PlanarDofMask.UZ), [Sample(0, new(0, 0, -0.01))]);

        var result = StripKinematicMapper.Map(Frame3D.Identity, Analogy(), boundary, Stations);

        Assert.True(result.IsCalculable);
        Assert.Equal([new StripPrescribedDisplacement(3, 2, -0.01)], result.Values);
    }

    [Fact]
    public void RotationRy_IsTransferredAsThetaY()
    {
        var boundary = Boundary(Kinematic(PlanarDofMask.RY), [Sample(0, PlanarVector3.Zero, new(0, 0.002, 0))]);

        var result = StripKinematicMapper.Map(Frame3D.Identity, Analogy(), boundary, Stations);

        var value = Assert.Single(result.Values);
        Assert.Equal((3, 3), (value.NodeIndex, value.Dof));
        Assert.Equal(0.002, value.Value, 12);
    }

    [Fact]
    public void StripRotatedToRegion_ProjectsDisplacementOnStripAxes()
    {
        // Полоса повёрнута на 30° в плоскости региона; граница поперёк оси в середине пролёта.
        double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6);
        var stripFrame = new Frame3D(PlanarVector3.Zero, new(c, s, 0), new(-s, c, 0), new(0, 0, 1));
        var mid = new PlanarVector3(3 * c, 3 * s, 0);
        var across = new PlanarVector3(-s, c, 0);
        var points = new[] { mid - across, mid + across }.Select(p => new PlanarPoint2D(p.X, p.Y)).ToList();
        var boundary = Boundary(Kinematic(PlanarDofMask.UX | PlanarDofMask.UY),
            [Sample(0, new(0.01, 0, 0))], points);

        var result = StripKinematicMapper.Map(Frame3D.Identity, Analogy(stripFrame), boundary, Stations);

        Assert.True(result.IsCalculable, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.Equal(0.01 * c, result.Values.Single(v => v.Dof == 0).Value, 12);
        Assert.Equal(-0.01 * s, result.Values.Single(v => v.Dof == 1).Value, 12);
        Assert.All(result.Values, v => Assert.Equal(2, v.NodeIndex));
    }

    [Fact]
    public void RotatedStripWithOnlyUxKinematic_IsMixedMode()
    {
        double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6);
        var stripFrame = new Frame3D(PlanarVector3.Zero, new(c, s, 0), new(-s, c, 0), new(0, 0, 1));
        var mid = new PlanarVector3(3 * c, 3 * s, 0);
        var across = new PlanarVector3(-s, c, 0);
        var points = new[] { mid - across, mid + across }.Select(p => new PlanarPoint2D(p.X, p.Y)).ToList();
        var boundary = Boundary(Kinematic(PlanarDofMask.UX), [Sample(0, new(0.01, 0, 0))], points);

        var result = StripKinematicMapper.Map(Frame3D.Identity, Analogy(stripFrame), boundary, Stations);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_kinematic_mode_mixed");
    }

    [Fact]
    public void BoundaryAlongStrip_IsRejected()
    {
        var boundary = Boundary(Kinematic(PlanarDofMask.UZ), [Sample(0, new(0, 0, -0.01))],
            [new(1.0, 0.5), new(5.0, 0.5)]);

        var result = StripKinematicMapper.Map(Frame3D.Identity, Analogy(), boundary, Stations);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_kinematic_along_strip_unsupported");
    }

    [Fact]
    public void CrossingBetweenNodes_IsRejected()
    {
        var boundary = Boundary(Kinematic(PlanarDofMask.UZ), [Sample(0, new(0, 0, -0.01))],
            [new(4.0, -1.0), new(4.0, 1.0)]);

        var result = StripKinematicMapper.Map(Frame3D.Identity, Analogy(), boundary, Stations);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_kinematic_station_not_node");
    }

    [Fact]
    public void Samples_AreInterpolatedByArcLengthAtCrossing()
    {
        // Граница от (4.5, −1) до (4.5, 3): ось пересекается на четверти длины дуги.
        var boundary = Boundary(Kinematic(PlanarDofMask.UZ),
            [Sample(0, new(0, 0, 0)), Sample(1, new(0, 0, -0.04))],
            [new(4.5, -1.0), new(4.5, 3.0)]);

        var result = StripKinematicMapper.Map(Frame3D.Identity, Analogy(), boundary, Stations);

        Assert.Equal(-0.01, Assert.Single(result.Values).Value, 12);
    }

    [Fact]
    public void RotationAboutStripAxis_WarnsTorsionNotTransferred()
    {
        var boundary = Boundary(Kinematic(PlanarDofMask.RX), [Sample(0, PlanarVector3.Zero, new(0.001, 0, 0))]);

        var result = StripKinematicMapper.Map(Frame3D.Identity, Analogy(), boundary, Stations);

        Assert.True(result.IsCalculable);
        Assert.Empty(result.Values);
        var warning = Assert.Single(result.Diagnostics);
        Assert.Equal("plate_strip_kinematic_torsion_not_transferred", warning.Code);
        Assert.False(warning.IsError);
    }

    [Fact]
    public void KinematicDofWithoutAction_IsRejected()
    {
        var boundary = new StripBoundaryInterface
        {
            Id = "b1",
            Geometry = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve, [new(4.5, -1), new(4.5, 1)]),
            NormalFromReplacedToRetained = new PlanarVector3(1, 0, 0),
            ModeByDof = Kinematic(PlanarDofMask.UZ)
        };

        var result = StripKinematicMapper.Map(Frame3D.Identity, Analogy(), boundary, Stations);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_boundary_kinematic_action_missing");
    }

    [Fact]
    public void NoKinematicDofs_GivesNothing()
    {
        var boundary = Boundary(PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.PreserveSupport), []);

        var result = StripKinematicMapper.Map(Frame3D.Identity, Analogy(), boundary, Stations);

        Assert.True(result.IsCalculable);
        Assert.Empty(result.Values);
    }

    [Fact]
    public void MapAll_EqualValuesAtSameDof_AreMerged()
    {
        var a = Boundary(Kinematic(PlanarDofMask.UZ), [Sample(0, new(0, 0, -0.01))], id: "a");
        var b = Boundary(Kinematic(PlanarDofMask.UZ), [Sample(0, new(0, 0, -0.01))], id: "b");

        var result = StripKinematicMapper.MapAll(Frame3D.Identity, Analogy(), [a, b], Stations);

        Assert.True(result.IsCalculable);
        Assert.Single(result.Values);
    }

    [Fact]
    public void MapAll_DifferentValuesAtSameDof_Conflict()
    {
        var a = Boundary(Kinematic(PlanarDofMask.UZ), [Sample(0, new(0, 0, -0.01))], id: "a");
        var b = Boundary(Kinematic(PlanarDofMask.UZ), [Sample(0, new(0, 0, -0.02))], id: "b");

        var result = StripKinematicMapper.MapAll(Frame3D.Identity, Analogy(), [a, b], Stations);

        Assert.False(result.IsCalculable);
        Assert.Empty(result.Values);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_kinematic_conflict" &&
                                                 d.Message.Contains("«a»") && d.Message.Contains("«b»"));
    }
}
