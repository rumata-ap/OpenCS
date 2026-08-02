using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public sealed class PlanarBoundaryActionTests
{
    [Fact]
    public void CutInterface_ValidatesCurveNormalAndUniqueId()
    {
        var cut = new PlanarCutInterface
        {
            Id = "top",
            Geometry = new PlanarConstraintGeometry(
                PlanarConstraintGeometryKind.Curve,
                [new(0, 1), new(2, 1)]),
            NormalFromFragmentToOmittedSide = new(0, 1, 0),
            ModeByDof = PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.Free)
        };

        Assert.Empty(cut.Validate());
    }

    [Fact]
    public void ModeByDofReturnsExplicitModeForEachDof()
    {
        var modes = PlanarBoundaryModeByDof.None
            .With(PlanarDofMask.UX, PlanarBoundaryDofMode.Force);

        Assert.Equal(PlanarBoundaryDofMode.Force, modes.Get(PlanarDofMask.UX));
    }

    [Fact]
    public void ForceSampleRequiresOrderedNormalizedS()
    {
        var action = new PlanarBoundaryForceAction
        {
            InterfaceId = "top",
            Samples =
            [
                new(0.75, new(1, 0, 0), PlanarVector3.Zero),
                new(0.25, new(1, 0, 0), PlanarVector3.Zero)
            ]
        };

        var diagnostics = action.Validate();

        Assert.Contains(diagnostics, d => d.Code == "planar_boundary_samples_not_ordered");
    }

    [Fact]
    public void ForceSampleRejectsOutOfRangeS()
    {
        var action = new PlanarBoundaryForceAction
        {
            InterfaceId = "top",
            Samples = [new(-0.1, new(1, 0, 0), PlanarVector3.Zero)]
        };

        var diagnostics = action.Validate();

        Assert.Contains(diagnostics, d => d.Code == "planar_boundary_sample_s_invalid");
    }

    [Fact]
    public void KinematicActionRejectsNonFiniteSample()
    {
        var action = new PlanarBoundaryKinematicAction
        {
            InterfaceId = "top",
            Samples = [new(0, new(double.NaN, 0, 0), PlanarVector3.Zero)]
        };

        var diagnostics = action.Validate();

        Assert.Contains(diagnostics, d => d.Code == "planar_boundary_sample_vector_invalid");
    }
}
