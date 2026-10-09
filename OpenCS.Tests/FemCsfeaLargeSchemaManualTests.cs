using System.Diagnostics;
using CScore;
using CScore.Fem;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручной прогон секущего CSfea на большой схеме (музей): копия базы OPENCS_CSFEA_LARGE_DB во временном каталоге; если
/// в ней нет постановки CSfea — добавляется «собственный вес» (загружение 1, один шаг, запись «только конечный»).
/// Проверка входа, расчёт, время и пик памяти — в вывод. OPENCS_CSFEA_LARGE_KEEP=путь — сохранить копию с результатом
/// (для проверки окна результата в UI). Без переменной тест сразу выходит.
/// </summary>
public sealed class FemCsfeaLargeSchemaManualTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RunLargeSchema()
    {
        var source = Environment.GetEnvironmentVariable("OPENCS_CSFEA_LARGE_DB");
        if (string.IsNullOrWhiteSpace(source)) return;
        var root = Path.Combine(Path.GetTempPath(), $"opencs-csfea-large-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "large.db");
        File.Copy(source, path);
        var clock = Stopwatch.StartNew();
        void Log(string s) => output.WriteLine(
            $"[{clock.Elapsed.TotalSeconds,6:0.0} с, {Process.GetCurrentProcess().WorkingSet64 / 1048576.0,6:0} МБ] {s}");
        try
        {
            using (var db = new DatabaseService(path))
            {
                db.LoadAll();
                Log("база загружена");
                var gmsh = new GmshSettings { ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe", ArtifactsPath = Path.Combine(root, "gmsh") };
                var ctx = new FemCsfeaRunContext(db, gmsh, new OutputLog(output));
                foreach (var schema in db.FemSchemas.ToList())
                {
                    if (!schema.Analyses.Any(a => a.Kind == FemCsfeaRunner.AnalysisKind))
                    {
                        AddSelfWeight(db, schema);
                        db.LoadAll();
                    }
                    var fresh = db.FemSchemas.Single(x => x.Id == schema.Id);
                    foreach (var analysis in fresh.Analyses.Where(a => a.Kind == FemCsfeaRunner.AnalysisKind).ToList())
                    {
                        var prepared = await FemCsfeaRunner.PrepareAsync(ctx, fresh, analysis, buildMesh: false, CancellationToken.None);
                        Log($"«{analysis.Tag}» — проверка входа: ошибки {prepared.HasErrors}");
                        foreach (var line in prepared.Describe().Concat(prepared.Report).Take(60)) output.WriteLine("  " + line);
                        if (prepared.HasErrors) continue;
                        // OPENCS_CSFEA_LARGE_BEAM — номер стержня: откуда его GJ.
                        if (Environment.GetEnvironmentVariable("OPENCS_CSFEA_LARGE_BEAM") is { Length: > 0 } beamTag)
                        {
                            var el = prepared.Input!.MeshElements.First(x => x.ElemTag == beamTag);
                            var member = el.SourceMemberTag is { } mt ? prepared.Input.Members.FirstOrDefault(m => m.ElemTag == mt) : null;
                            int? csId = el.CrossSectionId ?? member?.CrossSectionId;
                            output.WriteLine($"  стержень {beamTag}: сечение {csId} (КЭ {el.CrossSectionId}, КонЭ {member?.CrossSectionId}), " +
                                             $"GJ КЭ «{el.GjStrategy}», КонЭ «{member?.GjStrategy}»");
                            var cs = db.CrossSections.FirstOrDefault(x => x.Id == csId);
                            foreach (var area in cs?.Areas ?? [])
                            {
                                var mat = area.Material ?? db.Materials.FirstOrDefault(m => m.Id == area.MaterialId);
                                output.WriteLine($"    область «{area.Tag}» {area.Category}: материал {area.MaterialId} «{mat?.Tag}» E = {mat?.E}, контуров {area.Contours.Count}");
                            }
                            var src = new ProjectElementStiffnessSource(prepared.Input.Members, db.CrossSections, [], db.Materials);
                            var bar = src.Bar(el);
                            output.WriteLine($"    Bar: {(bar == null ? "null" : $"E {bar.E:E3}, A {bar.A:E3}, Iy {bar.Iy:E3}, Iz {bar.Iz:E3}, J {bar.J:E3}")}");
                        }
                        if (Environment.GetEnvironmentVariable("OPENCS_CSFEA_LARGE_DIRECT") == "1")
                        {
                            // Прямой прогон ядра с полным журналом итераций (время каждой строки), без дробления шага.
                            var csfea = FemAnalysisParams.Parse(analysis.ParamsJson).Csfea ?? new FemCsfeaParams();
                            csfea.MaxBisections = 0;
                            csfea.MaxIterations = int.TryParse(Environment.GetEnvironmentVariable("OPENCS_CSFEA_LARGE_ITER"), out int it) ? it : 20;
                            var options = FemCsfeaSetup.SecantOptions(csfea, -1, Log, prepared.Adapted!.ShellForceAngles);
                            var run = CSfea.CScoreBridge.Structural.RcSecantAnalysis.Run(prepared.Adapted.Model, options, null, CancellationToken.None);
                            Log($"прямой прогон: {run.Result.Message}; анализов Холецкого {run.Build.Mesh.CholeskyAnalyses}");
                            foreach (var line in run.Build.Report.Take(30)) output.WriteLine("  сетка: " + line);
                            var zeroGj = Enumerable.Range(0, run.Build.Mesh.Beams.Count)
                                .Where(e => run.Build.Mesh.Beams[e].Section.TorsionalStiffness() == 0).ToList();
                            output.WriteLine($"  стержней с GJ = 0: {zeroGj.Count} из {run.Build.Mesh.Beams.Count}; сечения: " +
                                string.Join(", ", zeroGj.Select(e => run.Build.Mesh.Beams[e].Section.GetType().Name).Distinct()) +
                                "; номера: " + string.Join(", ", zeroGj.Take(20).Select(e => run.Build.BeamIds[e])));
                            // OPENCS_CSFEA_LARGE_NODE — индекс узла сетки: что в нём сходится.
                            if (int.TryParse(Environment.GetEnvironmentVariable("OPENCS_CSFEA_LARGE_NODE"), out int ni))
                            {
                                var mesh = run.Build.Mesh;
                                output.WriteLine($"  узел сетки {ni}: узел модели {run.Build.NodeIds[ni]}");
                                for (int e = 0; e < mesh.Shells.Count; e++)
                                    if (mesh.Shells[e].Nodes.Contains(ni))
                                        output.WriteLine($"    оболочка {run.Build.ShellIds[e]}: узлы {string.Join(",", mesh.Shells[e].Nodes.Select(n => run.Build.NodeIds[n]))}");
                                for (int e = 0; e < mesh.Beams.Count; e++)
                                    if (mesh.Beams[e].I == ni || mesh.Beams[e].J == ni)
                                        output.WriteLine($"    стержень {run.Build.BeamIds[e]}: {run.Build.NodeIds[mesh.Beams[e].I]}–{run.Build.NodeIds[mesh.Beams[e].J]}, " +
                                                         $"шарниры I {mesh.Beams[e].ReleaseI:X}, J {mesh.Beams[e].ReleaseJ:X}, сечение {mesh.Beams[e].Section.GetType().Name}, GJ = {mesh.Beams[e].Section.TorsionalStiffness():E3}");
                                var sup = prepared.Adapted.Model.Supports.Where(x => x.NodeId == run.Build.NodeIds[ni]).ToList();
                                output.WriteLine($"    закреплений узла: {sup.Count}");
                            }
                            continue;
                        }
                        var progress = new Progress<CSfea.Core.SecantProgress>();
                        var result = await FemCsfeaRunner.RunAsync(ctx, fresh, analysis, progress, CancellationToken.None);
                        Log($"«{analysis.Tag}» — расчёт: {result.Status}");
                        // Как AppViewModel.RunFemAnalysis: без ссылки на результат окно анализа его не находит.
                        analysis.ResultId = result.Id;
                        analysis.Status = result.Status;
                        db.SaveFemAnalysis(analysis);
                        var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
                        foreach (var line in summary.Describe()) output.WriteLine("  " + line);
                        foreach (var st in summary.Steps)
                            output.WriteLine($"    шаг {st.N}: стадия {st.Stage}, λ = {st.LoadFactor:0.###}, сошёлся {st.Converged}, " +
                                             $"итераций {st.Iterations}, поля {st.Fields}");
                    }
                }
            }
            output.WriteLine($"Пик рабочего набора {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0:0} МБ");
            if (Environment.GetEnvironmentVariable("OPENCS_CSFEA_LARGE_KEEP") is { Length: > 0 } keep)
            {
                SqliteConnection.ClearAllPools();
                File.Copy(path, keep, overwrite: true);
                output.WriteLine($"Копия с результатом: {keep}");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    /// <summary>Постановка «собственный вес»: загружение с номером SCAD 1, один шаг, до 100 итераций, запись только конечного шага.</summary>
    static void AddSelfWeight(DatabaseService db, FemSchema schema)
    {
        var cases = db.GetFemLoadCases(schema.Id);
        var dead = cases.FirstOrDefault(c => c.SourceLoadNum == 1) ?? cases.First();
        string expr = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [dead.Id] }.ToJson();
        var pars = new FemAnalysisParams
        {
            CalcType = CalcType.N,
            Stages = [new FemAnalysisStage { Tag = dead.Tag, LoadExpressionJson = expr, LoadFactorStep = 1, MaxLoadFactor = 1 }],
            // OPENCS_CSFEA_LARGE_TOLK — допуск по жёсткости (по умолчанию 1e-3; на музее Пикар выходит на полку ~7e-3).
            Csfea = new FemCsfeaParams
            {
                ResultRecording = FemCsfeaRecording.Final, MaxIterations = 100,
                TolStiffness = double.TryParse(Environment.GetEnvironmentVariable("OPENCS_CSFEA_LARGE_TOLK"),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double tol) ? tol : 1e-3,
            },
        };
        db.SaveFemAnalysis(new FemAnalysis
        {
            SchemaId = schema.Id, Tag = $"CSfea: {dead.Tag}", Kind = FemCsfeaRunner.AnalysisKind,
            LoadExpressionJson = expr, ParamsJson = pars.ToJson(),
        });
    }

    sealed class OutputLog(ITestOutputHelper output) : ILogService
    {
        public System.Collections.ObjectModel.ObservableCollection<LogEntry> LogEntries { get; } = [];
        public void Info(string message) => output.WriteLine("журнал: " + message);
        public void Warning(string message) => output.WriteLine("журнал (!): " + message);
        public void Error(string message) => output.WriteLine("журнал (ошибка): " + message);
    }
}
