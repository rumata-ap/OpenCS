using System.Collections.ObjectModel;
using System.Globalization;
using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using CSfea.Core;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit.Abstractions;

namespace OpenCS.Gmsh.Tests;

/// <summary>
/// Исполнитель секущего расчёта CSfea постановки схемы (4д, срез 3): устаревшая сетка строится, расчёт со сводкой и
/// историей контрольного узла, отмена, отказ без переноса вложения SCAD, неизвестный контрольный узел.
/// </summary>
public sealed class FemCsfeaRunnerTests(ITestOutputHelper output)
{
    const string Gmsh = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe";
    const double Q = -10e3;   // Па, вниз

    [Fact]
    public async Task StaleMesh_IsBuilt_AndRunWritesSummary()
    {
        await WithSchema(async (ctx, log, schema, analysis) =>
        {
            Assert.Empty(ctx.Db.GetFemMeshElements(schema.Id));
            var reports = new List<SecantProgress>();

            var result = await FemCsfeaRunner.RunAsync(ctx, schema, analysis, new SyncProgress<SecantProgress>(reports.Add),
                CancellationToken.None);

            var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
            foreach (var line in summary.Report.Concat(summary.Describe())) output.WriteLine(line);
            Assert.Equal(FemCsfeaRunner.TaskKind, result.TaskKind);
            Assert.Equal("ok", result.Status);
            Assert.True(summary.Completed);
            Assert.Contains(ctx.Db.GetFemMeshElements(schema.Id), e => e.ElemType == "shell");
            Assert.Contains(log.LogEntries, e => e.Message.Contains("устарела", StringComparison.Ordinal));
            Assert.Equal(2, summary.Stages.Count);
            Assert.Equal(4.0 * 4.0 * Q * 1.5, summary.Stages.Sum(s => s.Fz), 1e-6 * Math.Abs(Q * 24));
            var ends = summary.Steps.Where(s => s.Converged && !s.Refinement).ToList();
            Assert.Equal(1 + 3, ends.Count);
            Assert.All(summary.Steps, s => Assert.NotNull(s.Control));
            Assert.Equal("5", summary.ControlSchemaNodeTag);
            Assert.NotEqual("", summary.ControlNodeTag);
            Assert.Contains(summary.Describe(), l => l.Contains("узел схемы 5 (сетка ", StringComparison.Ordinal));
            var w = summary.Steps.Select(s => s.Control!.Value).ToList();
            Assert.True(w.Zip(w.Skip(1), (a, b) => b <= a).All(x => x), "прогиб центра растёт по шагам");
            Assert.True(w[^1] < 0);
            Assert.Equal(1.0, reports[^1].Fraction, 12);
        });
    }

