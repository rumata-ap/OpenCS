using CScore.Fem;
using CScore.Fem.Imperfections;
using CSfea.CScoreBridge.Structural;
using CSfea.Core;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Срез 5 несовершенств: запись форм v2 (6 DOF узлов, double; v1 читается для 3D, для несовершенства — нет), формы
/// постановки устойчивости для несовершенства (<see cref="FemBucklingModes"/>: сверка отпечатка геометрии, α_cr,
/// состояние для диалога), вид «Формы» диалога несовершенства.
/// </summary>
public sealed class FemBucklingModesTests
{
    const double E = 2.0594e11, L = 4.0, Iz = 1e-4, P = 1e5;

    /// <summary>Шарнирная стойка 4 м из одного КЭ (101), сжатая силой P; отпечаток геометрии — <paramref name="geometry"/>.</summary>
    static FemSteelBucklingInput Column(string geometry)
    {
        var section = new RcBeamSection("own") { Elastic = new BeamSection(E, 1e-2, 2 * Iz, Iz), CrossYAlongLocalY = true };
        var m = new RcStructuralModel();
        m.Nodes.Add(new RcNode(1, 0, 0, 0));
        m.Nodes.Add(new RcNode(2, 0, 0, L));
        m.Beams.Add(new RcBeam(101, 1, 2, section, [1.0, 0.0, 0.0]));
        m.Supports.Add(new RcSupport(1, 0b001111));
        m.Supports.Add(new RcSupport(2, 0b000011));
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(2, [0.0, 0, -P, 0, 0, 0]));
        m.LoadCases.Add(lc);
        return new FemSteelBucklingInput(new FemRcModelResult(m, [], []), [new FemBucklingUnit("1", ["101"])], [[101]],
            [new FemRcStage("С1", [(1, 1.0)])], []) { Geometry = geometry };
    }

    [Fact]
    public void Codec_V2_KeepsRotationsInDouble_V1_ReadsWithoutRotations()
    {
        var input = Column("G");
        var r = FemSteelBuckling.Run(input, new RcBucklingOptions(Modes: 2));
        var c = r.Cases[0].Result;
        var mode = FemBucklingRunner.Mode(input, c, c.Modes[0]);
        Assert.True(mode.IsFull);
        // Шарнирные концы полуволны: повороты ±, по модулю π/L·(max|u| = 1) с точностью дробления 4.
        Assert.Equal(Math.PI / L, Math.Abs(mode.Rotations![1].Y), 0.02 * Math.PI / L);
        Assert.Equal(-mode.Rotations[1].Y, mode.Rotations[2].Y, 1e-9);

        var v2 = FemBucklingModeCodec.Decode(FemBucklingModeCodec.Encode(mode));
        Assert.True(v2.IsFull);
        Assert.Equal(mode.BarPoints[101][0].X, v2.BarPoints[101][0].X);      // double — без потерь
        Assert.Equal(mode.Rotations[1].Y, v2.Rotations![1].Y);
        var shape = v2.ToShape()!;
        Assert.Equal(mode.Rotations[1].Y, shape.Nodes[1][4]);
        Assert.Equal(3, shape.BarPoints[101].Length);

        var v1 = FemBucklingModeCodec.Decode(FemBucklingModeCodec.EncodeV1(mode));
        Assert.False(v1.IsFull);
        Assert.Null(v1.ToShape());
        Assert.Equal(mode.BarPoints[101][1].X, v1.BarPoints[101][1].X, 6);
    }

    [Fact]
    public async Task Lookup_ChecksGeometry_AlphaCr_Sources()
    {
        string path = Path.Combine(Path.GetTempPath(), "opencs_modes_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Схема" };
            db.SaveFemSchema(schema);
            var pars = new OpenCS.Tasks.FemAnalysisParams { Buckling = new OpenCS.Tasks.FemBucklingParams { Modes = 2 } };
            var analysis = new FemAnalysis { SchemaId = schema.Id, Tag = "Длины", Kind = FemBucklingRunner.AnalysisKind, ParamsJson = pars.ToJson() };
            db.SaveFemAnalysis(analysis);
            var p = new FemModesParams { AnalysisId = analysis.Id, Modes = [new() { Mode = 1, AmplitudeMm = 10 }] };

            Assert.Equal("imperfection_modes_source", FemBucklingModes.Lookup(db, schema.Id, "G", p).ErrorCode);   // не посчитана
            Assert.Equal(Loc.S("FemImperfectionModesNotComputed"), Assert.Single(FemBucklingModes.Sources(db, schema.Id, "G")).Status);
            Assert.Null(FemBucklingModes.AlphaCr(db, schema.Id, "G"));

            var result = await FemBucklingRunner.SolveAsync(db, analysis, Column("G"), "2026-11-07 12:00:00", CancellationToken.None);
            analysis.ResultId = result.Id;
            db.SaveFemAnalysis(analysis);

            var found = FemBucklingModes.Lookup(db, schema.Id, "G", p);
            Assert.Null(found.ErrorCode);
            Assert.Equal("Длины", found.AnalysisTag);
            Assert.Equal(2, found.Lambda.Count);
            var shape = found.Shapes[1];
            Assert.Equal(1.0, Math.Abs(shape.BarPoints[101][1][0]), 9);       // середина полуволны
            Assert.Equal(6, shape.Nodes[1].Length);

            Assert.Equal("imperfection_modes_stale", FemBucklingModes.Lookup(db, schema.Id, "H", p).ErrorCode);
            Assert.Equal("imperfection_modes_source", FemBucklingModes.Lookup(db, schema.Id, "G", p with { CaseIndex = 3 }).ErrorCode);
            Assert.Equal("imperfection_modes_source", FemBucklingModes.Lookup(db, schema.Id, "G", p with { AnalysisId = 999 }).ErrorCode);

            string alpha = FemBucklingModes.AlphaCr(db, schema.Id, "G")!;
            Assert.Contains("«Длины»", alpha);
            Assert.Null(FemBucklingModes.AlphaCr(db, schema.Id, "H"));

            var source = Assert.Single(FemBucklingModes.Sources(db, schema.Id, "G"));
            Assert.Equal("", source.Status);
            Assert.Equal(found.Lambda, Assert.Single(source.Cases).Lambda);
            Assert.Equal(Loc.S("FemImperfectionModesStale"), Assert.Single(FemBucklingModes.Sources(db, schema.Id, "H")).Status);

            // Резолвер через поиск: сумма с коэффициентом загружения.
            var item = new FemImperfection { Id = 1, Kind = FemImperfectionKinds.Modes, Tag = "Ф", ParamsJson = p.ToJson() };
            var resolved = FemImperfectionResolver.Resolve([(item, -1.0)], [], [], [],
                new FemImperfectionContext { Modes = q => FemBucklingModes.Lookup(db, schema.Id, "G", q) });
            var modes = Assert.IsType<FemResolvedModes>(Assert.Single(resolved.Items));
            Assert.Equal(-0.010 * shape.BarPoints[101][1][0], modes.BarPoints[101][1][0], 12);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    static readonly FemBucklingSourceChoice Source = new(7, "Длины", "",
        [new FemBucklingCaseChoice(0, "C1", [2.5, 4.0]), new FemBucklingCaseChoice(1, "C2", [3.0])]);

    [Fact]
    public void Dialog_Modes_RoundTripAndValidation()
    {
        var p = new FemModesParams { AnalysisId = 7, CaseIndex = 1, Modes = [new() { Mode = 1, AmplitudeMm = -15 }] };
        var item = new FemImperfection { Id = 5, SchemaId = 3, LoadCaseId = 7, Kind = FemImperfectionKinds.Modes, Tag = "Ф", ParamsJson = p.ToJson() };
        var vm = new FemImperfectionDialogVM(item, bucklingSources: [Source]);
        Assert.True(vm.IsModes);
        Assert.False(vm.HasTargets);
        Assert.Equal("C2", vm.BucklingCase!.Tag);
        Assert.Equal(Loc.S("FemImperfectionDialogTitleModes"), vm.Title);
        var row = Assert.Single(vm.ModeRows);
        var q = FemModesParams.Parse(vm.TryBuild(out var error)!.ParamsJson);
        Assert.Null(error);
        Assert.Equal((7, 1, 1, -15.0), (q.AnalysisId, q.CaseIndex, q.Modes[0].Mode, q.Modes[0].AmplitudeMm));

        row.ModeText = "2";                                  // у задачи C2 одна форма
        Assert.Null(vm.TryBuild(out error));
        Assert.NotNull(error);
        vm.BucklingCase = Source.Cases[0];
        Assert.NotNull(vm.TryBuild(out _));
        row.AmplitudeText = "0";
        Assert.Null(vm.TryBuild(out _));
        row.AmplitudeText = "5";
        vm.AddModeCommand.Execute(null);
        Assert.Equal("3", vm.ModeRows[1].ModeText);           // следующий номер
        vm.ModeRows[1].ModeText = "2";
        Assert.Null(vm.TryBuild(out _));                      // амплитуда пуста
        vm.RemoveModeCommand.Execute(vm.ModeRows[1]);
        Assert.Single(vm.ModeRows);

        // Новый элемент вида «Сдвиг» → «Формы»: постановка и задача по умолчанию, одна строка формы.
        var fresh = new FemImperfectionDialogVM(new FemImperfection { Kind = FemImperfectionKinds.Sway, ParamsJson = "{}" }, bucklingSources: [Source]);
        fresh.IsModes = true;
        Assert.Equal(Source, fresh.BucklingSource);
        Assert.Equal("C1", fresh.BucklingCase!.Tag);
        Assert.Single(fresh.ModeRows);
        Assert.NotEmpty(fresh.LambdaText);                    // текст — по ресурсу (в тестах без словаря — ключ)

        // Без постановок — сообщение, OK не проходит.
        var none = new FemImperfectionDialogVM(new FemImperfection { Kind = FemImperfectionKinds.Modes, ParamsJson = "{}" });
        Assert.Equal(Loc.S("FemImperfectionModesNoSources"), none.BucklingStatus);
        Assert.Null(none.TryBuild(out error));
        Assert.Equal(Loc.S("FemImperfectionModesNoSource"), error);
        Assert.NotEqual(item.Kind, FemImperfectionText.Summary(item));     // сводка вида «Формы», не код вида
    }
}
