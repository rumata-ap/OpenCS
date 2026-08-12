using OpenCS.OpenSees.Structural;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

public class ControlDisplacementPointsBuilderTests
{
    static FemNodeDisplacement Disp(int tag, double ux = 0, double uy = 0, double uz = 0) => new(tag, ux, uy, uz, 0, 0, 0);

    static FemNonlinearStepResult Step(int stepIndex, int stageIndex, double lambda, bool converged, params FemNodeDisplacement[] disps) =>
        new(stepIndex, lambda, converged, disps, [], []) { StageIndex = stageIndex };

    [Fact]
    public void Build_DirectDisplacementControlStage_UsesControlNodeDof()
    {
        var dc = new FemDisplacementControlSettings(4, 3, 0.001, 0.0001, 0.01, -0.05, 200);
        var stagePathControls = new List<FemPathControlSettings?> { new(FemPathControlMode.DisplacementControl, DisplacementControl: dc) };
        var steps = new List<FemNonlinearStepResult> { Step(1, 0, 0.5, true, Disp(4, uz: -0.01)) };

        var points = ControlDisplacementPointsBuilder.Build(steps, stagePathControls, []);

        Assert.Single(points);
        Assert.Equal(-0.01, points[0].X, 12);
        Assert.Equal(0.5, points[0].Y, 12);
    }

    [Fact]
    public void Build_ArcLengthStage_UsesMonitorNodeDof()
    {
        var al = new FemArcLengthSettings(0.01, 1.0, 0.001, 100, 4, 3);
        var stagePathControls = new List<FemPathControlSettings?> { new(FemPathControlMode.ArcLength, ArcLength: al) };
        var steps = new List<FemNonlinearStepResult> { Step(1, 0, 0.5, true, Disp(4, uz: 0.02)) };

        var points = ControlDisplacementPointsBuilder.Build(steps, stagePathControls, []);
        Assert.Equal(0.02, points[0].X, 12);
    }

    [Fact]
    public void Build_LoadControlStageWithoutContinuation_ProducesNaN()
    {
        var stagePathControls = new List<FemPathControlSettings?> { new(FemPathControlMode.LoadControl) };
        var steps = new List<FemNonlinearStepResult> { Step(1, 0, 0.5, true, Disp(4)) };

        var points = ControlDisplacementPointsBuilder.Build(steps, stagePathControls, []);
        Assert.True(double.IsNaN(points[0].X));
    }

    [Fact]
    public void Build_LegacyResultWithoutStagePathControls_ProducesNaNForEveryPoint_NoException()
    {
        // Backward-compat guard: StagePathControls пуст (старый DataJson) — не должно бросать
        // IndexOutOfRangeException при обращении к StagePathControls[Steps[i].StageIndex].
        var steps = new List<FemNonlinearStepResult> { Step(1, 0, 0.5, true, Disp(4)), Step(2, 0, 1.0, true, Disp(4)) };

        var points = ControlDisplacementPointsBuilder.Build(steps, [], []);

        Assert.Equal(2, points.Count);
        Assert.All(points, p => Assert.True(double.IsNaN(p.X)));
    }

    [Fact]
    public void Build_ContinuationSwitch_UsesContinuationNodeOnlyFromAtStepIndex()
    {
        var cdc = new FemDisplacementControlSettings(4, 3, 0.001, 0.0001, 0.01, -0.05, 200);
        var stagePathControls = new List<FemPathControlSettings?>
        {
            new(FemPathControlMode.LoadControl, ContinueWithMode: FemPathControlMode.DisplacementControl, ContinueWithDisplacementControl: cdc)
        };
        var switches = new List<FemPathControlSwitch> { new(StageIndex: 0, AtStepIndex: 3) };
        var steps = new List<FemNonlinearStepResult>
        {
            Step(1, 0, 0.2, true, Disp(4, uz: -0.001)), // до переключения (StepIndex 1 < AtStepIndex 3) — NaN
            Step(2, 0, 0.4, true, Disp(4, uz: -0.002)), // до переключения (StepIndex 2 < 3) — NaN
            Step(3, 0, 0.4, true, Disp(4, uz: -0.010)), // с переключения (StepIndex 3 >= 3) — реальная точка
        };

        var points = ControlDisplacementPointsBuilder.Build(steps, stagePathControls, switches);

        Assert.True(double.IsNaN(points[0].X));
        Assert.True(double.IsNaN(points[1].X));
        Assert.Equal(-0.010, points[2].X, 12);
    }

    [Fact]
    public void Build_ArcLengthContinuation_UsesMonitorNotControlFields()
    {
        // Регрессия на устранённое противоречие: у ArcLength нет ControlDof, только
        // MonitorDof — continuation в ArcLength обязан брать Monitor*, не Control*.
        var cal = new FemArcLengthSettings(0.01, 1.0, 0.001, 100, MonitorNodeTag: 5, MonitorDof: 2);
        var stagePathControls = new List<FemPathControlSettings?>
        {
            new(FemPathControlMode.LoadControl, ContinueWithMode: FemPathControlMode.ArcLength, ContinueWithArcLength: cal)
        };
        var switches = new List<FemPathControlSwitch> { new(0, 1) };
        var steps = new List<FemNonlinearStepResult> { Step(1, 0, 0.3, true, Disp(5, uy: 0.007)) };

        var points = ControlDisplacementPointsBuilder.Build(steps, stagePathControls, switches);
        Assert.Equal(0.007, points[0].X, 12);
    }

