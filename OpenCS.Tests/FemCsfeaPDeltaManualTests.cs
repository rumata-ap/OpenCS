using System.Globalization;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручной прогон P-Δ источника инженерной нелинейности на реальной базе (спека 2026-10-21): OPENCS_PDELTA_DB — база с
/// секущей и линейной (ИН) постановками CSfea одной схемы (читается копия). Секущая пересчитывается с геометрической
/// нелинейностью (второй порядок), линейная — на её жёсткостях без флажка P-Δ и с ним; в вывод — контрольный узел по
/// загружениям, итог и самопроверка. OPENCS_PDELTA_KEEP=путь — сохранить копию с результатами. Без переменной — выход.
/// </summary>
public class FemCsfeaPDeltaManualTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RunSourcePDelta()
    {
        var source = Environment.GetEnvironmentVariable("OPENCS_PDELTA_DB");
        if (string.IsNullOrWhiteSpace(source)) return;
        var root = Path.Combine(Path.GetTempPath(), $"opencs-pdelta-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "pdelta.db");
        File.Copy(source, path);
        try
        {
            using (var db = new DatabaseService(path))
            {
                db.LoadAll();
                var gmsh = new GmshSettings { ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe", ArtifactsPath = Path.Combine(root, "gmsh") };
                var ctx = new FemCsfeaRunContext(db, gmsh, new OutputLog(output));
                var schema = db.FemSchemas.First(s => s.Analyses.Any(a => a.Kind == FemCsfeaRunner.AnalysisKind));
                var secant = schema.Analyses.First(a => a.Kind == FemCsfeaRunner.AnalysisKind);
                var linear = schema.Analyses.First(a => a.Kind == FemCsfeaRunner.LinearAnalysisKind);
                output.WriteLine($"Схема «{schema.Tag}», секущая «{secant.Tag}», линейная «{linear.Tag}».");

                if (secant.ResultId is int before && db.GetCalcResultById(before) is { } r0)
                    Describe("Секущий без геомнелина (как в базе)", FemCsfeaResultSummary.Parse(r0.DataJson)!);

                var sp = FemAnalysisParams.Parse(secant.ParamsJson);
                sp.Csfea!.GeomNonlinear = true;
                secant.ParamsJson = sp.ToJson();
                var src = await FemCsfeaRunner.RunAsync(ctx, schema, secant, null, CancellationToken.None);
                secant.ResultId = src.Id;
                secant.Status = src.Status;
                db.SaveFemAnalysis(secant);
                Describe($"Секущий с геомнелином (второй порядок): {src.Status}", FemCsfeaResultSummary.Parse(src.DataJson)!);

                var candidate = FemCsfeaStiffnessSources.List(db, schema.Id).First(c => c.Analysis.Id == secant.Id && c.IsFinalState);
                var fingerprint = FemCsfeaStiffnessSources.Fingerprint(db, candidate, out var error)
                    ?? throw new InvalidOperationException(error);
                foreach (bool pDelta in new[] { false, true })
                {
                    var lp = FemAnalysisParams.Parse(linear.ParamsJson);
                    lp.Csfea!.StiffnessSource = fingerprint;
                    lp.Csfea.SourcePDelta = pDelta;
                    linear.ParamsJson = lp.ToJson();
                    var lin = await FemCsfeaRunner.RunAsync(ctx, schema, linear, null, CancellationToken.None);
                    linear.ResultId = lin.Id;
                    linear.Status = lin.Status;
                    db.SaveFemAnalysis(linear);
                    Describe($"Линейный на его жёсткостях, P-Δ {(pDelta ? "включён" : "выключен")}: {lin.Status}",
                        FemCsfeaResultSummary.Parse(lin.DataJson)!);
                }
            }
            if (Environment.GetEnvironmentVariable("OPENCS_PDELTA_KEEP") is { Length: > 0 } keep)
            {
                SqliteConnection.ClearAllPools();
                File.Copy(path, keep, overwrite: true);
                output.WriteLine($"Копия с результатами: {keep}");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    sealed class OutputLog(ITestOutputHelper output) : ILogService
    {
        public System.Collections.ObjectModel.ObservableCollection<LogEntry> LogEntries { get; } = [];
        public void Info(string message) { }
        public void Warning(string message) => output.WriteLine("журнал (!): " + message);
        public void Error(string message) => output.WriteLine("журнал (ошибка): " + message);
    }

    void Describe(string title, FemCsfeaResultSummary s)
    {
        output.WriteLine("");
        output.WriteLine($"== {title}");
        foreach (var line in s.Errors.Concat(s.Report).Concat(s.Describe())) output.WriteLine("  " + line);
        var ends = s.Steps.Where(st => st.Converged && !st.Refinement).GroupBy(st => st.Stage).Select(g => g.Last());
        foreach (var st in ends)
        {
            string stage = st.Stage < s.Stages.Count ? s.Stages[st.Stage].Tag : st.Stage.ToString(CultureInfo.InvariantCulture);
            output.WriteLine(string.Create(CultureInfo.CurrentCulture,
                $"  «{stage}»: λ = {st.LoadFactor:0.###}, контрольный узел {s.ControlSchemaNodeTag}: {1000 * (st.Control ?? double.NaN):0.###} мм"));
        }
    }
}