    [Fact]
    public async Task Cancel_DuringRun_Throws()
    {
        await WithSchema(async (ctx, _, schema, analysis) =>
        {
            using var cts = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FemCsfeaRunner.RunAsync(ctx, schema, analysis,
                new SyncProgress<SecantProgress>(_ => cts.Cancel()), cts.Token));
        });
    }

    [Fact]
    public async Task ScadAttachmentNotTransferred_IsError()
    {
        await WithSchema(async (ctx, _, schema, analysis) =>
        {
            ctx.Db.SaveFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.ScadAnalysisModel, "model.json", [1, 2, 3]);

            var result = await FemCsfeaRunner.RunAsync(ctx, schema, analysis, null, CancellationToken.None);

            var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
            Assert.Equal("error", result.Status);
            Assert.Contains(summary.Errors, e => e.Contains("Нагрузки из вложения SCAD", StringComparison.Ordinal));
            Assert.Contains(summary.Errors, e => e.Contains("Граничные условия SCAD", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task UnknownControlNode_IsError()
    {
        await WithSchema(async (ctx, _, schema, analysis) =>
        {
            var p = FemAnalysisParams.Parse(analysis.ParamsJson);
            p.Csfea!.ControlNodeTag = "999";
            analysis.ParamsJson = p.ToJson();

            var prepared = await FemCsfeaRunner.PrepareAsync(ctx, schema, analysis, buildMesh: false, CancellationToken.None);

            Assert.True(prepared.HasErrors);
            Assert.Contains(prepared.Report, l => l.Contains("«999»", StringComparison.Ordinal));
            Assert.Contains(prepared.Report, l => l.Contains("устарела", StringComparison.Ordinal));
            Assert.Empty(ctx.Db.GetFemMeshElements(schema.Id));   // проверка входа сетку не пишет
        });
    }

    [Fact]
    public async Task Recording_Final_WritesLastStepOnly()
    {
        await WithSchema(async (ctx, _, schema, analysis) =>
        {
            var result = await FemCsfeaRunner.RunAsync(ctx, schema, analysis, null, CancellationToken.None);

            var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
            var last = summary.Steps.Last(s => s.Converged);
            Assert.Equal([last.N], ctx.Db.GetFemResultStepNumbers(result.Id));
            Assert.Equal([last.N], summary.Steps.Where(s => s.Fields).Select(s => s.N));
            Assert.Equal(4, last.Planned);

            var fields = FemCsfeaStepFieldsCodec.Unpack(ctx.Db.GetFemResultStep(result.Id, last.N)!);
            int node = Array.IndexOf(fields.NodeIds, int.Parse(summary.ControlNodeTag, CultureInfo.InvariantCulture));
            Assert.Equal(last.Control!.Value, fields.Displacements[6 * node + 2], 12);
            Assert.Equal(1, fields.Stage);
            Assert.Equal(1.0, fields.LoadFactor, 12);
            // Плита на угловых шарнирах после q + q2: трещины есть, усилия пластин в выдаче конечны.
            Assert.Contains(fields.ShellFlags, f => (f & CSfea.CScoreBridge.Structural.RcSecantStepFields.Cracked) != 0);
            Assert.All(fields.ShellForces, v => Assert.True(double.IsFinite(v)));

            ctx.Db.DeleteCalcResult(result);
            Assert.Empty(ctx.Db.GetFemResultStepNumbers(result.Id));
        });
    }

    [Fact]
    public async Task Recording_Selected_PlannedStepsAndStageEnds()
    {
        await WithSchema(async (ctx, _, schema, analysis) =>
        {
            var result = await FemCsfeaRunner.RunAsync(ctx, schema, analysis, null, CancellationToken.None);

            var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
            foreach (var s in summary.Steps) output.WriteLine($"{s.N}: стадия {s.Stage}, λ = {s.LoadFactor}, план {s.Planned}, поля {s.Fields}");
            // План: q — 1 шаг (№ 1), q2 — 3 шага (№ 2–4); выбран № 2, концы стадий — № 1 и 4.
            var recorded = summary.Steps.Where(s => s.Fields).Select(s => s.Planned).ToList();
            Assert.Equal([1, 2, 4], recorded);
            Assert.Equal(summary.Steps.Where(s => s.Fields).Select(s => s.N), ctx.Db.GetFemResultStepNumbers(result.Id));
        }, p => { p.ResultRecording = FemCsfeaRecording.Selected; p.RecordSteps = "2"; p.RecordStageEnds = true; });
    }

    [Fact]
    public async Task Recording_All_WritesEveryConvergedStep()
    {
        await WithSchema(async (ctx, _, schema, analysis) =>
        {
            var result = await FemCsfeaRunner.RunAsync(ctx, schema, analysis, null, CancellationToken.None);

            var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
            Assert.Equal(summary.Steps.Where(s => s.Converged).Select(s => s.N), ctx.Db.GetFemResultStepNumbers(result.Id));
        }, p => p.ResultRecording = FemCsfeaRecording.All);
    }

    [Fact]
    public async Task ResultVM_StepsFieldsAndMosaic()
    {
        await WithSchema(async (ctx, _, schema, analysis) =>
        {
            var result = await FemCsfeaRunner.RunAsync(ctx, schema, analysis, null, CancellationToken.None);
            var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;

            var vm = new OpenCS.ViewModels.FemCsfeaResultVM(result, ctx.Db, schema);
            int last = summary.Steps.FindLastIndex(s => s.Converged);
            Assert.Equal(last, vm.SelectedStepIndex);
            Assert.NotNull(vm.Fields);
            Assert.False(vm.HasFieldsNote);
            Assert.True(vm.HasShells);
            int control = int.Parse(summary.ControlNodeTag, CultureInfo.InvariantCulture);
            Assert.Equal(summary.Steps[last].Control!.Value, vm.Displacement(control).Z, 12);
            Assert.Equal(summary.Steps.Count, vm.LambdaPoints.Count);
            Assert.Equal(2.0, vm.LambdaPoints[last].Y, 12);   // стадия 1 + λ = 1

            // Мозаика: все пластины раскрашены, прогиб центра — наибольший по модулю.
            vm.SelectedShellField = vm.ShellFields.Single(f => f.Value == OpenCS.ViewModels.FemCsfeaShellField.Uz);
            Assert.True(vm.IsActive);
            int shells = ctx.Db.GetFemMeshElements(schema.Id).Count(e => e.ElemType == "shell");
            Assert.Equal(shells, vm.Legend.Sum(l => l.Count));
            Assert.Equal(shells * 2, vm.ShellPatches.Sum(p => p.TriangleElements.Count));
            vm.SelectedShellField = vm.ShellFields.Single(f => f.Value == OpenCS.ViewModels.FemCsfeaShellField.State);
            Assert.Contains(vm.Legend, l => l.Count > 0);

            // Шаг без полей (записан только конечный): поля ближайшего записанного и подсказка.
            vm.SelectedStepIndex = 0;
            Assert.True(vm.HasFieldsNote);
            Assert.Equal(last, vm.FieldsStepIndex);
            vm.GoToFieldsStepCommand.Execute(null);
            Assert.Equal(last, vm.SelectedStepIndex);
        });
    }

    sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    sealed class ListLog : ILogService
    {
        public ObservableCollection<LogEntry> LogEntries { get; } = [];
        public void Info(string message) => LogEntries.Add(new(message, LogLevel.Info, DateTime.Now));
        public void Warning(string message) => LogEntries.Add(new(message, LogLevel.Warning, DateTime.Now));
        public void Error(string message) => LogEntries.Add(new(message, LogLevel.Error, DateTime.Now));
    }

    /// <summary>
    /// Схема: плита 4×4 м, h = 0,2 м на шарнирах в углах (узлы схемы 1–4), узел 5 в центре — контрольный; загружения
    /// q (−10 кПа) и q2 (−5 кПа); постановка CSfea: стадии q × 1 шаг и q2 × 3 шага. Сетка схемы не построена.
    /// </summary>
    async Task WithSchema(Func<FemCsfeaRunContext, ListLog, FemSchema, FemAnalysis, Task> body,
        Action<FemCsfeaParams>? tune = null)
    {
        const double a = 4.0, h = 0.2;
        var root = Path.Combine(Path.GetTempPath(), $"opencs-csfea-runner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var schema = new FemSchema { Tag = "slab" };
            db.SaveFemSchema(schema);
            var concrete = MaterialCatalog.CreateHeavyConcrete("B25", CatalogDirectory())!;
            var rebar = MaterialCatalog.CreateRebar("А500С", CatalogDirectory())!;
            db.AddMaterial(concrete);
            db.AddMaterial(rebar);
            double z = h / 2 - 0.03;
            var section = new PlateSection
            {
                Tag = "П", H = h, ConcreteMaterialId = concrete.Id, RebarMaterialId = rebar.Id,
                RebarLayers =
                [
                    new PlateRebarLayer { Name = "низ", InputMode = "direct", Asx = 5e-4, Asy = 5e-4, Zsx = -z, Zsy = -z, Face = RebarFace.MinusN },
                    new PlateRebarLayer { Name = "верх", InputMode = "direct", Asx = 5e-4, Asy = 5e-4, Zsx = z, Zsy = z, Face = RebarFace.PlusN },
                ],
            };
            db.SavePlateSection(section);

            const int pin = 0b000111;
            var nodes = new List<FemNode>
            {
                new() { SchemaId = schema.Id, NodeTag = "1", X = 0, Y = 0, DofMask = pin },
                new() { SchemaId = schema.Id, NodeTag = "2", X = a, Y = 0, DofMask = pin },
                new() { SchemaId = schema.Id, NodeTag = "3", X = a, Y = a, DofMask = pin },
                new() { SchemaId = schema.Id, NodeTag = "4", X = 0, Y = a, DofMask = pin },
                new() { SchemaId = schema.Id, NodeTag = "5", X = a / 2, Y = a / 2 },
            };
            db.SaveFemSchemaEdit(schema.Id, nodes, [], [], [
                new FemLoadCase { SchemaId = schema.Id, Tag = "q", SelfWeightFactor = 0 },
                new FemLoadCase { SchemaId = schema.Id, Tag = "q2", SelfWeightFactor = 0 }], []);
            var cases = db.GetFemLoadCases(schema.Id).OrderBy(c => c.Tag, StringComparer.Ordinal).ToList();

            var frame = new Frame3D(new PlanarVector3(0, 0, 0), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 1, 0), new PlanarVector3(0, 0, 1));
            var region = PlanarRegion.CreateFromContour(new Contour { X = [0, a, a, 0], Y = [0, 0, a, a] }, frame: frame, tag: "P1");
            region.MeshMaxElementSizeM = 0.5;
            db.AddPlanarRegion(region, schema.Id);
            db.SaveFemMember(new FemMember
            {
                SchemaId = schema.Id, ElemTag = "P1", ElemType = "shell", PlanarRegionId = region.Id, PlateSectionId = section.Id,
            });

            FemElementLoad Load(FemLoadCase lc, double q)
            {
                var load = new FemElementLoad
                {
                    SchemaId = schema.Id, LoadCaseId = lc.Id, TargetKind = FemLoadTargetKinds.Members,
                    LoadKind = FemElementLoadKinds.Uniform, Axis = "z",
                };
                load.SetTargetTags(["P1"]);
                load.SetValues([q]);
                return load;
            }
            db.SaveFemLoadCasesAndMeshLoads(schema.Id, cases, [Load(cases[0], Q), Load(cases[1], Q / 2)], []);
            db.LoadAll();
            schema = db.FemSchemas.Single(s => s.Id == schema.Id);

            string Expr(FemLoadCase lc) =>
                new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [lc.Id] }.ToJson();
            var pars = new FemAnalysisParams
            {
                CalcType = CalcType.N,
                Stages =
                [
                    new FemAnalysisStage { Tag = "q", LoadExpressionJson = Expr(cases[0]), LoadFactorStep = 1, MaxLoadFactor = 1 },
                    new FemAnalysisStage { Tag = "q2", LoadExpressionJson = Expr(cases[1]), LoadFactorStep = 1.0 / 3, MaxLoadFactor = 1 },
                ],
                Csfea = new FemCsfeaParams { ControlNodeTag = "5", ControlDof = 2 },
            };
            tune?.Invoke(pars.Csfea);
            var analysis = new FemAnalysis
            {
                SchemaId = schema.Id, Tag = "csfea", Kind = FemCsfeaRunner.AnalysisKind, ParamsJson = pars.ToJson(),
            };
            db.SaveFemAnalysis(analysis);

            var log = new ListLog();
            var gmsh = new GmshSettings
            {
                ExecutablePath = Gmsh, ArtifactsPath = Path.Combine(root, "gmsh"), KeepArtifacts = false,
                ElementMode = PlanarMeshElementMode.Quads, Algorithm = 8,
            };
            await body(new FemCsfeaRunContext(db, gmsh, log), log, schema, analysis);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    static string CatalogDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCS.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "OpenCS", "DataSource");
    }
}
