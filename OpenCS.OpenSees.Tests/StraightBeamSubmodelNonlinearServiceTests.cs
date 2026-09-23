using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Submodel;
using OpenCS.OpenSees.Structural;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using static OpenCS.OpenSees.Tests.StraightBeamBoundaryScenarioServiceTests;
using static OpenCS.OpenSees.Tests.StraightBeamSubmodelPersistenceTests;
using static OpenCS.OpenSees.Tests.SubmodelMaterializationPersistenceTests;

namespace OpenCS.OpenSees.Tests;

public sealed class StraightBeamSubmodelNonlinearServiceTests
{
    /// <summary>Подставной раннер: результат по тегу постановки, счётчик вызовов.</summary>
    sealed class FakeRunner(Func<FemAnalysis, CalcResult> produce) : ISubmodelAnalysisRunner
    {
        public List<string> Calls { get; } = [];

        public Task<CalcResult> RunAsync(FemSchema schema, FemAnalysis analysis, CancellationToken ct)
        {
            Calls.Add(analysis.Tag);
            return Task.FromResult(produce(analysis));
        }
    }

    static FemAnalysisParams Params(double step = 0.25) => new() { CalcType = CalcType.C, LoadFactorStep = step };

    /// <summary>Линейный результат ребёнка = результат родителя (теги совпадают), масштаб sign.</summary>
    static CalcResult Linear(double sign = 1)
    {
        var r = ToLinearResult(Fixture());
        var scaled = new FemLinearResult
        {
            Status = "ok",
            Displacements = r.Displacements.Select(d => d with { Ux = sign * d.Ux, Uy = sign * d.Uy, Uz = sign * d.Uz, Rx = sign * d.Rx, Ry = sign * d.Ry, Rz = sign * d.Rz }).ToList(),
            Reactions = [],
            ElementForces = r.ElementForces
        };
        return new CalcResult { TaskKind = "fem_linear", TaskTag = "linear", Created = "2026-09-23", Status = "ok", DataJson = JsonSerializer.Serialize(scaled) };
    }

    /// <summary>Нелинейный результат: шаги λ = 0,5 и 1, состояния = λ·родитель, реакции gauge = 0.</summary>
    static CalcResult Nonlinear(string status = "ok")
    {
        var r = ToLinearResult(Fixture());
        FemNonlinearStepResult Step(int index, double lambda) => new(index, lambda, true,
            r.Displacements.Select(d => d with { Ux = lambda * d.Ux, Uy = lambda * d.Uy, Uz = lambda * d.Uz, Rx = lambda * d.Rx, Ry = lambda * d.Ry, Rz = lambda * d.Rz }).ToList(),
            [new FemNodeReaction(2, 0, 0, 0, 0, 0, 0)],
            r.ElementForces.Select(f => new FemElementEndForces(f.ElemTag,
                lambda * f.Ni, lambda * f.Qyi, lambda * f.Qzi, lambda * f.Mxi, lambda * f.Myi, lambda * f.Mzi,
                lambda * f.Nj, lambda * f.Qyj, lambda * f.Qzj, lambda * f.Mxj, lambda * f.Myj, lambda * f.Mzj)).ToList());
        var result = new FemNonlinearResult { Status = status, Steps = status == "error" ? [] : [Step(1, 0.5), Step(2, 1.0)], Diagnostics = ["диагностика"] };
        return new CalcResult { TaskKind = "fem_nonlinear", TaskTag = "nonlinear", Created = "2026-09-23", Status = status, DataJson = JsonSerializer.Serialize(result) };
    }

    static FakeRunner Runner(Func<CalcResult>? linear = null, Func<CalcResult>? nonlinear = null) =>
        new(a => a.Tag == StraightBeamSubmodelNonlinearService.AnalysisTag ? (nonlinear ?? (() => Nonlinear()))() : (linear ?? (() => Linear()))());

    static int Materialized(DatabaseService db)
    {
        var seeded = SeedScenario(db);
        var outcome = new StraightBeamSubmodelMaterializationService(db).Materialize(seeded.Extraction.SubmodelSchemaId);
        Assert.NotNull(outcome.Materialization);
        return seeded.Extraction.SubmodelSchemaId;
    }

