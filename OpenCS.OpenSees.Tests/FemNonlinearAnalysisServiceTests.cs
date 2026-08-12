using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public class FemNonlinearAnalysisServiceTests
{
    [Fact]
    public async Task RunAsync_InvalidModel_ReturnsErrorWithoutRunningProcess()
    {
        var service = new FemNonlinearAnalysisService(
            new FemNonlinearTclGenerator(),
            new OpenSeesProcessRunner(),
            new OpenSeesArtifactStore(Path.Combine(Path.GetTempPath(), "opencs_fem_nl_art_" + Guid.NewGuid().ToString("N"))),
            new FemNonlinearResultParser());

        var invalidModel = new FemNonlinearModel();   // без узлов/элементов/секций — Validate() бросит

        var result = await service.RunAsync(invalidModel,
            new OpenSeesRunRequest { ExecutablePath = "OpenSees.exe", WorkingDirectory = Path.GetTempPath() },
            CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void ComputeStatus_ZeroStepSingleStageSuccessful_ReturnsOk()
    {
        var stageCompletions = new List<FemStageCompletion> { new(0, "zero_step_target_already_reached") };
        var steps = new List<FemNonlinearStepResult>(); // ни одного analyze() не выполнено
        bool ok = FemNonlinearAnalysisService.ComputeAllConverged(parsed: true, stageCompletions, steps, totalStages: 1);
        Assert.True(ok);
    }

    [Fact]
    public void ComputeStatus_DuplicateStageIndexReported_ReturnsFalseEvenIfNoFailedReason()
    {
        var stageCompletions = new List<FemStageCompletion> { new(0, "load_control_completed"), new(0, "load_control_completed") };
        var steps = new List<FemNonlinearStepResult> { new(1, 1.0, true, [], [], []) { StageIndex = 0 } };
        bool ok = FemNonlinearAnalysisService.ComputeAllConverged(parsed: true, stageCompletions, steps, totalStages: 2);
        Assert.False(ok); // Count==2==totalStages, но множество индексов {0,0} != {0,1}
    }

    [Fact]
    public void ComputeStatus_UnknownStageReason_ReturnsFalse()
    {
        var stageCompletions = new List<FemStageCompletion> { new(0, "some_unrecognized_future_reason") };
        var steps = new List<FemNonlinearStepResult>();
        bool ok = FemNonlinearAnalysisService.ComputeAllConverged(parsed: true, stageCompletions, steps, totalStages: 1);
        Assert.False(ok);
    }

    [Fact]
    public void ComputeStatus_FailedStageWithSkippedFollowing_ReturnsFalse()
    {
        var stageCompletions = new List<FemStageCompletion> { new(0, "failed"), new(1, "not_run_due_to_previous_failure") };
        var steps = new List<FemNonlinearStepResult> { new(1, 0.5, false, [], [], []) { StageIndex = 0, StopReason = "no_convergence" } };
        bool ok = FemNonlinearAnalysisService.ComputeAllConverged(parsed: true, stageCompletions, steps, totalStages: 2);
        Assert.False(ok);
    }

    [Fact]
    public void ComputeStatus_AllStagesLoadControlCompleted_ReturnsTrue()
    {
        var stageCompletions = new List<FemStageCompletion> { new(0, "load_control_completed") };
        var steps = new List<FemNonlinearStepResult> { new(1, 1.0, true, [], [], []) { StageIndex = 0 } };
        bool ok = FemNonlinearAnalysisService.ComputeAllConverged(parsed: true, stageCompletions, steps, totalStages: 1);
        Assert.True(ok);
    }
}
