using CScore;
using CScore.Fem;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Источник жёсткостей линейного CSfea (инженерная нелинейность, срез 1): разбор параметра, список пригодных записей
/// секущих постановок, отпечаток и его актуальность, e_min.
/// </summary>
public sealed class FemCsfeaStiffnessSourcesTests
{
    static FemAnalysis Secant(int id = 7, CalcType calc = CalcType.NL) => new()
    {
        Id = id, SchemaId = 1, Tag = "Стадии NL", Kind = FemCsfeaRunner.AnalysisKind,
        ParamsJson = new FemAnalysisParams { CalcType = calc, Csfea = new FemCsfeaParams() }.ToJson(),
    };

    static CalcResult Result(FemCsfeaResultSummary s, string status = "ok", string created = "2026-10-18 14:02:11") => new()
    {
        Id = 3, TaskKind = FemCsfeaRunner.TaskKind, Status = status, Created = created, DataJson = s.ToJson(),
    };

    static FemCsfeaResultSummary Staged(bool completed, bool lastFields = true) => new()
    {
        Completed = completed,
        Stages = [new("Постоянная", 2, 0, 0, -1), new("Временная", 2, 0, 0, -2)],
        Steps =
        [
            new(1, 0, 1, 0.5, false, true, 3, 0, null),
            new(2, 0, 2, 1.0, false, true, 3, 0, null),
            new(3, 1, 1, 0.5, false, true, 3, 0, null, Fields: lastFields),
            new(4, 1, 2, 1.0, false, false, 50, 1, null),
        ],
    };

    static byte[] Record(bool matrices, double lambda = 1.0) => FemCsfeaStepFieldsCodec.Pack(new RcSecantStepFields
    {
        LoadFactor = lambda, NodeIds = [], Displacements = [], ShellIds = [1], ShellForces = new double[8],
        ShellStates = new double[5], ShellFlags = [0], BeamIds = [], BeamForces = [], BeamFlags = [],
        HasMatrices = matrices, ShellMatrices = matrices ? new double[31] : [],
    });

    [Fact]
    public void List_AndStatus_ReadProjectDatabase()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opencs_stiffness_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Схема" };
            db.SaveFemSchema(schema);
            var result = Result(Staged(true));
            result.Id = 0;
            db.SaveCalcResult(result);
            db.SaveFemResultSteps(result.Id, [(3, Record(matrices: true))]);
            var secant = Secant(id: 0);
            secant.SchemaId = schema.Id;
            secant.ResultId = result.Id;
            db.SaveFemAnalysis(secant);
            var notRun = Secant(id: 0);
            notRun.SchemaId = schema.Id;
            db.SaveFemAnalysis(notRun);

            var c = Assert.Single(FemCsfeaStiffnessSources.List(db, schema.Id));
            Assert.Equal(secant.Id, c.Analysis.Id);
            var source = FemCsfeaStiffnessSources.Fingerprint(db, c, out var error);
            Assert.Null(error);
            Assert.Equal(FemCsfeaStiffnessSourceStatus.Current, FemCsfeaStiffnessSources.Status(db, schema.Id, source!));

