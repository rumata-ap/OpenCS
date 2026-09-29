using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Submodel;
using OpenCS.OpenSees.Structural;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using static OpenCS.OpenSees.Tests.StraightBeamBoundaryScenarioServiceTests;
using static OpenCS.OpenSees.Tests.StraightBeamSubmodelPersistenceTests;
using static OpenCS.OpenSees.Tests.SubmodelTestRunners;

namespace OpenCS.OpenSees.Tests;

/// <summary>VM вкладки «Субмодель» на SQLite: рама из эталона, расчёты — подставным раннером.</summary>
public sealed class SubmodelPanelVMTests
{
    sealed class FakeHost : ISubmodelUiHost
    {
        public bool Confirm { get; set; } = true;
        public int Reloads { get; private set; }
        public List<string> BusyMessages { get; } = [];
        public List<string?> EndMessages { get; } = [];
        public List<FemValidationDiagnostic> Logged { get; } = [];
        public FemAnalysisParams? DialogResult { get; set; } = Params();
        public int DialogCalls { get; private set; }

        public CancellationTokenSource BeginBusy(string message) { BusyMessages.Add(message); return new(); }
        public void EndBusy(string? message = null) => EndMessages.Add(message);
        public bool ConfirmDiscardUnsavedEdits() => Confirm;
        public void ReloadSchemaPage() => Reloads++;
        public FemAnalysisParams? EditNonlinearParameters(FemAnalysis? existing, FemAnalysisParams? current)
        {
            DialogCalls++;
            return DialogResult;
        }
        public void Log(FemValidationDiagnostic diagnostic) => Logged.Add(diagnostic);
    }

    static CalcResult ParentResult(Func<FemLinearResult, FemLinearResult>? edit = null)
    {
        var result = ToLinearResult(Fixture());
        return new CalcResult
        {
            TaskKind = "fem_linear", TaskTag = "linear", Created = "2026-09-29", Status = "ok",
            DataJson = JsonSerializer.Serialize(edit is null ? result : edit(result))
        };
    }

    static SubmodelPanelVM Panel(DatabaseService db, int child, FakeHost host, ISubmodelAnalysisRunner? runner = null,
        bool precheck = true) =>
        new(child, db, runner ?? Runner(), () => new CalcSettings { SubmodelLinearPrecheck = precheck }, host);

    [Fact]
    public void FreshSubmodel_HasNoScenario_MaterializeUnavailable() => InDatabase(db =>
    {
        var (extraction, _) = Seed(db, ParentResult());

        var panel = Panel(db, extraction.SubmodelSchemaId, new FakeHost());

        Assert.NotNull(panel.Extraction);
        Assert.Equal(3, panel.SegmentCount);
        Assert.Equal("portal", panel.ParentSchemaTag);
        Assert.Null(panel.Scenario);
        Assert.True(panel.BuildScenarioCommand.CanExecute(null));
        Assert.False(panel.MaterializeCommand.CanExecute(null));
        Assert.False(panel.RunLinearCommand.CanExecute(null));
    });

    [Fact]
    public void BuildScenario_ShowsTwelveRows_EnablesMaterialize() => InDatabase(db =>
    {
        var (extraction, _) = Seed(db, ParentResult());
        var panel = Panel(db, extraction.SubmodelSchemaId, new FakeHost());

        panel.BuildScenarioCommand.Execute(null);

        Assert.Equal(ScenarioStatus.Complete, panel.Scenario!.Status);
        Assert.Equal(12, panel.ScenarioRows.Count);
        Assert.Equal(2, panel.EndRows.Count);
        Assert.True(panel.MaterializeCommand.CanExecute(null));
        Assert.False(panel.RunLinearCommand.CanExecute(null));
    });

    [Fact]
    public void BlockedScenario_DisablesMaterialize() => InDatabase(db =>
    {
        var (extraction, _) = Seed(db, ParentResult());
        db.DeleteCalcResult(db.CalcResults.Single(r => r.Id == extraction.ParentResultId));
        var host = new FakeHost();
        var panel = Panel(db, extraction.SubmodelSchemaId, host);

        panel.BuildScenarioCommand.Execute(null);

        Assert.Equal(ScenarioStatus.Blocked, panel.Scenario!.Status);
        Assert.False(panel.MaterializeCommand.CanExecute(null));
        Assert.Contains(panel.ScenarioDiagnostics, d => d.IsError);
        Assert.NotEmpty(host.Logged);
    });