    [Fact]
    public void Build_TwoDirectStagesDifferentNodes_ProduceDifferentSegmentIds()
    {
        var dc1 = new FemDisplacementControlSettings(4, 3, 0.001, 0.0001, 0.01, -0.05, 200);
        var dc2 = new FemDisplacementControlSettings(6, 2, 0.001, 0.0001, 0.01, 0.03, 200);
        var stagePathControls = new List<FemPathControlSettings?>
        {
            new(FemPathControlMode.DisplacementControl, DisplacementControl: dc1),
            new(FemPathControlMode.DisplacementControl, DisplacementControl: dc2),
        };
        var steps = new List<FemNonlinearStepResult>
        {
            Step(1, 0, 0.5, true, Disp(4, uz: -0.01)),
            Step(2, 1, 0.5, true, Disp(6, uy: 0.02)),
        };

        var points = ControlDisplacementPointsBuilder.Build(steps, stagePathControls, []);

        Assert.NotEqual(points[0].SegmentId, points[1].SegmentId);
    }

    [Fact]
    public void Build_TwoDirectStagesSameNodeAndDof_StillProduceDifferentSegmentIds()
    {
        var dc1 = new FemDisplacementControlSettings(4, 3, 0.001, 0.0001, 0.01, -0.05, 200);
        var dc2 = new FemDisplacementControlSettings(4, 3, 0.001, 0.0001, 0.01, 0.05, 200); // тот же узел 4, тот же DOF 3
        var stagePathControls = new List<FemPathControlSettings?>
        {
            new(FemPathControlMode.DisplacementControl, DisplacementControl: dc1),
            new(FemPathControlMode.DisplacementControl, DisplacementControl: dc2),
        };
        var steps = new List<FemNonlinearStepResult>
        {
            Step(1, 0, 0.5, true, Disp(4, uz: -0.01)),
            Step(2, 1, 0.5, true, Disp(4, uz: -0.02)),
        };

        var points = ControlDisplacementPointsBuilder.Build(steps, stagePathControls, []);

        Assert.NotEqual(points[0].SegmentId, points[1].SegmentId);
    }

    [Fact]
    public void Build_ModeSwitchSameNodeAndDof_StillProduceDifferentSegmentIds()
    {
        var dc = new FemDisplacementControlSettings(4, 3, 0.001, 0.0001, 0.01, -0.05, 200);
        var al = new FemArcLengthSettings(0.01, 1.0, 0.001, 100, 4, 3);
        var stagePathControls = new List<FemPathControlSettings?>
        {
            new(FemPathControlMode.DisplacementControl, DisplacementControl: dc),
            new(FemPathControlMode.ArcLength, ArcLength: al),
        };
        var steps = new List<FemNonlinearStepResult>
        {
            Step(1, 0, 0.5, true, Disp(4, uz: -0.01)),
            Step(2, 1, 0.5, true, Disp(4, uz: -0.02)),
        };

        var points = ControlDisplacementPointsBuilder.Build(steps, stagePathControls, []);

        Assert.NotEqual(points[0].SegmentId, points[1].SegmentId);
    }

    [Fact]
    public void Build_ConvergedStepMissingDisplacementEntry_ProducesNaN_NotZeroOrCrash()
    {
        var dc = new FemDisplacementControlSettings(4, 3, 0.001, 0.0001, 0.01, -0.05, 200);
        var stagePathControls = new List<FemPathControlSettings?> { new(FemPathControlMode.DisplacementControl, DisplacementControl: dc) };
        var steps = new List<FemNonlinearStepResult> { Step(1, 0, 0.5, converged: false) };

        var points = ControlDisplacementPointsBuilder.Build(steps, stagePathControls, []);

        Assert.True(double.IsNaN(points[0].X));
    }

    [Fact]
    public void Build_ZeroStepContinuation_AtStepIndexBeyondAnyStep_DoesNotThrow()
    {
        var stagePathControls = new List<FemPathControlSettings?> { new(FemPathControlMode.LoadControl,
            ContinueWithMode: FemPathControlMode.DisplacementControl,
            ContinueWithDisplacementControl: new FemDisplacementControlSettings(4, 3, 0.001, 0.0001, 0.01, -0.05, 200)) };
        var switches = new List<FemPathControlSwitch> { new(0, 99) }; // ни один Step не имеет StepIndex>=99
        var steps = new List<FemNonlinearStepResult> { Step(1, 0, 0.5, true, Disp(4)) };

        var points = ControlDisplacementPointsBuilder.Build(steps, stagePathControls, switches);
        Assert.True(double.IsNaN(points[0].X));
    }
}
