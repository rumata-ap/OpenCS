using CScore.Fem;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Submodel;

public sealed class BoundaryControlAndModesTests
{
    static EndActions End(Dof6? boundary, Dof6? retained, Dof6? reaction = null, Dof6? displacement = null,
        IReadOnlyList<string>? unsupported = null) =>
        new(true, "102", boundary, [], retained, reaction, displacement, unsupported ?? [], []);

    [Fact]
    public void Control_ConsistentFreeEnd_Passes()
    {
        var p = new Dof6(1000, -50, 20, 3, -400, 7);
        var diagnostics = new List<FemValidationDiagnostic>();

        var check = BoundaryControlCheck.Compute(End(p, p + new Dof6(1e-8, 0, 0, 0, 1e-8, 0)), 0, diagnostics);

        Assert.True(check.Available);
        Assert.True(check.Passed);
        Assert.DoesNotContain(diagnostics, d => d.Code == BoundaryScenarioDiagnostics.ControlMismatch);
    }

    [Fact]
    public void Control_InvertedLocalForceSign_IsMismatch()
    {
        // Характерный признак ошибки конвенции localForce: p_boundary ≈ −p_control.
        var p = new Dof6(1000, -50, 20, 3, -400, 7);
        var diagnostics = new List<FemValidationDiagnostic>();

        var check = BoundaryControlCheck.Compute(End(p, -p), 0, diagnostics);

        Assert.False(check.Passed);
        Assert.Equal(2000, check.ForceMismatch, 9);
        Assert.Equal(800, check.MomentMismatch, 9);
        var warning = Assert.Single(diagnostics, d => d.Code == BoundaryScenarioDiagnostics.ControlMismatch);
        Assert.False(warning.IsError);
    }

    [Fact]
    public void Control_SupportedDof_AddsParentReaction()
    {
        // Опора по Uz: p_boundary + R = p_control.
        var boundary = new Dof6(0, 0, -30, 0, 0, 0);
        var control = new Dof6(0, 0, 70, 0, 0, 0);
        var reaction = new Dof6(0, 0, 100, 0, 0, 0);

        var check = BoundaryControlCheck.Compute(End(boundary, control, reaction), 1 << 2, []);

        Assert.True(check.Passed);
    }

    [Fact]
    public void Control_Unavailable_WithoutRetainedForces()
    {
        var diagnostics = new List<FemValidationDiagnostic>();
        var check = BoundaryControlCheck.Compute(End(Dof6.Zero, null), 0, diagnostics);

        Assert.False(check.Available);
        Assert.Contains(diagnostics, d => d.Code == BoundaryScenarioDiagnostics.Info && d.Message.Contains("недоступен"));
    }

    [Fact]
    public void Modes_SupportKinematicLoadForceAndDisplacement_InPriorityOrder()
    {
        var actions = End(new Dof6(1, 2, 3, 4, 5, 6), null, displacement: new Dof6(0.1, 0.2, 0.3, 0.4, 0.5, 0.6));
        var diagnostics = new List<FemValidationDiagnostic>();

        var modes = BoundaryDofModeSelector.Select(actions, parentDofMask: 1 << 0,
            [new InterfaceKinematicLoad("102", 1, -0.005, 3)], [], diagnostics);

        Assert.Equal(DofMode.Fixed, modes[0].Mode);
        Assert.Equal((DofMode.Kinematic, (double?)-0.005), (modes[1].Mode, modes[1].Value));
        Assert.All(modes.Skip(2), m => Assert.Equal(DofMode.Force, m.Mode));
        Assert.Equal(6, modes[5].Value);
        Assert.DoesNotContain(diagnostics, d => d.IsError);
    }

    [Fact]
    public void Modes_UndefinedForce_FallsBackToParentDisplacement()
    {
        var actions = End(null, null, displacement: new Dof6(0.1, 0.2, 0.3, 0.4, 0.5, 0.6), unsupported: ["900"]);

        var modes = BoundaryDofModeSelector.Select(actions, 0, [], [], []);

        Assert.All(modes, m => Assert.Equal(DofMode.Kinematic, m.Mode));
        Assert.Equal(0.3, modes[2].Value);
    }

    [Fact]
    public void Modes_NothingAvailable_IsBlocking()
    {
        var diagnostics = new List<FemValidationDiagnostic>();

        BoundaryDofModeSelector.Select(End(null, null), 0, [], [], diagnostics);

        Assert.Equal(6, diagnostics.Count(d => d.Code == BoundaryScenarioDiagnostics.DofUndetermined && d.IsError));
    }

    [Fact]
    public void Overrides_ValidAndInvalid()
    {
        var withShell = End(null, null, displacement: new Dof6(0.1, 0.2, 0.3, 0.4, 0.5, 0.6), unsupported: ["900"]);
        var diagnostics = new List<FemValidationDiagnostic>();

        var modes = BoundaryDofModeSelector.Select(withShell, 0, [],
            [new DofOverride(true, 0, DofMode.Fixed), new DofOverride(true, 1, DofMode.Force), new DofOverride(false, 2, DofMode.Fixed)],
            diagnostics);

        Assert.Equal((DofMode.Fixed, DofSource.Override), (modes[0].Mode, modes[0].Source));
        Assert.Equal(DofSource.Override, modes[1].Source);
        Assert.Contains(diagnostics, d => d.Code == BoundaryScenarioDiagnostics.UnsupportedJunction && d.IsError);
        // Переопределение другого конца к этому не применяется.
        Assert.Equal((DofMode.Kinematic, DofSource.Auto), (modes[2].Mode, modes[2].Source));

        var noDisplacement = new List<FemValidationDiagnostic>();
        BoundaryDofModeSelector.Select(End(Dof6.Zero, null), 0, [], [new DofOverride(true, 3, DofMode.Kinematic)], noDisplacement);
        Assert.Contains(noDisplacement, d => d.Code == BoundaryScenarioDiagnostics.OverrideInvalid && d.IsError);
    }
}
