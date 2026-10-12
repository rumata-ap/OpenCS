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

    /// <summary>
    /// Линейный расчёт: стадии постановки — независимые загружения (q2 = q/2 — прогиб вдвое меньше, а не в полтора раза
    /// больше, как при накоплении), у каждого — свой шаг сводки с полями; окно результата — по загружениям.
    /// </summary>
    [Fact]
    public async Task Linear_IndependentCases_AllRecorded()
    {
        await WithSchema(async (ctx, log, schema, analysis) =>
        {
            analysis.Kind = FemCsfeaRunner.LinearAnalysisKind;
            ctx.Db.SaveFemAnalysis(analysis);

            var result = await FemCsfeaRunner.RunAsync(ctx, schema, analysis, null, CancellationToken.None);

            var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
            foreach (var line in summary.Report.Concat(summary.Describe())) output.WriteLine(line);
            foreach (var e in log.LogEntries) output.WriteLine(e.Message);
            Assert.Equal(FemCsfeaRunner.TaskKind, result.TaskKind);
            Assert.Equal("ok", result.Status);
            Assert.Equal(FemCsfeaRunner.LinearAnalysisKind, summary.Kind);
            Assert.True(summary.Completed);
            Assert.Equal(["q", "q2"], summary.Stages.Select(s => s.Tag));
            Assert.Equal(4.0 * 4.0 * Q, summary.Stages[0].Fz, 1e-6 * Math.Abs(Q * 16));
            Assert.Equal(2, summary.Steps.Count);
            Assert.All(summary.Steps, s => Assert.True(s.Converged && s.Fields && s.LoadFactor == 1.0));
            Assert.Equal([1, 2], ctx.Db.GetFemResultStepNumbers(result.Id));
            double w1 = summary.Steps[0].Control!.Value, w2 = summary.Steps[1].Control!.Value;
            Assert.True(w1 < 0);
            Assert.Equal(0.5, w2 / w1, 9);
            Assert.Contains(summary.Describe(), l => l.StartsWith("Загружение «q2»", StringComparison.Ordinal));
            Assert.Contains(log.LogEntries, e => e.Message.Contains("Линейный расчёт: загружений 2", StringComparison.Ordinal));

            var fields = FemCsfeaStepFieldsCodec.Unpack(ctx.Db.GetFemResultStep(result.Id, 2)!);
            int node = Array.IndexOf(fields.NodeIds, int.Parse(summary.ControlNodeTag, CultureInfo.InvariantCulture));
            Assert.Equal(w2, fields.Displacements[6 * node + 2], 12);
            Assert.All(fields.ShellFlags, f => Assert.Equal(0, f));

            var vm = new OpenCS.ViewModels.FemCsfeaResultVM(result, ctx.Db, schema);
            Assert.True(vm.IsLinear);
            Assert.Equal(1, vm.SelectedStepIndex);
            Assert.False(vm.HasFieldsNote);
            vm.SelectedStepIndex = 0;
            Assert.False(vm.HasFieldsNote);
            Assert.Equal(w1, vm.Displacement(int.Parse(summary.ControlNodeTag, CultureInfo.InvariantCulture)).Z, 12);
        });
    }

    [Fact]
    public async Task ResultVM_StepsFieldsAndTables()
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

            // Поля пластин для мозаики: прогиб и состояние есть у всех пластин.
            var shellTags = ctx.Db.GetFemMeshElements(schema.Id).Where(e => e.ElemType == "shell")
                .Select(e => int.Parse(e.ElemTag, CultureInfo.InvariantCulture)).ToList();
            int shells = shellTags.Count;
            Assert.All(shellTags, t => Assert.NotNull(vm.ShellFieldValue(t, OpenCS.ViewModels.FemCsfeaShellField.Uz)));
            Assert.All(shellTags, t => Assert.InRange(vm.ShellFieldValue(t, OpenCS.ViewModels.FemCsfeaShellField.State)!.Value, 0, 3));

            // Таблицы окна: строка на узел и на пластину, перемещения в мм, усилия в кН — как в полях шага.
            Assert.Equal(vm.Fields!.NodeIds.Length, vm.NodeRows.Count);
            Assert.Equal(shells, vm.ShellRows.Count);
            var controlRow = vm.NodeRows.Single(r => r.Node == control);
            Assert.Equal(summary.Steps[last].Control!.Value * 1e3, controlRow.Uz, 9);
            var shellRow = vm.ShellRows[0];
            Assert.Equal(vm.ShellForces(shellRow.Elem)![3] / 1e3, shellRow.Mx, 12);
            Assert.NotEqual("", shellRow.State);

            // Штрихи трещин мозаики ширины раскрытия: по одному на пластину с раскрытием, середина — в центре КЭ.
            var acrc = OpenCS.ViewModels.FemCsfeaShellField.AcrcLongBottom;
            Assert.True(vm.HasCrackWidths);
            if (!vm.PrepareShellField(acrc)) await vm.CrackWidthsTask;
            Assert.True(vm.PrepareShellField(acrc));
            var cracked = shellTags.Where(t => vm.ShellFieldValue(t, acrc) > 0).ToList();
            var nodes = ctx.Db.GetFemMeshNodes(schema.Id).ToDictionary(n => int.Parse(n.NodeTag, CultureInfo.InvariantCulture),
                n => new System.Windows.Media.Media3D.Point3D(n.X, n.Y, n.Z));
            var strokes = vm.CrackStrokes(acrc, shellTags, n => nodes[n]);
            Assert.NotEmpty(cracked);
            Assert.Equal(2 * cracked.Count, strokes.Count);
            var contours = ctx.Db.GetFemMeshElements(schema.Id).Where(e => e.ElemType == "shell").ToDictionary(
                e => int.Parse(e.ElemTag, CultureInfo.InvariantCulture),
                e => System.Text.Json.JsonSerializer.Deserialize<int[]>(e.NodeIdsJson)!);
            for (int k = 0; k < cracked.Count; k++)
            {
                var c = contours[cracked[k]];
                var center = new System.Windows.Media.Media3D.Point3D(c.Average(n => nodes[n].X), c.Average(n => nodes[n].Y),
                    c.Average(n => nodes[n].Z));
                Assert.True((strokes[2 * k] + (strokes[2 * k + 1] - strokes[2 * k]) / 2 - center).Length < 1e-9);
            }
            Assert.Empty(vm.CrackStrokes(OpenCS.ViewModels.FemCsfeaShellField.Uz, shellTags, n => nodes[n]));

            // Шаг → наборы усилий: пластины, строка на КЭ, усилия в кН — как в полях шага.
            Assert.True(vm.CanCreateForceSets);
            var sets = vm.BuildForceSets([]);
            var plates = Assert.Single(sets);
            Assert.Equal("shell", plates.Kind);
            Assert.Equal(schema.Id, plates.SourceSchemaId);
            Assert.Equal(shells, plates.ShellItems.Count);
            var row = plates.ShellItems[0];
            var forces = vm.ShellForces(row.SourceElementNum!.Value)!;
            Assert.Equal(forces[3] / 1e3, row.Mx, 12);
            Assert.Equal(forces[7] / 1e3, row.Qy, 12);

            // Шаг без полей (записан только конечный): поля ближайшего записанного и подсказка.
            vm.SelectedStepIndex = 0;
            Assert.False(vm.CanCreateForceSets);
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