    [Fact]
    public void RecalculatedParent_ScenarioBuildReportsStaleExtraction() => InDatabase(db =>
    {
        var (extraction, _) = Seed(db, ParentResult());
        var analysis = db.GetFemAnalyses(extraction.ParentSchemaId).Single(a => a.Id == extraction.ParentAnalysisId);
        var recalculated = ParentResult();
        db.SaveCalcResult(recalculated);
        analysis.ResultId = recalculated.Id;
        db.SaveFemAnalysis(analysis);
        var panel = Panel(db, extraction.SubmodelSchemaId, new FakeHost());

        panel.BuildScenarioCommand.Execute(null);

        Assert.Null(panel.Scenario);
        Assert.Contains(panel.ScenarioDiagnostics, d => d.Code == BoundaryScenarioDiagnostics.ExtractionStale && d.IsError);
        Assert.False(panel.MaterializeCommand.CanExecute(null));
    });

    [Fact]
    public void Materialize_ReloadsPage_NewPanelSeesMaterialization() => InDatabase(db =>
    {
        var (extraction, _) = Seed(db, ParentResult());
        int child = extraction.SubmodelSchemaId;
        var host = new FakeHost();
        var panel = Panel(db, child, host);
        panel.BuildScenarioCommand.Execute(null);

        panel.MaterializeCommand.Execute(null);

        Assert.Equal(1, host.Reloads);
        var reopened = Panel(db, child, host);
        Assert.True(reopened.IsMaterializationCurrent);
        Assert.Equal(3, reopened.MaterializationDetails.Count);
        Assert.True(reopened.RunLinearCommand.CanExecute(null));
        Assert.Null(reopened.LinearReport);
    });

    [Fact]
    public void Materialize_Declined_DoesNotTouchSchema() => InDatabase(db =>
    {
        var (extraction, _) = Seed(db, ParentResult());
        var host = new FakeHost { Confirm = false };
        var panel = Panel(db, extraction.SubmodelSchemaId, host);
        panel.BuildScenarioCommand.Execute(null);

        panel.MaterializeCommand.Execute(null);

        Assert.Equal(0, host.Reloads);
        Assert.Null(db.GetSubmodelMaterialization(panel.Scenario!.Id));
    });

    [Fact]
    public async Task RunLinear_PassesAndEndsBusy() => await InDatabaseAsync(async db =>
    {
        int child = Materialized(db);
        var host = new FakeHost();
        var runner = Runner();
        var panel = Panel(db, child, host, runner);

        await panel.RunLinearAsync();

        Assert.True(panel.LinearReport!.Passed);
        Assert.Equal(6, panel.LinearRows.Count);
        Assert.Single(runner.Calls);
        Assert.Single(host.BusyMessages);
        Assert.Single(host.EndMessages);
        Assert.True(panel.RunLinearCommand.CanExecute(null));
    });

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public async Task RunNonlinear_PrecheckSetting_ControlsRunnerCalls(bool precheck, int calls) => await InDatabaseAsync(async db =>
    {
        int child = Materialized(db);
        var host = new FakeHost();
        var runner = Runner();
        var panel = Panel(db, child, host, runner, precheck);

        await panel.RunNonlinearAsync();

        Assert.Equal(calls, runner.Calls.Count);
        Assert.Equal(1, host.DialogCalls);
        Assert.True(panel.NonlinearReport!.LambdaReached);
        Assert.Equal(6, panel.NonlinearRows.Count);
        Assert.Equal(precheck ? "SubmodelPrecheckPassed" : "SubmodelPrecheckDisabled", panel.PrecheckText);
        Assert.Contains(panel.PrecheckText, panel.NonlinearNotes);
        Assert.Single(host.EndMessages);

        await panel.RunNonlinearAsync();
        Assert.Equal(1, host.DialogCalls);
    });

    [Fact]
    public async Task RunNonlinear_DialogCancelled_DoesNotRun() => await InDatabaseAsync(async db =>
    {
        int child = Materialized(db);
        var host = new FakeHost { DialogResult = null };
        var runner = Runner();

        await Panel(db, child, host, runner).RunNonlinearAsync();

        Assert.Empty(runner.Calls);
        Assert.Empty(host.BusyMessages);
    });

    sealed class CancellingRunner : ISubmodelAnalysisRunner
    {
        public Task<CalcResult> RunAsync(FemSchema schema, FemAnalysis analysis, CancellationToken ct) =>
            throw new OperationCanceledException();
    }

