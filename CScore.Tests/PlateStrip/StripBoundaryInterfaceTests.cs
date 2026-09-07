using System.Linq;
using CScore.Planar;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

/// <summary>Срез 7, Task 5: граница заменяемой области полосы.</summary>
public sealed class StripBoundaryInterfaceTests
{
    const double Width = 2.0;
    const double Length = 6.0;

    [Fact]
    public void ValidInterface_ProducesNoDiagnostics()
    {
        Assert.Empty(Interface().Validate(Analogy()));
    }

    [Fact]
    public void DegenerateGeometry_IsRejected()
    {
        var single = Interface(points: [new(3.0, -1.0)]);
        var collapsed = Interface(points: [new(3.0, -1.0), new(3.0, -1.0)]);
        var nonFinite = Interface(points: [new(3.0, double.NaN), new(3.0, 1.0)]);

        Assert.Contains(single.Validate(Analogy()), d => d.Code == "plate_strip_boundary_geometry_invalid");
        Assert.Contains(collapsed.Validate(Analogy()), d => d.Code == "plate_strip_boundary_geometry_invalid");
        Assert.Contains(nonFinite.Validate(Analogy()), d => d.Code == "plate_strip_boundary_geometry_invalid");
    }

    [Fact]
    public void NonUnitOrZeroNormal_IsRejected()
    {
        Assert.Contains(Interface(normal: new(0, 0, 0)).Validate(Analogy()),
            d => d.Code == "plate_strip_boundary_normal_invalid");
        Assert.Contains(Interface(normal: new(2, 0, 0)).Validate(Analogy()),
            d => d.Code == "plate_strip_boundary_normal_invalid");
    }

    [Fact]
    public void NormalOutOfRegionPlane_IsRejected()
    {
        var diagnostics = Interface(normal: new(0, 0, 1)).Validate(Analogy());

        Assert.Contains(diagnostics, d => d.Code == "plate_strip_boundary_normal_invalid" &&
                                          d.Message.Contains("плоскости региона"));
    }

    [Fact]
    public void IncompleteMode_IsRejected()
    {
        var boundary = Interface(modes: PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.Incomplete));

        Assert.Contains(boundary.Validate(Analogy()), d => d.Code == "plate_strip_boundary_mode_incomplete");
    }

    [Fact]
    public void ForceDofWithoutForceAction_IsRejected()
    {
        var boundary = Interface(
            modes: PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.Force), forceAction: null);

        Assert.Contains(boundary.Validate(Analogy()), d => d.Code == "plate_strip_boundary_force_action_missing");
    }

    [Fact]
    public void ForceDofWithForceAction_IsAccepted()
    {
        var boundary = Interface(
            modes: PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.Force), forceAction: ForceAction());

        Assert.DoesNotContain(boundary.Validate(Analogy()),
            d => d.Code == "plate_strip_boundary_force_action_missing");
    }

    [Fact]
    public void PreserveSupportWithoutExplicitSupport_IsNotDiagnosed()
    {
        // Сознательное решение Среза 7: проверка нереализуема (SupportLocus никогда не null,
        // BeamJunctionMode.Support — значение по умолчанию) и вводила бы автовывод опорной
        // схемы, оставленный за границей объёма.
        var boundary = Interface(modes: PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.PreserveSupport));

        Assert.Empty(boundary.Validate(Analogy()));
    }

    [Fact]
    public void BoundaryAcrossStrip_ProjectsToItsStation()
    {
        var boundary = Interface(points: [new(1.5, -2.0), new(1.5, 2.0)]);

        Assert.True(boundary.TryProjectToStation(Frame3D.Identity, Analogy(), out double station));
        Assert.Equal(0.25, station, 9);
    }

    [Fact]
    public void BoundaryAlongStrip_DoesNotProject()
    {
        var boundary = Interface(points: [new(1.0, 0.5), new(5.0, 0.5)]);

        Assert.False(boundary.TryProjectToStation(Frame3D.Identity, Analogy(), out _));
    }

    [Fact]
    public void BoundaryOutsideCorridor_DoesNotProject()
    {
        // Пересекает ось далеко за концом полосы.
        var boundary = Interface(points: [new(9.0, -2.0), new(9.0, 2.0)]);

        Assert.False(boundary.TryProjectToStation(Frame3D.Identity, Analogy(), out _));
    }

    [Fact]
    public void RotatedRegionFrame_GivesSameStation()
    {
        // Регион повёрнут на 90° вокруг Z, полоса задана в тех же глобальных координатах:
        // станция обязана определяться геометрией, а не системой отсчёта региона.
        var rotated = new Frame3D(
            new PlanarVector3(0, 0, 0),
            new PlanarVector3(0, 1, 0),
            new PlanarVector3(-1, 0, 0),
            new PlanarVector3(0, 0, 1));
        var boundary = Interface(points: [new(1.5, -2.0), new(1.5, 2.0)]);
        var analogy = Analogy(stripFrame: new Frame3D(
            new PlanarVector3(0, 0, 0),
            new PlanarVector3(0, 1, 0),
            new PlanarVector3(-1, 0, 0),
            new PlanarVector3(0, 0, 1)));

        Assert.True(boundary.TryProjectToStation(rotated, analogy, out double station));
        Assert.Equal(0.25, station, 9);
    }

    [Fact]
    public void Validate_RequiresAnalogy()
    {
        Assert.Throws<ArgumentNullException>(() => Interface().Validate(null!));
        Assert.Throws<ArgumentNullException>(
            () => Interface().TryProjectToStation(Frame3D.Identity, null!, out _));
    }

    static StripBoundaryInterface Interface(
        IReadOnlyList<PlanarPoint2D>? points = null,
        PlanarVector3? normal = null,
        PlanarBoundaryModeByDof? modes = null,
        PlanarBoundaryForceAction? forceAction = null) => new()
    {
        Id = "b1",
        StripId = "strip-1",
        Geometry = new PlanarConstraintGeometry(
            PlanarConstraintGeometryKind.Curve,
            points ?? [new PlanarPoint2D(3.0, -2.0), new PlanarPoint2D(3.0, 2.0)]),
        NormalFromReplacedToRetained = normal ?? new PlanarVector3(1, 0, 0),
        ModeByDof = modes ?? PlanarBoundaryModeByDof.None,
        ForceAction = forceAction,
    };

    static PlanarBoundaryForceAction ForceAction() => new()
    {
        InterfaceId = "b1",
        DofMask = PlanarDofMask.UZ,
        Samples =
        [
            new PlanarBoundaryForceSample(0.0, new PlanarVector3(0, 0, -10_000), PlanarVector3.Zero),
            new PlanarBoundaryForceSample(1.0, new PlanarVector3(0, 0, -10_000), PlanarVector3.Zero)
        ]
    };

    static PlateStripBeamAnalogy Analogy(Frame3D? stripFrame = null) => new()
    {
        Id = "strip-1",
        SourceRegionId = 10,
        ExplicitWidthM = Width,
        Fingerprint = "strip-fp",
        StripFrame = stripFrame ?? Frame3D.Identity,
        Geometry = new PlateStripGeometry { LengthM = Length }
    };
}
