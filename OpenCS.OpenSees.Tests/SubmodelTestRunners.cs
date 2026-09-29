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

/// <summary>Общие заготовки тестов субмоделей: подставной раннер, линейный/нелинейный результат, материализованная субмодель.</summary>
internal static class SubmodelTestRunners
{
    /// <summary>Подставной раннер: результат по тегу постановки, счётчик вызовов.</summary>
    internal sealed class FakeRunner(Func<FemAnalysis, CalcResult> produce) : ISubmodelAnalysisRunner
    {
        public List<string> Calls { get; } = [];

        public Task<CalcResult> RunAsync(FemSchema schema, FemAnalysis analysis, CancellationToken ct)
        {
            Calls.Add(analysis.Tag);
            return Task.FromResult(produce(analysis));
        }
    }

    internal static FemAnalysisParams Params(double step = 0.25) => new() { CalcType = CalcType.C, LoadFactorStep = step };

    /// <summary>Линейный результат ребёнка = результат родителя (теги совпадают), масштаб sign.</summary>
    internal static CalcResult Linear(double sign = 1)
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
    internal static CalcResult Nonlinear(string status = "ok")
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

    internal static FakeRunner Runner(Func<CalcResult>? linear = null, Func<CalcResult>? nonlinear = null) =>
        new(a => a.Tag == StraightBeamSubmodelNonlinearService.AnalysisTag ? (nonlinear ?? (() => Nonlinear()))() : (linear ?? (() => Linear()))());

    internal static int Materialized(DatabaseService db)
    {
        var seeded = SeedScenario(db);
        var outcome = new StraightBeamSubmodelMaterializationService(db).Materialize(seeded.Extraction.SubmodelSchemaId);
        Assert.NotNull(outcome.Materialization);
        return seeded.Extraction.SubmodelSchemaId;
    }

    internal static void InDatabase(Action<DatabaseService> body)
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            body(db);
        }
        finally { DeleteDatabase(path); }
    }

    internal static async Task InDatabaseAsync(Func<DatabaseService, Task> body)
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            await body(db);
        }
        finally { DeleteDatabase(path); }
    }
}
