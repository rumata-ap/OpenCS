using System.Globalization;
using CScore;
using CScore.Fem;
using CSfea.CScoreBridge.Structural;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручная сверка секущего расчёта CSfea с расчётом OpenSees на стержневой схеме базы OpenCS (неразрезная балка,
/// полурама): стадия 1 — первое загружение целиком, стадия 2 — второе с множителем OPENCS_BEAM_SECANT_K за
/// OPENCS_BEAM_SECANT_STEPS шагов. База — OPENCS_BEAM_SECANT_RUN (считается копия), выгрузка по принятым шагам —
/// каталог OPENCS_BEAM_SECANT_OUT: steps.csv, disp.csv, beamforces.csv (местные силы концов КЭ, как localForce
/// OpenSees), журнал итераций log.txt. OPENCS_BEAM_SECANT_ROT — поворот сечений, град. OPENCS_BEAM_SECANT_ITER / _BIS — итераций на шаг и дроблений; _TENS, _PSI,
/// _SHEAR = 1 — растяжение бетона, ψs и сдвиг (по умолчанию выключены, как в расчёте OpenSees). Без переменных тест
/// выходит.
/// </summary>
public sealed class BeamSecantVsOpenSeesManualTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Run()
    {
        var source = Environment.GetEnvironmentVariable("OPENCS_BEAM_SECANT_RUN");
        if (string.IsNullOrWhiteSpace(source)) return;
        var inv = CultureInfo.InvariantCulture;
        double Num(string name, double fallback) =>
            double.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, inv, out double x) ? x : fallback;
        bool Flag(string name) => Environment.GetEnvironmentVariable(name) == "1";
        double k = Num("OPENCS_BEAM_SECANT_K", 45);
        int steps = (int)Num("OPENCS_BEAM_SECANT_STEPS", 90);
        string outDir = Environment.GetEnvironmentVariable("OPENCS_BEAM_SECANT_OUT") ?? Path.GetTempPath();
        Directory.CreateDirectory(outDir);

        var root = Path.Combine(Path.GetTempPath(), $"opencs-beam-secant-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "schema.db");
        File.Copy(source, path);
        try
        {
            // OPENCS_BEAM_SECANT_ROT — поворот сечений всех стержней копии, град (ориентация как в расчёте OpenSees).
            if (Environment.GetEnvironmentVariable("OPENCS_BEAM_SECANT_ROT") is { Length: > 0 })
            {
                using var raw = new SqliteConnection($"Data Source={path}");
                raw.Open();
                using var cmd = raw.CreateCommand();
                cmd.CommandText = "update fem_members set rotation_deg = $r";
                cmd.Parameters.AddWithValue("$r", Num("OPENCS_BEAM_SECANT_ROT", 0));
                output.WriteLine($"поворот сечений: {Num("OPENCS_BEAM_SECANT_ROT", 0)}°, стержней {cmd.ExecuteNonQuery()}");
                raw.Close();
                SqliteConnection.ClearAllPools();
            }
            using var db = new DatabaseService(path);
            db.LoadAll();
            var schema = db.FemSchemas.First();
            var cases = db.GetFemLoadCases(schema.Id).OrderBy(c => c.Id).ToList();
            output.WriteLine("загружения: " + string.Join(", ", cases.Select(c => $"{c.Id} «{c.Tag}»")));
            var gmsh = new GmshSettings { ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe", ArtifactsPath = Path.Combine(root, "gmsh") };
            var ctx = new FemCsfeaRunContext(db, gmsh, new OutputLog(output));

            string Expr(FemLoadCase c, double coefficient) => new FemLoadExpression
            {
                Mode = FemLoadExpressionMode.Sum, Terms = [new FemLoadTerm { LoadCaseId = c.Id, Coefficient = coefficient }],
            }.ToJson();
            List<FemAnalysisStage> stages =
            [
                new() { Tag = cases[0].Tag, LoadExpressionJson = Expr(cases[0], 1), LoadFactorStep = 1, MaxLoadFactor = 1 },
                new() { Tag = cases[1].Tag, LoadExpressionJson = Expr(cases[1], k), LoadFactorStep = 1.0 / steps, MaxLoadFactor = 1 },
            ];
            var csfea = new FemCsfeaParams
            {
                TensionConcrete = Flag("OPENCS_BEAM_SECANT_TENS"), Psi = Flag("OPENCS_BEAM_SECANT_PSI"),
                BeamShear = Flag("OPENCS_BEAM_SECANT_SHEAR"), MaxIterations = (int)Num("OPENCS_BEAM_SECANT_ITER", 50),
                MaxBisections = (int)Num("OPENCS_BEAM_SECANT_BIS", 4),
            };
            var analysis = new FemAnalysis
            {
                SchemaId = schema.Id, Tag = "секущий", Kind = FemCsfeaRunner.AnalysisKind, LoadExpressionJson = stages[0].LoadExpressionJson,
                ParamsJson = new FemAnalysisParams { CalcType = CalcType.C, Stages = stages, Csfea = csfea }.ToJson(),
            };
            var prepared = await FemCsfeaRunner.PrepareAsync(ctx, schema, analysis, buildMesh: false, CancellationToken.None);
            output.WriteLine($"проверка входа — ошибки {prepared.HasErrors}");
            foreach (var line in prepared.Describe().Concat(prepared.Report).Take(40)) output.WriteLine("  " + line);
            if (prepared.HasErrors) return;

            using var log = new StreamWriter(Path.Combine(outDir, "log.txt")) { AutoFlush = true };
            var stepRows = new List<string> { "stage,factor,lambda,iterations,residual,cracked,yielded,failed" };
            var dispRows = new List<string> { "stage,factor,node,ux,uy,uz,rx,ry,rz" };
            var forceRows = new List<string> { "stage,factor,beam,ni,qyi,qzi,mxi,myi,mzi,nj,qyj,qzj,mxj,myj,mzj" };
            const int nc = RcSecantStepFields.NodeComponents, bc = RcSecantStepFields.BeamForceComponents;
            void OnStep(CSfea.Core.SecantStepResult s, Func<RcSecantStepFields> fields)
            {
                var f = fields();
                string head = string.Create(inv, $"{s.Stage},{s.LoadFactor:R}");
                stepRows.Add(head + string.Create(inv, $",{(s.Stage == 0 ? 0 : k * s.LoadFactor):R},{s.Iterations},{s.TrueResidual:e3},") +
                             $"{s.Beams.Count(x => x.Cracked)},{s.Beams.Count(x => x.Yielded)},{s.Beams.Count(x => x.Failed)}");
                for (int i = 0; i < f.NodeIds.Length; i++)
                    dispRows.Add($"{head},{f.NodeIds[i]}," + string.Join(',', f.Displacements.Skip(i * nc).Take(nc).Select(x => x.ToString("R", inv))));
                for (int e = 0; e < f.BeamIds.Length; e++)
                    forceRows.Add($"{head},{f.BeamIds[e]}," + string.Join(',', f.BeamForces.Skip(e * bc).Take(bc).Select(x => x.ToString("R", inv))));
            }
            var options = FemCsfeaSetup.SecantOptions(csfea, -1, s => log.WriteLine(s), prepared.Adapted!.ShellForceAngles, OnStep);
            var run = RcSecantAnalysis.Run(prepared.Adapted.Model, options, null, CancellationToken.None);
            File.WriteAllLines(Path.Combine(outDir, "steps.csv"), stepRows);
            File.WriteAllLines(Path.Combine(outDir, "disp.csv"), dispRows);
            File.WriteAllLines(Path.Combine(outDir, "beamforces.csv"), forceRows);

            var r = run.Result;
            var last = r.Steps.LastOrDefault(s => s.Converged);
            output.WriteLine(r.Completed ? "пройдено" : "ОСТАНОВЛЕНО: " + r.Message);
            if (last != null)
                output.WriteLine($"последний принятый шаг: стадия {last.Stage}, доля {last.LoadFactor:0.####}, множитель " +
                                 $"{(last.Stage == 0 ? 0 : k * last.LoadFactor):0.###}; шагов {r.Steps.Count(s => s.Converged)}, " +
                                 $"итераций {r.Steps.Sum(s => s.Iterations)}, невязка {last.TrueResidual:e2}");
            output.WriteLine($"выгрузка: {outDir}");
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
        public void Info(string message) => output.WriteLine("журнал: " + message);
        public void Warning(string message) => output.WriteLine("журнал (!): " + message);
        public void Error(string message) => output.WriteLine("журнал (ошибка): " + message);
    }
}
