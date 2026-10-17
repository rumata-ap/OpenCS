using System.Collections.ObjectModel;
using CScore;
using CScore.Fem;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit.Abstractions;

namespace OpenCS.Gmsh.Tests;

/// <summary>
/// Ручной прогон пакетного расчёта CSfea на реальной базе: OPENCS_CSFEA_BATCH_DB — путь к базе (копируется во временный
/// каталог; без переменной тест сразу выходит), OPENCS_CSFEA_BATCH_N — число комбинаций C (по умолчанию 2). Параметры
/// CSfea — первой секущей постановки схемы (нет — по умолчанию, вид расчёта N). Печатает время и размер записи каждого
/// варианта рядом с оценкой диалога (<see cref="FemCsfeaBatchEstimate"/>).
/// </summary>
public sealed class FemCsfeaBatchManualTests(ITestOutputHelper output)
{
    [Fact]
    public async Task BatchOnRealSchema()
    {
        var source = Environment.GetEnvironmentVariable("OPENCS_CSFEA_BATCH_DB");
        if (string.IsNullOrWhiteSpace(source)) return;
        int count = int.TryParse(Environment.GetEnvironmentVariable("OPENCS_CSFEA_BATCH_N"), out int n) && n > 0 ? n : 2;
        var root = Path.Combine(Path.GetTempPath(), $"opencs-csfea-batch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "batch.db");
        File.Copy(source, path);
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();
            var schema = db.FemSchemas.First(s => s.LoadDefinitions.Any(d => d.CombinationType == "C"));
            var template = schema.Analyses.FirstOrDefault(a => a.Kind == FemCsfeaRunner.AnalysisKind);
            var pars = template != null ? FemAnalysisParams.Parse(template.ParamsJson) : new FemAnalysisParams { CalcType = CalcType.N, Csfea = new() };
            pars.Stages = [];
            pars.Batch = new FemCsfeaBatch
            {
                Items = schema.LoadDefinitions.Where(d => d.CombinationType == "C").Take(count)
                    .Select(d => new FemCsfeaBatchItem { Tag = d.Tag, LoadExpressionJson = d.ExpressionJson, Type = "C" }).ToList(),
            };
            var analysis = new FemAnalysis
            {
                SchemaId = schema.Id, Tag = "пакет-замер", Kind = FemCsfeaRunner.AnalysisKind, ParamsJson = pars.ToJson(),
                LoadExpressionJson = pars.Batch.Items[0].LoadExpressionJson,
            };
            db.SaveFemAnalysis(analysis);
            output.WriteLine($"«{schema.Tag}»: шаблон «{template?.Tag ?? "—"}», вариантов {pars.Batch.Items.Count}, вид {pars.CalcType}");

            var log = new ListLog(output);
            var gmsh = new GmshSettings { ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe", ArtifactsPath = Path.Combine(root, "gmsh") };
            var result = await FemCsfeaRunner.RunBatchAsync(new FemCsfeaRunContext(db, gmsh, log), schema, analysis, null, null, null,
                CancellationToken.None);

            var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
            foreach (var line in summary.Report.Take(20).Concat(summary.Describe())) output.WriteLine(line);
            double estimate = FemCsfeaBatchEstimate.FieldBytes(summary.Nodes, summary.Shells, summary.Beams);
            foreach (var b in summary.Batch!)
            {
                int size = b.RecordN is int r ? db.GetFemResultStep(result.Id, r)?.Length ?? 0 : 0;
                output.WriteLine($"  {b.Label}: λ = {b.LoadFactor:0.###}, шагов {b.Steps}, итераций {b.Iterations}, {b.ElapsedSeconds:0.#} с, " +
                                 $"запись {size / 1e6:0.00} МБ (оценка {estimate / 1e6:0.00} МБ)");
            }
            Assert.NotEqual("error", result.Status);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    sealed class ListLog(ITestOutputHelper output) : ILogService
    {
        public ObservableCollection<LogEntry> LogEntries { get; } = [];
        public void Info(string message) => output.WriteLine(message);
        public void Warning(string message) => output.WriteLine("! " + message);
        public void Error(string message) => output.WriteLine("ОШИБКА " + message);
    }
}
