using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
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
            // Реакции: у каждого сошедшегося шага в сводке ΣR уравновешивает ΣF; в полях — угловые опоры.
            Assert.All(summary.Steps.Where(s => s.Converged), s =>
                Assert.True(Math.Abs(s.ReactionTotal![2] + s.LoadSum![2]) <= 1e-6 * Math.Abs(s.LoadSum[2]), $"шаг {s.N}"));
            Assert.Equal(4, fields.ReactionNodeIds.Length);
            Assert.Equal(-fields.LoadSum[2], Enumerable.Range(0, 4).Sum(i => fields.Reactions[6 * i + 2]), 1e-6 * Math.Abs(fields.LoadSum[2]));
            // Конечное состояние: жёсткости и полные секущие матрицы, деформаций без галки нет; у треснувших КЭ D11 < D11₀.
            Assert.True(fields.HasStiffness && fields.HasMatrices && !fields.HasStrains);
            const int k = CSfea.CScoreBridge.Structural.RcSecantStepFields.ShellStiffnessComponents;
            Assert.Equal(fields.ShellIds.Length * k, fields.ShellStiffness.Length);
            Assert.Contains(Enumerable.Range(0, fields.ShellIds.Length), e => fields.ShellStiffness[e * k + 4] < 0.9 * fields.ShellStiffness[e * k + 12]);

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
            Assert.Equal(-fields.LoadSum[2], fields.ReactionSums[2], 1e-9 * Math.Abs(fields.LoadSum[2]));
            Assert.Equal(fields.ReactionSums, summary.Steps[1].ReactionSums);

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

    // ------------------------------------------------------------ пакет вариантов (спека 2026-10-17)

    /// <summary>Пакет: q (комбинация C), q + q2 (комбинация C), q2 (загружение); шаг 0,5, предел 1.</summary>
    static void MakeBatch(DatabaseService db, FemSchema schema, FemAnalysis analysis, string recording = FemCsfeaBatchRecording.Fields)
    {
        var cases = db.GetFemLoadCases(schema.Id).OrderBy(c => c.Tag, StringComparer.Ordinal).ToList();
        string Sum(params (FemLoadCase Case, double K)[] terms) => new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum,
            Terms = terms.Select(t => new FemLoadTerm { LoadCaseId = t.Case.Id, Coefficient = t.K }).ToList(),
        }.ToJson();
        var pars = FemAnalysisParams.Parse(analysis.ParamsJson);
        pars.Batch = new FemCsfeaBatch
        {
            Items =
            [
                new() { Tag = "C1", LoadExpressionJson = Sum((cases[0], 1)), Type = "C" },
                new() { Tag = "C2", LoadExpressionJson = Sum((cases[0], 1), (cases[1], 1)), Type = "C" },
                new()
                {
                    Tag = "q2", Kind = FemCsfeaBatchItemKind.LoadCase, Type = "Q",
                    LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [cases[1].Id] }.ToJson(),
                },
            ],
            LoadFactorStep = 0.5, MaxLoadFactor = 1, Recording = recording,
        };
        analysis.ParamsJson = pars.ToJson();
        db.SaveFemAnalysis(analysis);
    }

    static CSfea.CScoreBridge.Structural.RcSecantStepFields Fields(DatabaseService db, int resultId, int n) =>
        FemCsfeaStepFieldsCodec.Unpack(db.GetFemResultStep(resultId, n)!);

    [Fact]
    public async Task Batch_EachVariantMatchesSeparateSingleStageRun()
    {
        await WithSchema(async (ctx, log, schema, analysis) =>
        {
            MakeBatch(ctx.Db, schema, analysis);
            CalcResult? attached = null;

            var result = await FemCsfeaRunner.RunBatchAsync(ctx, schema, analysis, null, null, r => attached = r, CancellationToken.None);

            var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
            foreach (var line in summary.Describe()) output.WriteLine(line);
            Assert.Same(result, attached);
            Assert.Equal("ok", result.Status);
            Assert.Equal(3, summary.Batch!.Count);
            Assert.All(summary.Batch, b => { Assert.True(b.Done); Assert.True(b.Completed); Assert.Equal(1.0, b.LoadFactor, 12); });
            Assert.Equal([1, 2, 3], summary.Batch.Select(b => b.RecordN!.Value));
            Assert.Equal([1, 2, 3], ctx.Db.GetFemResultStepNumbers(result.Id));
            Assert.Equal(16 * Q * 1.5, summary.Batch[1].Fz, 1e-6 * Math.Abs(Q * 24));
            Assert.Contains(log.LogEntries, e => e.Message.Contains("Вариант 3 из 3 «q2»", StringComparison.Ordinal));

            // Тот же вариант отдельной постановкой одной стадией — те же поля.
            var pars = FemAnalysisParams.Parse(analysis.ParamsJson);
            var item = pars.Batch!.Items[1];
            pars.Batch = null;
            pars.Stages = [new FemAnalysisStage { Tag = item.Tag, LoadExpressionJson = item.LoadExpressionJson, LoadFactorStep = 0.5, MaxLoadFactor = 1 }];
            var single = new FemAnalysis { SchemaId = schema.Id, Tag = "single", Kind = FemCsfeaRunner.AnalysisKind, ParamsJson = pars.ToJson() };
            var singleResult = await FemCsfeaRunner.RunAsync(ctx, schema, single, null, CancellationToken.None);
            var expected = Fields(ctx.Db, singleResult.Id, ctx.Db.GetFemResultStepNumbers(singleResult.Id).Max());
            var actual = Fields(ctx.Db, result.Id, 2);
            double scale = expected.Displacements.Max(Math.Abs);
            Assert.Equal(expected.NodeIds, actual.NodeIds);
            for (int i = 0; i < expected.Displacements.Length; i++)
                Assert.Equal(expected.Displacements[i], actual.Displacements[i], 1e-9 * scale);
        });
    }

    [Fact]
    public async Task Batch_ForceSetsPerVariant_GroupC_TwoVariantsByThreeSelectedElements()
    {
        await WithSchema(async (ctx, _, schema, analysis) =>
        {
            MakeBatch(ctx.Db, schema, analysis);
            var result = await FemCsfeaRunner.RunBatchAsync(ctx, schema, analysis, null, null, null, CancellationToken.None);
            var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
            var first = Fields(ctx.Db, result.Id, 1);
            var picked = first.ShellIds.Take(3).ToArray();

            var built = FemCsfeaBatchForceSets.Build(schema, analysis.Tag, summary, n => Fields(ctx.Db, result.Id, n), ["C"], [],
                new FemCsfeaForceSetScope(picked, []), [], null, CancellationToken.None);

            var set = Assert.Single(built.Sets);
            output.WriteLine(set.Tag);
            Assert.Empty(built.Warnings);
            Assert.EndsWith("— C, 2 вар. — пластины [выбор: 3 КЭ] (C)", set.Tag);
            Assert.Equal(2 * 3, set.ShellItems.Count);
            Assert.Equal(picked.Select(e => $"{e} · C1").Concat(picked.Select(e => $"{e} · C2")),
                set.ShellItems.Select(r => r.Label));
            Assert.Equal(first.ShellForces[3] / 1e3, set.ShellItems[0].Mx, 12);
            var second = Fields(ctx.Db, result.Id, 2);
            Assert.Equal(second.ShellForces[3] / 1e3, set.ShellItems[3].Mx, 12);
            Assert.NotEqual(set.ShellItems[0].Mx, set.ShellItems[3].Mx);

            // Окно результата: команда доступна, сетка для оценки строк — по полям показанного варианта.
            var vm = new OpenCS.ViewModels.FemCsfeaResultVM(result, ctx.Db, schema, analysis);
            Assert.True(vm.CanCreateBatchForceSets);
            var mesh = vm.BatchMesh()!.Value;
            Assert.Equal(first.ShellIds.Length, mesh.Shells.Count);
            Assert.Equal(first.BeamIds.Length, mesh.Beams.Count);
        });
    }

    /// <summary>
    /// Сквозная проверка (срез 3): rc_plate_check по КЭ по набору «строка на вариант» — строки всех вариантов
    /// считаются, определяющая метка «КЭ · вариант»; φ1 «авто» без парного NL-набора — замечание, не ошибка, с парой
    /// (группа загружений, типы N и NL — метки совпадают) — без замечания.
    /// </summary>
    [Fact]
    public async Task Batch_ForceSetsPerVariant_PlateCheckByElements()
    {
        await WithSchema(async (ctx, _, schema, analysis) =>
        {
            MakeBatch(ctx.Db, schema, analysis);
            var result = await FemCsfeaRunner.RunBatchAsync(ctx, schema, analysis, null, null, null, CancellationToken.None);
            var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
            var first = Fields(ctx.Db, result.Id, 1);
            // Три КЭ с наибольшим |Mx| — у них определяющий вариант очевиден (C2 = 1,5 × C1).
            int stride = CSfea.CScoreBridge.Structural.RcSecantStepFields.ShellForceComponents;
            var picked = first.ShellIds.Select((id, i) => (id, Mx: Math.Abs(first.ShellForces[i * stride + 3])))
                .OrderByDescending(x => x.Mx).Take(3).Select(x => x.id).ToArray();
            var scopeSet = new FemCsfeaForceSetScope(picked, []);
            var fs = FemCsfeaBatchForceSets.Build(schema, analysis.Tag, summary, n => Fields(ctx.Db, result.Id, n), ["C"], [],
                scopeSet, [], null, CancellationToken.None).Sets.Single();
            fs.Id = 1;

            var data = FemCheckSchemaData.Load(ctx.Db, schema.Id);
            var target = data.Members.Single(m => m.ElemTag == "P1");
            var scope = data.Scope(target);
            var section = ctx.Db.PlateSections.Single();
            var inputs = new FemPerElementInputs
            {
                PlateTemplate = section,
                ConcreteMat = ctx.Db.Materials.First(m => m.Id == section.ConcreteMaterialId),
                RebarMat = ctx.Db.Materials.First(m => m.Id == section.RebarMaterialId),
            };
            // LoadAll без очистки коллекций дублирует добавленные материалы — отсюда First.
            Assert.Equal(MatType.Concrete, inputs.ConcreteMat.Type);
            Assert.NotEqual(MatType.Concrete, inputs.RebarMat.Type);
            FemCheck Check(string kind) => new()
            {
                NormCode = "rc_plate_check", Tag = kind,
                ParamsJson = new PlateCheckParams { Kind = kind, Phi1Mode = "auto", Phi1 = 1.4 }.ToJson(),
            };
            CalcResult Run(FemCheck check, params ForceSet[] sets) => FemCheckRunner.RunPerElement(check, target, scope, sets,
                inputs, (_, _, _) => throw new InvalidOperationException("стержневой исполнитель не нужен"));
            static List<string> Warnings(JsonDocument doc) => doc.RootElement.GetProperty("summary").GetProperty("warnings")
                .EnumerateArray().Select(w => w.GetString()!).ToList();

            var uls = Run(Check("shell_simpl_wa_uls"), fs);

            output.WriteLine(uls.DataJson);
            Assert.NotEqual("error", uls.Status);
            Assert.Equal(2 * 3, uls.FemCheckRows!.Count);
            Assert.All(uls.FemCheckRows, r => Assert.False(r.NotChecked, r.WorstDescription));
            Assert.Equal(picked.SelectMany(e => new[] { $"{e} · C1", $"{e} · C2" }).Order(),
                uls.FemCheckRows.Select(r => r.Label).Order());
            using (var doc = JsonDocument.Parse(uls.DataJson))
            {
                var elements = doc.RootElement.GetProperty("elements").EnumerateArray()
                    .Where(e => e.GetProperty("status").GetString() != "no_forces").ToList();
                Assert.Equal(picked.Order(), elements.Select(e => e.GetProperty("elemNum").GetInt32()).Order());
                Assert.All(elements, e =>
                {
                    Assert.Equal(2, e.GetProperty("rows").GetInt32());
                    Assert.Equal($"{e.GetProperty("elemNum").GetInt32()} · C2", e.GetProperty("label").GetString());
                    Assert.Equal(fs.Tag, e.GetProperty("forceSetTag").GetString());
                });
                Assert.DoesNotContain(Warnings(doc), w => w.Contains("φ1"));
            }

            // Трещины, φ1 «авто»: набор «(N)» без парного «(NL)» — замечание, строки посчитаны с φ1 из параметров.
            var nOnly = FemCsfeaBatchForceSets.Build(schema, analysis.Tag, summary, n => Fields(ctx.Db, result.Id, n),
                [FemCsfeaBatchGroup.LoadCases], ["N"], scopeSet, [], null, CancellationToken.None).Sets.Single();
            nOnly.Id = 2;
            var sls = Run(Check("shell_simpl_wa_sls"), nOnly);
            Assert.NotEqual("error", sls.Status);
            Assert.Equal(3, sls.FemCheckRows!.Count);
            Assert.All(sls.FemCheckRows, r => Assert.False(r.NotChecked, r.WorstDescription));
            using (var doc = JsonDocument.Parse(sls.DataJson))
                Assert.Contains(Warnings(doc), w => w.StartsWith("φ1 «авто» (п. 8.2.7): у 3 строк", StringComparison.Ordinal));

            // Те же загружения с типами N и NL: метки строк совпадают — пара находится, замечания нет.
            var pair = FemCsfeaBatchForceSets.Build(schema, analysis.Tag, summary, n => Fields(ctx.Db, result.Id, n),
                [FemCsfeaBatchGroup.LoadCases], ["N", "NL"], scopeSet, [], null, CancellationToken.None).Sets;
            Assert.Equal(2, pair.Count);
            for (int i = 0; i < pair.Count; i++) pair[i].Id = 10 + i;
            var slsPair = Run(Check("shell_simpl_wa_sls"), [.. pair]);
            Assert.Equal(2 * 3, slsPair.FemCheckRows!.Count);
            using (var doc = JsonDocument.Parse(slsPair.DataJson))
                Assert.DoesNotContain(Warnings(doc), w => w.Contains("φ1"));
        });
    }

    [Fact]
    public async Task Batch_CancelKeepsDone_ResumeComputesRest()
    {
        await WithSchema(async (ctx, _, schema, analysis) =>
        {
            MakeBatch(ctx.Db, schema, analysis);
            using var cts = new CancellationTokenSource();

            var partial = await FemCsfeaRunner.RunBatchAsync(ctx, schema, analysis, null,
                new SyncProgress<FemCsfeaBatchProgress>(p => { if (p.Number == 2) cts.Cancel(); }), null, cts.Token);

            var summary = FemCsfeaResultSummary.Parse(partial.DataJson)!;
            foreach (var line in summary.Describe()) output.WriteLine(line);
            Assert.Equal("partial", partial.Status);
            Assert.True(summary.Batch![0].Done);
            Assert.Contains(summary.Batch, b => !b.Done);
            Assert.Contains(summary.Describe(), l => l.StartsWith("Не посчитаны:", StringComparison.Ordinal));
            int firstRecord = summary.Batch[0].RecordN!.Value;

            var full = await FemCsfeaRunner.RunBatchAsync(ctx, schema, analysis, partial, null, null, CancellationToken.None);

            var done = FemCsfeaResultSummary.Parse(full.DataJson)!;
            Assert.Equal(partial.Id, full.Id);
            Assert.Equal("ok", full.Status);
            Assert.All(done.Batch!, b => Assert.True(b.Done));
            Assert.Equal(firstRecord, done.Batch[0].RecordN);
            Assert.Equal(3, ctx.Db.GetFemResultStepNumbers(full.Id).Count);

            // Вариант убран из пакета — при досчёте его запись удаляется.
            var pars = FemAnalysisParams.Parse(analysis.ParamsJson);
            pars.Batch!.Items.RemoveAt(2);
            analysis.ParamsJson = pars.ToJson();
            var trimmed = await FemCsfeaRunner.RunBatchAsync(ctx, schema, analysis, full, null, null, CancellationToken.None);
            Assert.Equal(2, FemCsfeaResultSummary.Parse(trimmed.DataJson)!.Batch!.Count);
            Assert.Equal(2, ctx.Db.GetFemResultStepNumbers(trimmed.Id).Count);
        });
    }

    [Fact]
    public async Task Batch_EnvelopesOnly_EqualMinMaxOfVariantFields()
    {
        await WithSchema(async (ctx, _, schema, analysis) =>
        {
            MakeBatch(ctx.Db, schema, analysis);
            var withFields = await FemCsfeaRunner.RunBatchAsync(ctx, schema, analysis, null, null, null, CancellationToken.None);
            MakeBatch(ctx.Db, schema, analysis, FemCsfeaBatchRecording.Envelopes);

            var result = await FemCsfeaRunner.RunBatchAsync(ctx, schema, analysis, null, null, null, CancellationToken.None);

            var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
            Assert.Equal("ok", result.Status);
            Assert.All(summary.Batch!, b => Assert.Null(b.RecordN));
            Assert.False(new OpenCS.ViewModels.FemCsfeaResultVM(result, ctx.Db, schema, analysis).CanCreateBatchForceSets);
            Assert.Equal(["C", FemCsfeaBatchGroup.LoadCases], summary.BatchGroups!.Select(g => g.Key));
            Assert.Equal([2, 1], summary.BatchGroups.Select(g => g.Count));
            var (env, reference) = FemCsfeaEnvelopeCodec.Unpack(ctx.Db.GetFemResultStep(result.Id, summary.BatchGroups[0].RecordN)!);
            Assert.Equal(2, env.Count);
            var a = Fields(ctx.Db, withFields.Id, 1);
            var b = Fields(ctx.Db, withFields.Id, 2);
            Assert.Equal(a.NodeIds, reference.NodeIds);
            const int nq = FemCsfeaEnvelopeQuantities.NodeQ, nc = 6;
            double scale = a.Displacements.Concat(b.Displacements).Max(Math.Abs);
            for (int i = 0; i < a.NodeIds.Length; i++)
            {
                double ua = a.Displacements[nc * i + 2], ub = b.Displacements[nc * i + 2];
                Assert.Equal(Math.Min(ua, ub), env.Min[i * nq + 2], 1e-9 * scale);
                Assert.Equal(Math.Max(ua, ub), env.Max[i * nq + 2], 1e-9 * scale);
                if (ua != ub) Assert.Equal(ua < ub ? 0 : 1, env.MinSource[i * nq + 2]);
            }

            // Трещины пластин (вид N): огибающая окна по полям вариантов (с a_crc при доле 0,9) = перебор по вариантам =
            // записанная огибающая.
            Assert.Equal(0.9, summary.BatchCrackLongShare);
            var vm = new OpenCS.ViewModels.FemCsfeaResultVM(withFields, ctx.Db, schema, analysis);
            Assert.Equal(0.9, vm.LongShare);
            vm.ShowEnvelopes = true;
            await vm.EnvelopeTask;
            var envelopeItem = vm.SelectedCaseItem!;
            Assert.Equal("C", envelopeItem.Envelope!.Type);
            const OpenCS.ViewModels.FemCsfeaShellField acrc = OpenCS.ViewModels.FemCsfeaShellField.AcrcShortBottom,
                crack = OpenCS.ViewModels.FemCsfeaShellField.CrackBottom;
            Assert.False(vm.PrepareShellField(acrc));
            await vm.EnvelopeTask;
            Assert.True(vm.PrepareShellField(acrc));
            var shells = reference.ShellIds;
            var envAcrc = shells.ToDictionary(s => s, s => vm.EnvelopeShell(s, acrc)!.Value.Max);
            var envCrack = shells.ToDictionary(s => s, s => vm.EnvelopeShell(s, crack)!.Value.Max);
            Assert.Contains(envAcrc.Values, v => v > 0);

            var maxAcrc = shells.ToDictionary(s => s, _ => 0.0);
            var maxCrack = shells.ToDictionary(s => s, _ => double.NegativeInfinity);
            foreach (var variant in envelopeItem.Envelope.Combinations)
            {
                vm.SelectedCaseItem = variant;
                if (!vm.PrepareShellField(acrc)) await vm.CrackWidthsTask;
                foreach (int s in shells)
                {
                    maxAcrc[s] = Math.Max(maxAcrc[s], vm.ShellFieldValue(s, acrc) ?? 0);
                    maxCrack[s] = Math.Max(maxCrack[s], vm.ShellFieldValue(s, crack)!.Value);
                }
            }
            int co = FemCsfeaEnvelopeQuantities.Offsets(reference).Crack;
            for (int i = 0; i < shells.Length; i++)
            {
                int s = shells[i];
                Assert.Equal(maxAcrc[s], envAcrc[s], 1e-9);
                Assert.Equal(maxCrack[s], envCrack[s], 1e-12);
                // Записанная огибающая — по полям в double, поля вариантов в БД — во float.
                Assert.Equal(envAcrc[s], env.Max[co + i * FemCsfeaEnvelopeQuantities.CrackQ + 4], 1e-5);
                Assert.Equal(envCrack[s], env.Max[co + i * FemCsfeaEnvelopeQuantities.CrackQ], 1e-6);
            }
        });
    }

    /// <summary>
    /// Пакет длительных частей: q — постоянное, q2 — кратковременное с долей 0,5; N1 = q + q2, N2 = q, загружение q2.
    /// </summary>
    static void SetLongTermBatch(DatabaseService db, FemSchema schema, FemAnalysis analysis, bool pairs, bool deflection)
    {
        var cases = db.GetFemLoadCases(schema.Id).OrderBy(c => c.Tag, StringComparer.Ordinal).ToList();
        cases[0].Sp20Type = "permanent";
        cases[1].LongTermShare = 0.5;
        foreach (var c in cases) db.SaveFemLoadCase(c);
        string Sum(params (FemLoadCase Case, double K)[] terms) => new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum, CombinationType = "N",
            Terms = terms.Select(t => new FemLoadTerm { LoadCaseId = t.Case.Id, Coefficient = t.K }).ToList(),
        }.ToJson();
        var pars = FemAnalysisParams.Parse(analysis.ParamsJson);
        pars.Batch = new FemCsfeaBatch
        {
            Items =
            [
                new() { Tag = "N1", LoadExpressionJson = Sum((cases[0], 1), (cases[1], 1)), Type = "N" },
                new() { Tag = "N2", LoadExpressionJson = Sum((cases[0], 1)), Type = "N" },
                new()
                {
                    Tag = "q2", Kind = FemCsfeaBatchItemKind.LoadCase, Type = "Q",
                    LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [cases[1].Id] }.ToJson(),
                },
            ],
            LoadFactorStep = 0.5, MaxLoadFactor = 1,
            LongTermPairs = pairs ? ["N"] : [], LongTermDeflection = deflection,
        };
        analysis.ParamsJson = pars.ToJson();
    }

    static double MaxUz(CSfea.CScoreBridge.Structural.RcSecantStepFields f) =>
        Enumerable.Range(0, f.NodeIds.Length).Max(i => Math.Abs(f.Displacements[6 * i + 2]));

    /// <summary>
    /// Срез 5: наборы «строка на вариант» группы N с парами — «(N)» и «(NL)» с той же основой имени, строки NL — поля пар
    /// (N2 — поля варианта), метки — от вариантов; rc_plate_check с φ1 «авто» находит пару — замечания нет. Диалог:
    /// отметка длительных частей, без неё — предупреждение о связке и один набор.
    /// </summary>
    [Fact]
    public async Task Batch_LongTermForceSets_PairFoundByPlateCheck()
    {
        await WithSchema(async (ctx, _, schema, analysis) =>
        {
            SetLongTermBatch(ctx.Db, schema, analysis, pairs: true, deflection: false);
            var result = await FemCsfeaRunner.RunBatchAsync(ctx, schema, analysis, null, null, null, CancellationToken.None);
            var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
            var first = Fields(ctx.Db, result.Id, summary.Batch![0].RecordN!.Value);
            int stride = CSfea.CScoreBridge.Structural.RcSecantStepFields.ShellForceComponents;
            var picked = first.ShellIds.Select((id, i) => (id, Mx: Math.Abs(first.ShellForces[i * stride + 3])))
                .OrderByDescending(x => x.Mx).Take(3).Select(x => x.id).ToArray();
            var scopeSet = new FemCsfeaForceSetScope(picked, []);

            var built = FemCsfeaBatchForceSets.Build(schema, analysis.Tag, summary, n => Fields(ctx.Db, result.Id, n), ["N"], [],
                scopeSet, [], null, CancellationToken.None);

            Assert.Empty(built.Warnings);
            Assert.Equal(2, built.Sets.Count);
            var (nSet, nlSet) = (built.Sets[0], built.Sets[1]);
            Assert.EndsWith("(N)", nSet.Tag);
            Assert.Equal(nSet.Tag[..^3] + "(NL)", nlSet.Tag);
            Assert.Equal(nSet.ShellItems.Select(r => r.Label), nlSet.ShellItems.Select(r => r.Label));
            var pairFields = Fields(ctx.Db, result.Id, summary.LongOf(0)!.RecordN!.Value);
            int i0 = Array.IndexOf(pairFields.ShellIds, picked[0]);
            Assert.Equal(pairFields.ShellForces[i0 * stride + 3] / 1e3, nlSet.ShellItems[0].Mx, 9);
            // N2 = q: длительная часть совпадает с полной — строки те же.
            Assert.Equal(nSet.ShellItems[3].Mx, nlSet.ShellItems[3].Mx, 12);
            Assert.True(Math.Abs(nlSet.ShellItems[0].Mx) < Math.Abs(nSet.ShellItems[0].Mx));

            nSet.Id = 1;
            nlSet.Id = 2;
            var data = FemCheckSchemaData.Load(ctx.Db, schema.Id);
            var target = data.Members.Single(m => m.ElemTag == "P1");
            var section = ctx.Db.PlateSections.Single();
            var inputs = new FemPerElementInputs
            {
                PlateTemplate = section,
                ConcreteMat = ctx.Db.Materials.First(m => m.Id == section.ConcreteMaterialId),
                RebarMat = ctx.Db.Materials.First(m => m.Id == section.RebarMaterialId),
            };
            var check = new FemCheck
            {
                NormCode = "rc_plate_check", Tag = "sls",
                ParamsJson = new PlateCheckParams { Kind = "shell_simpl_wa_sls", Phi1Mode = "auto", Phi1 = 1.4 }.ToJson(),
            };
            var sls = FemCheckRunner.RunPerElement(check, target, data.Scope(target), [nSet, nlSet], inputs,
                (_, _, _) => throw new InvalidOperationException("стержневой исполнитель не нужен"));
            Assert.NotEqual("error", sls.Status);
            using (var doc = JsonDocument.Parse(sls.DataJson))
                Assert.DoesNotContain(doc.RootElement.GetProperty("summary").GetProperty("warnings").EnumerateArray()
                    .Select(w => w.GetString()!), w => w.Contains("φ1"));

            // Диалог: у группы N — отметка длительных частей (2 из 2); снята — предупреждение и наборов без NL.
            var vm =new OpenCS.ViewModels.FemCsfeaBatchForceSetsVM(summary, analysis.Tag, first.ShellIds.Length, 0);
            var nGroup = vm.Groups.Single(g => g.Key == "N");
            Assert.True(nGroup.HasLong);
            Assert.Equal(2, nGroup.LongIncluded);
            Assert.Equal(["N"], vm.LongGroups);
            Assert.False(vm.ShowPairWarning);
            Assert.Equal(2, vm.SetCount);
            nGroup.IncludeLong = false;
            Assert.True(vm.ShowPairWarning);
            Assert.Equal(1, vm.SetCount);
            Assert.Single(FemCsfeaBatchForceSets.Build(schema, analysis.Tag, summary, n => Fields(ctx.Db, result.Id, n), ["N"], [],
                scopeSet, [], null, CancellationToken.None, vm.LongGroups).Sets);
        });
    }

    /// <summary>
    /// Срез 3: длительная часть на диаграммах NL — вторая модель; у N1 — стадия пары, у N2 (совпадает с полной) — стадия
    /// варианта; прогиб на диаграммах NL больше, чем от той же нагрузки на диаграммах варианта (N).
    /// </summary>
    [Fact]
    public async Task Batch_LongTermDeflection_SecondModelOnNLDiagrams()
    {
        await WithSchema(async (ctx, log, schema, analysis) =>
        {
            SetLongTermBatch(ctx.Db, schema, analysis, pairs: true, deflection: true);

            var result = await FemCsfeaRunner.RunBatchAsync(ctx, schema, analysis, null, null, null, CancellationToken.None);

            var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
            foreach (var line in summary.Describe()) output.WriteLine(line);
            Assert.Equal("ok", result.Status);
            Assert.Equal(4, summary.BatchLong!.Count);
            var d1 = summary.LongOf(0, FemCsfeaBatchLongKind.Deflection)!;
            var d2 = summary.LongOf(1, FemCsfeaBatchLongKind.Deflection)!;
            Assert.Equal(3, d1.Stage);
            Assert.Equal(1, d2.Stage);
            Assert.All(new[] { d1, d2 }, d => { Assert.True(d.Done && d.Completed); Assert.NotNull(d.RecordN); });
            Assert.Null(summary.LongOf(2, FemCsfeaBatchLongKind.Deflection));
            Assert.Equal(6, ctx.Db.GetFemResultStepNumbers(result.Id).Count);
            Assert.Contains(summary.Describe(), l => l.StartsWith("Длительные части на диаграммах NL (полный прогиб): посчитаны 2.", StringComparison.Ordinal));

            double u2 = MaxUz(Fields(ctx.Db, result.Id, summary.LongOf(0)!.RecordN!.Value));
            double u3 = MaxUz(Fields(ctx.Db, result.Id, d1.RecordN!.Value));
            output.WriteLine($"N1: u2 = {u2 * 1e3:0.###} мм, u3 = {u3 * 1e3:0.###} мм");
            Assert.True(u3 > u2 * 1.05, $"u3 = {u3}, u2 = {u2}");
            double u2Same = MaxUz(Fields(ctx.Db, result.Id, summary.Batch![1].RecordN!.Value));
            Assert.True(MaxUz(Fields(ctx.Db, result.Id, d2.RecordN!.Value)) > u2Same * 1.05);

            // Срез 4 — окно результата: режимы длительной части, полный прогиб u₁ − u₂ + u₃, ключ показа, огибающая пар.
            var vm = new OpenCS.ViewModels.FemCsfeaResultVM(result, ctx.Db, schema, analysis);
            var n1 = vm.CombinationItems.First(i => i.Batch!.Index == 0);
            vm.SelectedCaseItem = n1;
            Assert.Equal([OpenCS.ViewModels.FemCsfeaLongMode.Total, OpenCS.ViewModels.FemCsfeaLongMode.Long,
                OpenCS.ViewModels.FemCsfeaLongMode.LongNL, OpenCS.ViewModels.FemCsfeaLongMode.FullDeflection],
                vm.LongModeOptions.Select(o => o.Mode));
            Assert.True(vm.CrackByPair);
            vm.LongMode = OpenCS.ViewModels.FemCsfeaLongMode.FullDeflection;
            var u1 = Fields(ctx.Db, result.Id, summary.Batch[0].RecordN!.Value).Displacements;
            var u2f = Fields(ctx.Db, result.Id, summary.LongOf(0)!.RecordN!.Value).Displacements;
            var u3f = Fields(ctx.Db, result.Id, d1.RecordN!.Value).Displacements;
            for (int i = 0; i < u1.Length; i++) Assert.Equal(u1[i] - u2f[i] + u3f[i], vm.Fields!.Displacements[i], 12);
            Assert.All(vm.Fields!.ShellForces, v => Assert.Equal(0, v));
            Assert.False(vm.CrackByPair);
            Assert.Equal(OpenCS.ViewModels.FemCsfeaLongMode.FullDeflection, vm.ShownKey!.LongMode);
            vm.LongMode = OpenCS.ViewModels.FemCsfeaLongMode.Long;
            Assert.Equal(u2f, vm.Fields!.Displacements);
            // Загружение без длительной части — только «Полные»; возврат к N1 — режим запомнен.
            vm.SelectedCaseItem = vm.CombinationItems.First(i => i.Batch!.Index == 2);
            Assert.False(vm.HasLongModes);
            Assert.Equal(u1.Length, vm.Fields!.Displacements.Length);
            vm.SelectedCaseItem = n1;
            Assert.Equal(OpenCS.ViewModels.FemCsfeaLongMode.Long, vm.SelectedLongModeOption!.Mode);
            // Огибающая длительных частей NL — отдельной группой из обеих пар (N2 — поля варианта).
            var pairsEnvelope = vm.EnvelopeItems.Single(e => e.Envelope!.Type == FemCsfeaBatchGroup.PairsOf("NL"));
            Assert.Equal(2, pairsEnvelope.Envelope!.Combinations.Count);
            Assert.Equal([summary.LongOf(0)!.RecordN, summary.Batch[1].RecordN],
                pairsEnvelope.Envelope.Combinations.Select(c => c.Batch!.RecordN));
            Assert.NotEqual("", vm.BatchRows[0].LongPart);
            Assert.NotEqual("", vm.BatchRows[0].LongDeflection);
            Assert.Equal("", vm.BatchRows[2].LongPart);
        });
    }
    /// <summary>
    /// Длительные части (спека «длительные пары», срез 2): q — постоянное, q2 — кратковременное с долей 0,5. N1 = q + q2 →
    /// пара q + 0,5·q2 своим прогоном после варианта; N2 = q — совпадает с полной, без прогона (поля — варианта).
    /// Флажок снят — записи пар удаляются при досчёте, включён снова — досчитывается только пара.
    /// </summary>
    [Fact]
    public async Task Batch_LongTermPairs_DerivedRunAndSameAsTotal_ResumeAddsOrRemoves()
    {
        await WithSchema(async (ctx, log, schema, analysis) =>
        {
            SetLongTermBatch(ctx.Db, schema, analysis, pairs: true, deflection: false);

            var result = await FemCsfeaRunner.RunBatchAsync(ctx, schema, analysis, null, null, null, CancellationToken.None);

            var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
            foreach (var line in summary.Describe()) output.WriteLine(line);
            Assert.Equal("ok", result.Status);
            Assert.Equal(2, summary.BatchLong!.Count);
            var derived = summary.LongOf(0)!;
            Assert.True(derived.IsDerived);
            Assert.True(derived.Done && derived.Completed);
            Assert.Equal("NL", derived.Type);
            Assert.Equal(3, derived.Stage);
            Assert.Equal(2, derived.RecordN);   // поля пишутся по порядку прогонов: N1, его пара, N2, q2
            Assert.Equal(16 * Q * 1.25, derived.Fz, 1e-6 * Math.Abs(Q * 24));
            var same = summary.LongOf(1)!;
            Assert.True(same.IsSameAsTotal);
            Assert.Null(same.Stage);
            Assert.Null(same.RecordN);
            Assert.Equal(summary.Batch![1].RecordN, summary.LongRecordN(same));
            Assert.Null(summary.LongOf(2));
            Assert.Equal([1, 2, 3, 4], ctx.Db.GetFemResultStepNumbers(result.Id));
            Assert.Contains(log.LogEntries, e => e.Message.Contains("[N] N1: длительная часть NL", StringComparison.Ordinal));
            Assert.Contains(summary.Describe(), l => l.StartsWith("Длительные части: своим прогоном 1, совпадают с полной 1, нулевые 0", StringComparison.Ordinal));

            // Прогиб длительной части — между q и q + q2 (центр плиты, наибольший |uz|).
            double uTotal = MaxUz(Fields(ctx.Db, result.Id, summary.Batch[0].RecordN!.Value));
            double uPermanent = MaxUz(Fields(ctx.Db, result.Id, summary.Batch[1].RecordN!.Value));
            double uLong = MaxUz(Fields(ctx.Db, result.Id, derived.RecordN!.Value));
            Assert.InRange(uLong, uPermanent * 1.01, uTotal * 0.99);

            // Флажок снят: пары удаляются, варианты не пересчитываются.
            SetLongTermBatch(ctx.Db, schema, analysis, pairs: false, deflection: false);
            var trimmed = await FemCsfeaRunner.RunBatchAsync(ctx, schema, analysis, result, null, null, CancellationToken.None);
            Assert.Null(FemCsfeaResultSummary.Parse(trimmed.DataJson)!.BatchLong);
            Assert.Equal([1, 3, 4], ctx.Db.GetFemResultStepNumbers(trimmed.Id));

            // Включён снова: досчитывается только пара.
            SetLongTermBatch(ctx.Db, schema, analysis, pairs: true, deflection: false);
            int before = log.LogEntries.Count;
            var again = await FemCsfeaRunner.RunBatchAsync(ctx, schema, analysis, trimmed, null, null, CancellationToken.None);
            var againSummary = FemCsfeaResultSummary.Parse(again.DataJson)!;
            Assert.Equal("ok", again.Status);
            Assert.DoesNotContain(log.LogEntries.Skip(before), e => e.Message.Contains("Вариант ", StringComparison.Ordinal));
            Assert.True(againSummary.LongOf(0)!.Done);
            Assert.Equal(5, againSummary.LongOf(0)!.RecordN);
            Assert.Equal([1, 3, 4, 5], ctx.Db.GetFemResultStepNumbers(again.Id));
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
        return Path.Combine(dir!.FullName, "OpenCS.App", "DataSource");
    }
}