    static void InDatabase(Action<DatabaseService> body)
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            body(db);
        }
        finally { DeleteDatabase(path); }
    }

    static async Task InDatabaseAsync(Func<DatabaseService, Task> body)
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            await body(db);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void Prepare_CreatesSingleStageAnalysisToLambdaOne() => InDatabase(db =>
    {
        int child = Materialized(db);

        var outcome = new StraightBeamSubmodelNonlinearService(db).PrepareNonlinearAnalysis(child, Params(0.25));

        Assert.DoesNotContain(outcome.Diagnostics, d => d.IsError);
        var analysis = outcome.Analysis!;
        Assert.Equal("nonlinear", analysis.Kind);
        Assert.Equal(StraightBeamSubmodelNonlinearService.AnalysisTag, analysis.Tag);
        var stored = FemAnalysisParams.Parse(db.GetFemAnalyses(child).Single(a => a.Id == analysis.Id).ParamsJson);
        Assert.Equal(CalcType.C, stored.CalcType);
        var stage = Assert.Single(stored.Stages);
        Assert.Equal(1.0, stage.MaxLoadFactor);
        Assert.Equal(0.25, stage.LoadFactorStep);
        Assert.Null(stage.PathControl);
        var loadCase = db.GetFemLoadCases(child).Single(c => c.Tag == SubmodelMaterializationPlanner.LoadCaseTag);
        Assert.Equal([loadCase.Id], FemLoadExpression.Parse(stage.LoadExpressionJson).LoadCaseIds);
        Assert.Single(db.FemSchemas.Single(s => s.Id == child).Analyses, a => a.Tag == StraightBeamSubmodelNonlinearService.AnalysisTag);
    });

    [Fact]
    public void Prepare_WithoutCalcTypeOrWithBadStep_IsParamsInvalid() => InDatabase(db =>
    {
        int child = Materialized(db);
        var service = new StraightBeamSubmodelNonlinearService(db);

        var noCalcType = service.PrepareNonlinearAnalysis(child, new FemAnalysisParams());
        var zeroStep = service.PrepareNonlinearAnalysis(child, Params(0));
        var bigStep = service.PrepareNonlinearAnalysis(child, Params(1.5));

        foreach (var outcome in new[] { noCalcType, zeroStep, bigStep })
        {
            Assert.Null(outcome.Analysis);
            Assert.Contains(outcome.Diagnostics, d => d.Code == SubmodelNonlinearDiagnostics.ParamsInvalid && d.IsError);
        }
    });

    [Fact]
    public void Prepare_WithoutMaterialization_IsStale() => InDatabase(db =>
    {
        var seeded = SeedScenario(db);

        var outcome = new StraightBeamSubmodelNonlinearService(db).PrepareNonlinearAnalysis(seeded.Extraction.SubmodelSchemaId, Params());

        Assert.Null(outcome.Analysis);
        Assert.Contains(outcome.Diagnostics, d => d.Code == SubmodelMaterializationDiagnostics.Stale);
    });

    [Fact]
    public async Task Prepare_SameParamsKeepResult_ChangedParamsDropIt() => await InDatabaseAsync(async db =>
    {
        int child = Materialized(db);
        var service = new StraightBeamSubmodelNonlinearService(db);
        await service.RunAsync(child, Params(0.25), Runner(), linearPrecheck: false, CancellationToken.None);
        var analysis = service.PrepareNonlinearAnalysis(child, Params(0.25)).Analysis!;
        int resultId = analysis.ResultId!.Value;

        var same = service.PrepareNonlinearAnalysis(child, Params(0.25)).Analysis!;
        Assert.Equal(analysis.Id, same.Id);
        Assert.Equal(resultId, same.ResultId);

        var changed = service.PrepareNonlinearAnalysis(child, Params(0.1)).Analysis!;
        Assert.Equal(analysis.Id, changed.Id);
        Assert.Null(changed.ResultId);
        Assert.Null(db.GetCalcResultById(resultId));
        Assert.Single(db.FemSchemas.Single(s => s.Id == child).Analyses, a => a.Tag == StraightBeamSubmodelNonlinearService.AnalysisTag);
    });

    [Fact]
    public async Task Run_WithPrecheck_RunsBothSavesAndVerifies() => await InDatabaseAsync(async db =>
    {
        int child = Materialized(db);
        var runner = Runner();
        var service = new StraightBeamSubmodelNonlinearService(db);

        var outcome = await service.RunAsync(child, Params(), runner, linearPrecheck: true, CancellationToken.None);

        Assert.Equal([SubmodelMaterializationPlanner.AnalysisTag, StraightBeamSubmodelNonlinearService.AnalysisTag], runner.Calls);
        Assert.True(outcome.Precheck!.Passed, string.Join(" | ", outcome.Diagnostics.Select(d => d.Message)));
        Assert.True(outcome.Report!.LambdaReached);
        Assert.Equal(0, outcome.Report.AtReached.Translation.Value, 12);
        Assert.Equal(0, outcome.Report.AtReached.EndMoment.Value, 9);
        Assert.DoesNotContain(outcome.Diagnostics, d => d.IsError || d.Code == SubmodelNonlinearDiagnostics.PrecheckFailed);
        Assert.All(db.GetFemAnalyses(child), a => Assert.NotNull(a.ResultId));

        int firstResult = db.GetFemAnalyses(child).Single(a => a.Tag == StraightBeamSubmodelNonlinearService.AnalysisTag).ResultId!.Value;
        await service.RunAsync(child, Params(), runner, linearPrecheck: false, CancellationToken.None);
        Assert.Null(db.GetCalcResultById(firstResult));
    });

    [Fact]
    public async Task Run_WithoutPrecheck_RunsOnlyNonlinear() => await InDatabaseAsync(async db =>
    {
        int child = Materialized(db);
        var runner = Runner();

        var outcome = await new StraightBeamSubmodelNonlinearService(db).RunAsync(child, Params(), runner, linearPrecheck: false, CancellationToken.None);

        Assert.Equal([StraightBeamSubmodelNonlinearService.AnalysisTag], runner.Calls);
        Assert.Null(outcome.Precheck);
        Assert.NotNull(outcome.Report);
    });

    [Fact]
    public async Task Run_FailedPrecheck_WarnsAndContinues() => await InDatabaseAsync(async db =>
    {
        int child = Materialized(db);
        var runner = Runner(linear: () => Linear(sign: -1));

        var outcome = await new StraightBeamSubmodelNonlinearService(db).RunAsync(child, Params(), runner, linearPrecheck: true, CancellationToken.None);

        var warning = Assert.Single(outcome.Diagnostics, d => d.Code == SubmodelNonlinearDiagnostics.PrecheckFailed);
        Assert.False(warning.IsError);
        Assert.False(outcome.Precheck!.Passed);
        Assert.Equal(2, runner.Calls.Count);
        Assert.NotNull(outcome.Report);
    });

    [Theory]
    [InlineData("error")]
    [InlineData("cancelled")]
    public async Task Run_NonlinearFailure_NoReportButResultSaved(string status) => await InDatabaseAsync(async db =>
    {
        int child = Materialized(db);

        var outcome = await new StraightBeamSubmodelNonlinearService(db).RunAsync(child, Params(),
            Runner(nonlinear: () => Nonlinear(status)), linearPrecheck: false, CancellationToken.None);

        Assert.Null(outcome.Report);
        Assert.Contains(outcome.Diagnostics, d => d.Code == SubmodelNonlinearDiagnostics.ResultInvalid && d.IsError);
        var analysis = db.GetFemAnalyses(child).Single(a => a.Tag == StraightBeamSubmodelNonlinearService.AnalysisTag);
        Assert.Equal(status, analysis.Status);
        Assert.NotNull(db.GetCalcResultById(analysis.ResultId!.Value));
    });

    [Fact]
    public void VerifyNonlinear_WithoutResult_AsksToRun() => InDatabase(db =>
    {
        int child = Materialized(db);
        var service = new StraightBeamSubmodelNonlinearService(db);
        service.PrepareNonlinearAnalysis(child, Params());

        var outcome = service.VerifyNonlinear(child);

        Assert.Null(outcome.Report);
        Assert.Contains(outcome.Diagnostics, d => d.Code == SubmodelNonlinearDiagnostics.Info && !d.IsError);
    });
}