            db.SaveFemResultStep(result.Id, 3, Record(matrices: true, lambda: 0.8));
            Assert.Equal(FemCsfeaStiffnessSourceStatus.Changed, FemCsfeaStiffnessSources.Status(db, schema.Id, source!));
            secant.InvalidateResult();
            db.SaveFemAnalysis(secant);
            Assert.Equal(FemCsfeaStiffnessSourceStatus.Missing, FemCsfeaStiffnessSources.Status(db, schema.Id, source!));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { System.IO.File.Delete(path); } catch (System.IO.IOException) { }
        }
    }

    [Fact]
    public void StiffnessSource_RoundTrips_AndIsOmittedWhenNull()
    {
        var pars = new FemAnalysisParams
        {
            Csfea = new FemCsfeaParams
            {
                StiffnessSource = new FemCsfeaStiffnessSource
                {
                    AnalysisId = 7, BatchIndex = 2, LongKind = FemCsfeaBatchLongKind.Deflection,
                    ResultCreated = "2026-10-18 14:02:11", RecordCrc = 0xDEADBEEF,
                },
            },
        };
        var s = FemAnalysisParams.Parse(pars.ToJson()).Csfea!.StiffnessSource!;

        Assert.Equal(7, s.AnalysisId);
        Assert.Equal(2, s.BatchIndex);
        Assert.Equal(FemCsfeaBatchLongKind.Deflection, s.LongKind);
        Assert.Equal("2026-10-18 14:02:11", s.ResultCreated);
        Assert.Equal(0xDEADBEEFu, s.RecordCrc);
        Assert.DoesNotContain("StiffnessSource", new FemAnalysisParams { Csfea = new FemCsfeaParams() }.ToJson());
        Assert.Null(FemAnalysisParams.Parse("{\"Csfea\":{}}").Csfea!.StiffnessSource);
    }

    [Fact]
    public void Candidates_Staged_FinalStateIsLastConvergedRecordedStep()
    {
        var c = Assert.Single(FemCsfeaStiffnessSources.Candidates(Secant(), Result(Staged(completed: false))));

        Assert.True(c.IsFinalState);
        Assert.Equal(3, c.RecordN);
        Assert.Equal(CalcType.NL, c.Calc);
        Assert.False(c.Completed);
        Assert.Contains("«Временная»", c.State);
        Assert.Contains("0,5", c.Describe().Replace('.', ','));
        Assert.Contains("18.10.2026 14:02", c.Describe());
        Assert.True(c.Matches(new FemCsfeaStiffnessSource { AnalysisId = 7 }));
        Assert.False(c.Matches(new FemCsfeaStiffnessSource { AnalysisId = 7, BatchIndex = 0 }));
    }

    [Fact]
    public void Candidates_SkipsUnsuitableResults()
    {
        var linear = Secant();
        linear.Kind = FemCsfeaRunner.LinearAnalysisKind;

        Assert.Empty(FemCsfeaStiffnessSources.Candidates(linear, Result(Staged(true))));
        Assert.Empty(FemCsfeaStiffnessSources.Candidates(Secant(), Result(Staged(true), status: "error")));
        Assert.Empty(FemCsfeaStiffnessSources.Candidates(Secant(), Result(Staged(true, lastFields: false))));
        Assert.Empty(FemCsfeaStiffnessSources.Candidates(Secant(), null));
        Assert.Empty(FemCsfeaStiffnessSources.Candidates(Secant(),
            Result(new FemCsfeaResultSummary { Kind = FemCsfeaRunner.LinearAnalysisKind, Steps = Staged(true).Steps })));
    }

    [Fact]
    public void Candidates_Batch_RecordedVariantsAndOwnLongTermRuns()
    {
        var summary = new FemCsfeaResultSummary
        {
            BatchMaxLoadFactor = 1.0,
            Batch =
            [
                new() { Index = 0, Tag = "C1", Type = "N", Done = true, Completed = true, LoadFactor = 1, RecordN = 1 },
                new() { Index = 1, Tag = "C2", Type = "N", Done = true, Completed = false, LoadFactor = 0.6, RecordN = 2 },
                new() { Index = 2, Tag = "C3", Type = "N", Done = false },
                new() { Index = 3, Tag = "C4", Type = "N", Done = true, Completed = true, LoadFactor = 1 },
            ],
            BatchLong =
            [
                new() { PairOf = 0, Kind = FemCsfeaBatchLongKind.Part, Type = "NL", PairKind = nameof(CScore.Fem.Combinations.FemLongTermPairKind.Derived), Done = true, Completed = true, LoadFactor = 1, RecordN = 5 },
                new() { PairOf = 0, Kind = FemCsfeaBatchLongKind.Deflection, Type = "NL", PairKind = nameof(CScore.Fem.Combinations.FemLongTermPairKind.Derived), Done = true, Completed = true, LoadFactor = 1, RecordN = 6 },
                new() { PairOf = 1, Kind = FemCsfeaBatchLongKind.Part, Type = "NL", PairKind = nameof(CScore.Fem.Combinations.FemLongTermPairKind.SameAsTotal) },
            ],
        };

        var list = FemCsfeaStiffnessSources.Candidates(Secant(calc: CalcType.N), Result(summary));

        Assert.Equal([1, 2, 5, 6], list.Select(c => c.RecordN));
        Assert.Equal([0, 1, 0, 0], list.Select(c => c.BatchIndex!.Value));
        Assert.Equal([null, null, FemCsfeaBatchLongKind.Part, FemCsfeaBatchLongKind.Deflection], list.Select(c => c.LongKind));
        Assert.Equal([CalcType.N, CalcType.N, CalcType.N, CalcType.NL], list.Select(c => c.Calc));
        Assert.False(list[1].Completed);
        Assert.Contains("не дошёл", list[1].State);
        Assert.Equal("[N] C1", list[0].Label);
        Assert.Contains("на диаграммах NL", list[3].Label);
        Assert.Same(list[2], FemCsfeaStiffnessSources.Find(list,
            new FemCsfeaStiffnessSource { AnalysisId = 7, BatchIndex = 0, LongKind = FemCsfeaBatchLongKind.Part }));
    }

    [Fact]
    public void Fingerprint_RequiresMatrices_AndTracksRecomputation()
    {
        var c = Assert.Single(FemCsfeaStiffnessSources.Candidates(Secant(), Result(Staged(true))));

        Assert.Null(FemCsfeaStiffnessSources.Fingerprint(c, null, out var noRecord));
        Assert.NotNull(noRecord);
        Assert.Null(FemCsfeaStiffnessSources.Fingerprint(c, Record(matrices: false), out var noMatrices));
        Assert.Contains("матриц", noMatrices);

        var record = Record(matrices: true);
        var source = FemCsfeaStiffnessSources.Fingerprint(c, record, out var error);
        Assert.Null(error);
        Assert.NotNull(source);
        Assert.Equal(7, source.AnalysisId);
        Assert.Null(source.BatchIndex);
        Assert.Equal("2026-10-18 14:02:11", source.ResultCreated);
        Assert.Equal(FemCsfeaStiffnessSources.Crc32(record), source.RecordCrc);

        Assert.Equal(FemCsfeaStiffnessSourceStatus.Current, FemCsfeaStiffnessSources.Status(source, c, record));
        Assert.Equal(FemCsfeaStiffnessSourceStatus.Changed, FemCsfeaStiffnessSources.Status(source, c, Record(true, 0.9)));
        var recomputed = Assert.Single(FemCsfeaStiffnessSources.Candidates(Secant(), Result(Staged(true), created: "2026-10-18 15:00:00")));
        Assert.Equal(FemCsfeaStiffnessSourceStatus.Changed, FemCsfeaStiffnessSources.Status(source, recomputed, record));
        Assert.Equal(FemCsfeaStiffnessSourceStatus.Missing, FemCsfeaStiffnessSources.Status(source, null, record));
        Assert.Equal(FemCsfeaStiffnessSourceStatus.Missing, FemCsfeaStiffnessSources.Status(source, c, null));
    }

    [Fact]
    public void Crc32_MatchesStandardCheckValue() =>
        Assert.Equal(0xCBF43926u, FemCsfeaStiffnessSources.Crc32("123456789"u8.ToArray()));

    [Theory]
    [InlineData(0.01, 0.01)]
    [InlineData(0.05, 0.05)]
    [InlineData(0.0, 0.01)]
    [InlineData(1.0, 0.01)]
    [InlineData(-0.2, 0.01)]
    public void StiffnessFloor_ResolvesToDefaultOutsideRange(double value, double expected) =>
        Assert.Equal(expected, new CalcSettings { CsfeaStiffnessFloor = value }.ResolveCsfeaStiffnessFloor());

    [Fact]
    public void StiffnessFloor_DefaultsAndClones()
    {
        Assert.Equal(0.01, new CalcSettings().CsfeaStiffnessFloor);
        Assert.Equal(0.03, new CalcSettings { CsfeaStiffnessFloor = 0.03 }.Clone().CsfeaStiffnessFloor);
    }
}
