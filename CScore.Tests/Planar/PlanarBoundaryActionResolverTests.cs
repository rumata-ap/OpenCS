using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public sealed class PlanarBoundaryActionResolverTests
{
    [Fact]
    public void CombinedModeAcceptsDisjointDofs()
    {
        var result = Resolve(
            PlanarBoundaryActionSourceMode.Combined,
            ParentForce(PlanarDofMask.UX),
            TemplateKinematic(PlanarDofMask.UZ));

        Assert.True(result.IsCalculable, Diagnostics(result));
        Assert.Equal(PlanarDofMask.UX | PlanarDofMask.UZ, result.CoveredDofs);
    }

    [Theory]
    [InlineData(PlanarBoundaryActionKind.Force, PlanarBoundaryActionKind.Force)]
    [InlineData(PlanarBoundaryActionKind.Kinematic, PlanarBoundaryActionKind.Kinematic)]
    [InlineData(PlanarBoundaryActionKind.Force, PlanarBoundaryActionKind.Kinematic)]
    public void CombinedModeRejectsOverlappingDofs(
        PlanarBoundaryActionKind first,
        PlanarBoundaryActionKind second)
    {
        var result = Resolve(
            PlanarBoundaryActionSourceMode.Combined,
            ActionFrom("parent", first, PlanarDofMask.UX),
            ActionFrom("template", second, PlanarDofMask.UX));

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "planar_boundary_source_dof_conflict");
    }

    [Fact]
    public void TemplateProviderDoesNotTreatMissingDofAsZero()
    {
        var template = new PlanarBoundaryActionSet
        {
            SourceMode = PlanarBoundaryActionSourceMode.Template,
            ForceActions = [ForceAction("top", PlanarDofMask.UX)]
        };

        var result = new PlanarBoundaryTemplateProvider(template).Resolve(Request(
            PlanarBoundaryActionSourceMode.Template,
            PlanarDofMask.UX | PlanarDofMask.UZ));

        Assert.Equal(PlanarDofMask.UX, result.CoveredDofs);
        Assert.Equal(PlanarDofMask.None, result.CoveredDofs & PlanarDofMask.UZ);
    }

    [Fact]
    public void ResolverConvertsForceToTargetFrame()
    {
        var targetFrame = new Frame3D(
            PlanarVector3.Zero,
            new(0, 1, 0),
            new(-1, 0, 0),
            new(0, 0, 1));
        var source = new PlanarBoundaryActionSet
        {
            SourceMode = PlanarBoundaryActionSourceMode.Template,
            ForceActions =
            [
                new PlanarBoundaryForceAction
                {
                    InterfaceId = "top",
                    DofMask = PlanarDofMask.UX,
                    Frame = Frame3D.Identity,
                    Samples = [new(0, new(0, 1, 0), PlanarVector3.Zero)]
                }
            ]
        };

        var result = new PlanarBoundaryTemplateProvider(source).Resolve(Request(
            PlanarBoundaryActionSourceMode.Template,
            PlanarDofMask.UX,
            targetFrame));

        Assert.True(result.IsCalculable, Diagnostics(result));
        Assert.Equal(1, Assert.Single(result.ForceActions).Samples[0].ForcePerLength.X, 10);
        Assert.Equal(0, Assert.Single(result.ForceActions).Samples[0].ForcePerLength.Y, 10);
    }

    [Fact]
    public void ResolverRejectsMissingParentResult()
    {
        var result = new PlanarBoundaryActionResolver().Resolve(
            Request(PlanarBoundaryActionSourceMode.Parent, PlanarDofMask.UX) with
            {
                Scenario = new PlanarBoundarySourceScenario("parent", null, null, null, false, true)
            },
            new FixedProvider(ParentForce(PlanarDofMask.UX)),
            null);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "planar_boundary_parent_result_missing");
    }

    [Fact]
    public void ResolverRejectsNonConvergedParentStep()
    {
        var result = new PlanarBoundaryActionResolver().Resolve(
            Request(PlanarBoundaryActionSourceMode.Parent, PlanarDofMask.UX) with
            {
                Scenario = new PlanarBoundarySourceScenario("parent", "r1", 0, 3, true, false)
            },
            new FixedProvider(ParentForce(PlanarDofMask.UX)),
            null);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "planar_boundary_parent_step_not_converged");
    }

    [Fact]
    public void FingerprintIsDeterministicAndChangesForSampleValues()
    {
        var result = TemplateKinematic(PlanarDofMask.UZ);
        var cut = Interface();

        var first = PlanarBoundaryActionFingerprint.Compute(
            new PlanarBoundaryActionProviderResult
            {
                SourceMode = result.SourceMode,
                KinematicActions = result.KinematicActions
            },
            cut);
        var same = PlanarBoundaryActionFingerprint.Compute(
            new PlanarBoundaryActionProviderResult
            {
                SourceMode = result.SourceMode,
                KinematicActions = result.KinematicActions
            },
            cut);
        var changed = PlanarBoundaryActionFingerprint.Compute(
            new PlanarBoundaryActionProviderResult
            {
                SourceMode = result.SourceMode,
                KinematicActions =
                [
                    new PlanarBoundaryKinematicAction
                    {
                        InterfaceId = "top",
                        DofMask = PlanarDofMask.UZ,
                        Samples = [new(0, new(0, 0, 0.02), PlanarVector3.Zero)]
                    }
                ]
            },
            cut);

        Assert.Equal(first, same);
        Assert.NotEqual(first, changed);
    }

    static PlanarBoundaryActionSet ParentForce(PlanarDofMask mask) =>
        new() { SourceMode = PlanarBoundaryActionSourceMode.Parent, ForceActions = [ForceAction("top", mask)] };

    static PlanarBoundaryActionSet TemplateKinematic(PlanarDofMask mask) =>
        new() { SourceMode = PlanarBoundaryActionSourceMode.Template, KinematicActions = [KinematicAction("top", mask)] };

    static PlanarBoundaryActionSet ActionFrom(string source, PlanarBoundaryActionKind kind, PlanarDofMask mask) =>
        kind == PlanarBoundaryActionKind.Force
            ? new() { SourceMode = source == "parent" ? PlanarBoundaryActionSourceMode.Parent : PlanarBoundaryActionSourceMode.Template, ForceActions = [ForceAction("top", mask)] }
            : new() { SourceMode = source == "parent" ? PlanarBoundaryActionSourceMode.Parent : PlanarBoundaryActionSourceMode.Template, KinematicActions = [KinematicAction("top", mask)] };

    static PlanarBoundaryForceAction ForceAction(string interfaceId, PlanarDofMask mask) => new()
    {
        InterfaceId = interfaceId,
        DofMask = mask,
        Samples = [new(0, new(1, 0, 0), PlanarVector3.Zero)]
    };

    static PlanarBoundaryKinematicAction KinematicAction(string interfaceId, PlanarDofMask mask) => new()
    {
        InterfaceId = interfaceId,
        DofMask = mask,
        Samples = [new(0, new(0.01, 0, 0), PlanarVector3.Zero)]
    };

    static PlanarBoundaryActionRequest Request(
        PlanarBoundaryActionSourceMode mode,
        PlanarDofMask required,
        Frame3D? frame = null) => new()
    {
        Interface = Interface(),
        SourceMode = mode,
        RequestedKind = null,
        RequiredDofs = required,
        TargetFrame = frame ?? Frame3D.Identity,
        Scenario = new PlanarBoundarySourceScenario("parent", "r1", 0, 0, true, true)
    };

    static PlanarBoundaryActionProviderResult Resolve(
        PlanarBoundaryActionSourceMode mode,
        PlanarBoundaryActionSet parent,
        PlanarBoundaryActionSet template) =>
        new PlanarBoundaryActionResolver().Resolve(
            Request(mode, parent.CoveredDofs | template.CoveredDofs),
            new FixedProvider(parent),
            new FixedProvider(template));

    static PlanarCutInterface Interface() => new()
    {
        Id = "top",
        Geometry = new PlanarConstraintGeometry(
            PlanarConstraintGeometryKind.Curve,
            [new(0, 1), new(2, 1)]),
        NormalFromFragmentToOmittedSide = new(0, 1, 0),
        ModeByDof = PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.Force)
    };

    static string Diagnostics(PlanarBoundaryActionProviderResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message));

    sealed class FixedProvider(PlanarBoundaryActionSet source) : IPlanarBoundaryActionProvider
    {
        public PlanarBoundaryActionProviderResult Resolve(PlanarBoundaryActionRequest request) => new()
        {
            SourceMode = source.SourceMode,
            ForceActions = source.ForceActions,
            KinematicActions = source.KinematicActions,
            SourceReferences = source.SourceReferences,
            Diagnostics = source.Diagnostics
        };
    }
}