    sealed class FailingRunner : ISubmodelAnalysisRunner
    {
        public Task<CalcResult> RunAsync(FemSchema schema, FemAnalysis analysis, CancellationToken ct) =>
            throw new InvalidOperationException("сбой");
    }

    [Fact]
    public async Task Cancel_EndsBusyAndKeepsPreviousReports() => await InDatabaseAsync(async db =>
    {
        int child = Materialized(db);
        var first = Panel(db, child, new FakeHost(), precheck: false);
        await first.RunNonlinearAsync();
        var host = new FakeHost();
        var panel = Panel(db, child, host, new CancellingRunner());
        var before = panel.NonlinearReport;

        await panel.RunNonlinearAsync();
        await panel.RunLinearAsync();

        Assert.Same(before, panel.NonlinearReport);
        Assert.Equal(["CalcTaskCancelled", "CalcTaskCancelled"], host.EndMessages);
        Assert.True(panel.RunNonlinearCommand.CanExecute(null));
    });

    [Fact]
    public async Task RunnerFailure_EndsBusyAndLogs() => await InDatabaseAsync(async db =>
    {
        int child = Materialized(db);
        var host = new FakeHost();
        var panel = Panel(db, child, host, new FailingRunner());

        await panel.RunNonlinearAsync();

        Assert.Equal([null], host.EndMessages);
        Assert.Contains(host.Logged, d => d.Message == "сбой" && d.IsError);
        Assert.True(panel.RunNonlinearCommand.CanExecute(null));
    });

    [Fact]
    public async Task Reopen_RestoresReportsWithoutRunning() => await InDatabaseAsync(async db =>
    {
        int child = Materialized(db);
        var panel = Panel(db, child, new FakeHost());
        await panel.RunNonlinearAsync();
        var runner = Runner();

        var reopened = Panel(db, child, new FakeHost(), runner);

        Assert.Empty(runner.Calls);
        Assert.True(reopened.LinearReport!.Passed);
        Assert.NotNull(reopened.NonlinearReport);
        Assert.Null(reopened.PrecheckText);
    });

    [Fact]
    public void Override_Kinematic_IsSavedAndRestored() => InDatabase(db =>
    {
        var (extraction, _) = Seed(db, ParentResult());
        int child = extraction.SubmodelSchemaId;
        var panel = Panel(db, child, new FakeHost());
        panel.BuildScenarioCommand.Execute(null);
        var row = panel.ScenarioRows.First(r => r.Mode == DofMode.Force);

        row.Choice = DofOverrideChoice.Kinematic;
        panel.BuildScenarioCommand.Execute(null);

        var saved = panel.Scenario!.Scenario.Ends.Single(e => e.AtStart == row.AtStart).Dofs[row.DofIndex];
        Assert.Equal(DofMode.Kinematic, saved.Mode);
        Assert.Equal(DofSource.Override, saved.Source);
        var reopened = Panel(db, child, new FakeHost());
        Assert.Equal(DofOverrideChoice.Kinematic,
            reopened.ScenarioRows.Single(r => r.AtStart == row.AtStart && r.DofIndex == row.DofIndex).Choice);
        Assert.Equal(1, reopened.ScenarioRows.Count(r => r.Choice != DofOverrideChoice.Auto));
    });

    [Fact]
    public void Override_KinematicWithoutParentDisplacements_BlocksScenario() => InDatabase(db =>
    {
        var (extraction, _) = Seed(db, ParentResult(r => new FemLinearResult
        {
            Status = r.Status, Reactions = r.Reactions, ElementForces = r.ElementForces,
            Displacements = r.Displacements.Where(d => d.NodeTag is not (2 or 3)).ToList()
        }));
        var panel = Panel(db, extraction.SubmodelSchemaId, new FakeHost());
        panel.BuildScenarioCommand.Execute(null);
        var row = panel.ScenarioRows.First(r => r.Mode == DofMode.Force);

        row.Choice = DofOverrideChoice.Kinematic;
        panel.BuildScenarioCommand.Execute(null);

        Assert.Equal(ScenarioStatus.Blocked, panel.Scenario!.Status);
        Assert.Contains(panel.ScenarioDiagnostics, d => d.Code == BoundaryScenarioDiagnostics.OverrideInvalid);
        Assert.False(panel.MaterializeCommand.CanExecute(null));
    });
}
