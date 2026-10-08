using System.Diagnostics;
using System.Globalization;
using CSfea.CScoreBridge.Structural;
using OpenCS.OpenSees.CScore;
using OpenCS.Services.Scad;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручная сверка линейного расчёта CSfea со SCAD (срез 4 плана 4б): схема .SPR с результатами в рабочем каталоге →
/// <see cref="ScadRcModelAdapter"/> (упругие пластины по «GE E ν h», стержни S0/S3/S6, оси SCAD, шарниры, пружины,
/// жёсткие тела, C1) → одна факторизация на все загружения → перемещения и повороты против SCAD по каждой компоненте.
/// OPENCS_SCAD_LINEAR_SPR — модель SCAD (без неё тест сразу выходит); OPENCS_SCAD_LINEAR_LOADS — номера загружений
/// через запятую (по умолчанию все); OPENCS_SCAD_DIR, OPENCS_SCAD_WORK — как в <see cref="ScadApiReaderManualTests"/>.
/// </summary>
public class ScadLinearCsfeaManualTests(ITestOutputHelper output)
{
    [Fact]
    public void LinearAgainstScadDisplacements()
    {
        string? spr = Environment.GetEnvironmentVariable("OPENCS_SCAD_LINEAR_SPR");
        if (string.IsNullOrWhiteSpace(spr)) return;
        string dir = Environment.GetEnvironmentVariable("OPENCS_SCAD_DIR") ?? ScadInstallLocator.FindDllDirectory()!;
        string? work = Environment.GetEnvironmentVariable("OPENCS_SCAD_WORK") ?? ScadInstallLocator.FindWorkDirectory();
        var only = Environment.GetEnvironmentVariable("OPENCS_SCAD_LINEAR_LOADS")?
            .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToHashSet();
        var sw = Stopwatch.StartNew();
        void Log(string s) => output.WriteLine($"[{sw.Elapsed.TotalSeconds,6:0.0} с] {s}");

        var native = ScadApiNative.Load(dir);
        using var s = new ScadApiSession(native);
        s.Open(spr);
        var data = ScadApiReader.Read(s, new ScadReadOptions(), null, CancellationToken.None).Data;
        var am = data.AnalysisModel!;
        Log($"Схема: узлов {data.Nodes.Count}, КЭ {data.Elements.Count}, закреплений {am.Bounds.Count}, пружин {am.Springs.Count}, " +
            $"тел {am.RigidBodies.Count}, шарниров {am.Joints.Count}, осей стержней {data.RodAxes.Count}, загружений {am.LoadCases.Count}");
        foreach (var g in data.RodAxes.Values.GroupBy(a => (a.Type, string.Join(" ", a.Values.Select(v => v.ToString("G4", CultureInfo.InvariantCulture))))))
            output.WriteLine($"  оси стержней: тип {g.Key.Type} [{g.Key.Item2}] — {g.Count()} КЭ");

        var stiff = data.Stiffnesses.ToDictionary(x => x.Id);
        var elems = data.Elements.ToDictionary(e => e.Id);
        (double E, double Nu) Ge(int stiffId)
        {
            var p = stiff.GetValueOrDefault(stiffId)?.Text?.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries) ?? [];
            return p.Length > 2 && p[0].StartsWith("GE", StringComparison.OrdinalIgnoreCase)
                ? (double.Parse(p[1], CultureInfo.InvariantCulture) * am.ForceUnitN / (am.LengthUnitM * am.LengthUnitM),
                    double.Parse(p[2], CultureInfo.InvariantCulture))
                : (3e10, 0.2);
        }
        // Профили STZ — по сортаментам установленного SCAD (каталог SCADAPIX.dll), как ScadSteelProfileLoader.
        var steel = CScore.Import.ScadSteelProfiles.ResolveAll(
            data.Stiffnesses.Where(x => x.Text != null).Select(x => (x.Id, x.Text!)),
            b => File.Exists(Path.Combine(dir, b + ".PRF")) ? CScore.Import.ScadPrfReader.Read(Path.Combine(dir, b + ".PRF")) : null);
        foreach (var p in steel)
            output.WriteLine($"  сортамент: жёсткость {p.Num} {p.Source} → {(p.Shape is { } sh ? $"{sh.Kind} {sh.Name} H={sh.H * 1e3:0.#} мм" : p.Reason)}");
        var cases = am.LoadCases.Where(l => only == null || only.Contains(l.Num)).ToList();
        var adapted = ScadRcModelAdapter.Adapt(new ScadRcModelInput
        {
            Data = data,
            PlateSection = id =>
            {
                int sid = elems[id].StiffnessId;
                double h = stiff.GetValueOrDefault(sid)?.ThicknessM ?? 0.2;
                return new ScadShellElementSection(new CScore.PlateSection { Tag = $"ж{sid}", H = h }, $"elastic|{sid}");
            },
            ShellSection = sec =>
            {
                var (e, nu) = Ge(int.Parse(sec.Section.Tag![1..], CultureInfo.InvariantCulture));
                return ScadRcModelAdapter.ElasticShells(e, nu)(sec);
            },
            SteelShapes = steel.Where(p => p.Shape != null).ToDictionary(p => p.Num, p => p.Shape!),
            Stages = cases.Select(l => new ScadShellStage($"L{l.Num}", [(l.Num, 1.0)], 1.0)).ToList(),
        });
        var m = adapted.Model;
        Log($"Модель: узлов {m.Nodes.Count}, пластин {m.Shells.Count}, стержней {m.Beams.Count} (шарнирных концов " +
            $"{m.Beams.Sum(b => (b.ReleaseI != 0 ? 1 : 0) + (b.ReleaseJ != 0 ? 1 : 0))}), пружин {m.Springs.Count}, тел {m.RigidBodies.Count}, " +
            $"закреплений {m.Supports.Count}");
        var build = RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());
        foreach (var line in adapted.Report.Concat(build.Report).Distinct().Take(60)) output.WriteLine("  " + line);

        var loads = cases.Select((_, k) => build.Combination(m.Stages[k].Loads)).ToList();
        Log($"Сетка: NDof {build.Mesh.NDof}; решение {loads.Count} загружений…");
        var us = build.Mesh.SolveLinear(loads, build.Bc);
        Log("Решено.");

        var disp = ScadApiDisplacementReader.Read(s, work, m.Nodes.Select(n => n.Id).ToList(), CancellationToken.None);
        // Повороты узлов, где у примыкающего конца стержня освобождён поворот, не определены однозначно — не сравниваем.
        var hinged = m.Beams.SelectMany(b => new[] { (b.NodeI, b.ReleaseI), (b.NodeJ, b.ReleaseJ) })
            .Where(x => (x.Item2 & 0x38) != 0).Select(x => x.Item1).ToHashSet();
        output.WriteLine($"Узлов с шарнирами по поворотам (φ не сравниваются): {hinged.Count}");
        int loadIndex = 0;
        foreach (var lc in am.LoadCases)
        {
            loadIndex++;
            int k = cases.IndexOf(lc);
            if (k < 0) continue;
            var row = disp.Rows.FirstOrDefault(r => r.Load == loadIndex && r.Step == 0);
            if (row == null) { output.WriteLine($"L{lc.Num}: нет перемещений SCAD"); continue; }
            var scad = new Dictionary<int, double[]>(row.Nodes.Length);
            for (int i = 0; i < row.Nodes.Length; i++) scad[row.Nodes[i]] = row.Values.AsSpan(6 * i, 6).ToArray();
            var u = us[k];
            // ΣF — узловые силы и концевые силы шарнирных стержней.
            var rcCase = m.LoadCases.Single(l => l.Id == lc.Num);
            double Sum(int c) => rcCase.Nodal.Sum(p => p.Force[c]) + rcCase.BeamEnds.Sum(p => p.Forces[c] + p.Forces[c + 6]);
            output.WriteLine($"L{lc.Num} «{lc.Name}»: ΣF = ({string.Join("; ", Enumerable.Range(0, 3).Select(c => (Sum(c) / 1e3).ToString("0.###", CultureInfo.InvariantCulture)))}) кН");
            for (int c = 0; c < 6; c++)
            {
                var pairs = m.Nodes.Where(n => scad.ContainsKey(n.Id) && (c < 3 || !hinged.Contains(n.Id)))
                    .Select(n => (Id: n.Id, S: scad[n.Id][c], C: u[build.Dof(n.Id, c)])).ToList();
                double ss = pairs.Sum(p => p.S * p.S), cc = pairs.Sum(p => p.C * p.C);
                if (ss == 0 && cc == 0) continue;
                double a = ss > 0 ? pairs.Sum(p => p.S * p.C) / ss : double.NaN;
                double res = cc > 0 ? Math.Sqrt(pairs.Sum(p => Math.Pow(p.C - a * p.S, 2)) / cc) : double.NaN;
                var peak = pairs.MaxBy(p => Math.Abs(p.S));
                string unit = c < 3 ? "мм" : "мрад";
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {(c < 3 ? "u" : "φ")}{"xyzxyz"[c]}: max|SCAD| {Math.Abs(peak.S) * 1e3:0.####} {unit} (узел {peak.Id}, CSfea {peak.C * 1e3:0.####}), " +
                    $"max|CSfea| {pairs.Max(p => Math.Abs(p.C)) * 1e3:0.####}; CSfea ≈ {a:0.####}·SCAD, остаток {res:P2}"));
                if (Environment.GetEnvironmentVariable("OPENCS_SCAD_LINEAR_DIAG") == "1" && res > 0.1)
                    foreach (var w in pairs.OrderByDescending(p => Math.Abs(p.C - p.S)).Take(3).Append(peak))
                        output.WriteLine($"    узел {w.Id}: SCAD {w.S * 1e3:0.####}, CSfea {w.C * 1e3:0.####}; стержни: " + string.Join("; ",
                            m.Beams.Where(b => b.NodeI == w.Id || b.NodeJ == w.Id).Select(b =>
                                $"{b.Id} ж{elems[b.Id].StiffnessId} {(b.NodeI == w.Id ? "I" : "J")} осв {(b.NodeI == w.Id ? b.ReleaseI : b.ReleaseJ)} " +
                                $"y=({string.Join(",", b.RefVec!.Select(v => v.ToString("0.##", CultureInfo.InvariantCulture)))})")));
            }
        }
    }
}
