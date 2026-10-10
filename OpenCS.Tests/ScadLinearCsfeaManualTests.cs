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
/// через запятую (по умолчанию все); OPENCS_SCAD_LINEAR_DIAG=1 — состав нагрузок, ΣF без закреплённых направлений и
/// данные узлов с наибольшим расхождением; OPENCS_SCAD_LINEAR_OUT — каталог выгрузки перемещений по узлам (CSV);
/// OPENCS_SCAD_LINEAR_SHEAR — множитель трансверсальных модулей сдвига пластин (проверка вклада поперечного сдвига);
/// OPENCS_SCAD_DIR, OPENCS_SCAD_WORK — как в <see cref="ScadApiReaderManualTests"/>.
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
        double shearFactor = double.TryParse(Environment.GetEnvironmentVariable("OPENCS_SCAD_LINEAR_SHEAR"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out double sf) ? sf : 1;
        if (Environment.GetEnvironmentVariable("OPENCS_SCAD_LINEAR_DIAG") == "1")
            foreach (var g in data.Elements.Where(e => e.NodeIds.Length is 3 or 4).GroupBy(e => e.StiffnessId).OrderBy(g => g.Key))
                output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  жёсткость пластин {g.Key}: «{stiff.GetValueOrDefault(g.Key)?.Text}», " +
                    $"h = {stiff.GetValueOrDefault(g.Key)?.ThicknessM:0.###} м, E = {Ge(g.Key).E / 1e6:0} МПа, ν = {Ge(g.Key).Nu}, КЭ {g.Count()}"));
        if (Environment.GetEnvironmentVariable("OPENCS_SCAD_LINEAR_DIAG") == "1")
            foreach (var lc in cases)
            {
                foreach (var g in lc.NodeLoads.GroupBy(r => (r.Qw, r.Qn)))
                    output.WriteLine($"  L{lc.Num} узловые Qw {g.Key.Qw} Qn {g.Key.Qn}: записей {g.Count()}, узлов {g.Sum(r => r.Ids.Length)}");
                foreach (var g in lc.ElementLoads.SelectMany(r => r.Ids.Select(id => (r, n: elems.GetValueOrDefault(id)?.NodeIds.Length ?? 0)))
                    .GroupBy(x => (x.r.Qw, x.r.Qn, x.r.Data.Length, x.n)).OrderBy(g => g.Key))
                {
                    var first = g.First().r;
                    output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  L{lc.Num} на КЭ Qw {g.Key.Qw} Qn {g.Key.Qn}, чисел {g.Key.Length}, узлов в КЭ {g.Key.n}: КЭ {g.Count()}; " +
                        $"пример КЭ {first.Ids[0]} [{string.Join(" ", first.Data.Select(v => v.ToString("0.####", CultureInfo.InvariantCulture)))}]"));
                }
                output.WriteLine($"  L{lc.Num} по области: {lc.AreaLoads.Length}");
            }
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
                if (shearFactor == 1) return ScadRcModelAdapter.ElasticShells(e, nu)(sec);
                // Проверка вклада поперечного сдвига пластин: трансверсальные модули × коэффициент (большой — тонкая плита).
                double g = e / (2 * (1 + nu));
                return new RcShellSection($"elastic|{sec.Section.H}")
                {
                    Elastic = new CSfea.Core.Laminate([new CSfea.Core.Ply(
                        new CSfea.Core.OrthotropicMaterial(e, e, nu, g, g * shearFactor, g * shearFactor), 0.0, sec.Section.H)]),
                };
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
            if (Environment.GetEnvironmentVariable("OPENCS_SCAD_LINEAR_OUT") is { Length: > 0 } outDir)
            {
                Directory.CreateDirectory(outDir);
                var sb = new System.Text.StringBuilder("node,x,y,z,scad_ux,scad_uy,scad_uz,scad_rx,scad_ry,scad_rz,ux,uy,uz,rx,ry,rz\n");
                foreach (var n in m.Nodes.Where(n => scad.ContainsKey(n.Id)))
                    sb.Append(string.Create(CultureInfo.InvariantCulture, $"{n.Id},{n.X:R},{n.Y:R},{n.Z:R},")).Append(string.Join(",",
                        scad[n.Id].Concat(Enumerable.Range(0, 6).Select(c => u[build.Dof(n.Id, c)])).Select(v => v.ToString("R", CultureInfo.InvariantCulture)))).Append('\n');
                File.WriteAllText(Path.Combine(outDir, $"scad-linear-L{lc.Num}.csv"), sb.ToString());
            }
            // ΣF — узловые силы и концевые силы шарнирных стержней.
            var rcCase = m.LoadCases.Single(l => l.Id == lc.Num);
            double Sum(int c) => rcCase.Nodal.Sum(p => p.Force[c]) + rcCase.BeamEnds.Sum(p => p.Forces[c] + p.Forces[c + 6]);
            output.WriteLine($"L{lc.Num} «{lc.Name}»: ΣF = ({string.Join("; ", Enumerable.Range(0, 3).Select(c => (Sum(c) / 1e3).ToString("0.###", CultureInfo.InvariantCulture)))}) кН");
            if (Environment.GetEnvironmentVariable("OPENCS_SCAD_LINEAR_DIAG") == "1")
            {
                // Как протокол SCAD «Суммарные внешние нагрузки»: без сил, приложенных по направлению связей.
                var f = loads[k];
                var fixedDofs = build.Bc.FixedDofs.ToHashSet();
                double Free(int c) => m.Nodes.Select(n => build.Dof(n.Id, c)).Where(d => !fixedDofs.Contains(d)).Sum(d => f[d]);
                output.WriteLine($"  ΣF без закреплённых направлений = ({string.Join("; ", Enumerable.Range(0, 3).Select(c => (Free(c) / 1e3).ToString("0.###", CultureInfo.InvariantCulture)))}) кН; " +
                    $"маски закреплений: {string.Join(", ", m.Supports.GroupBy(x => x.Mask).Select(g => $"0x{g.Key:X} — {g.Count()}"))}");
            }
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
                if (Environment.GetEnvironmentVariable("OPENCS_SCAD_LINEAR_DIAG") == "1" && res > 0.05)
                    foreach (var w in pairs.OrderByDescending(p => Math.Abs(p.C - p.S)).Take(6).Append(peak))
                    {
                        var nd = m.Nodes.Single(n => n.Id == w.Id);
                        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                            $"    узел {w.Id} ({nd.X:0.###}; {nd.Y:0.###}; {nd.Z:0.###}): SCAD {w.S * 1e3:0.####}, CSfea {w.C * 1e3:0.####}; " +
                            $"закрепление {string.Join(",", m.Supports.Where(x => x.NodeId == w.Id).Select(x => "0x" + x.Mask.ToString("X")))}; " +
                            $"связи SCAD {(am.Bounds.TryGetValue(w.Id, out int bm) ? "0x" + bm.ToString("X") : "нет")}; " +
                            $"тела {string.Join(",", m.RigidBodies.Where(x => x.Master == w.Id || x.Slaves.Contains(w.Id)).Select(x => $"{x.Id}{(x.Master == w.Id ? "в" : "п")} маска 0x{x.Mask:X} узлов {x.Slaves.Count}"))}; " +
                            $"пружины {string.Join(",", m.Springs.Where(x => x.NodeId == w.Id).Select(x => $"{x.Dof}:{x.Stiffness:0.##e0}"))}; " +
                            $"пластины {string.Join(",", m.Shells.Where(x => x.NodeIds.Contains(w.Id)).Select(x => $"{x.Id} ж{elems[x.Id].StiffnessId}"))}; стержни: ") + string.Join("; ",
                            m.Beams.Where(b => b.NodeI == w.Id || b.NodeJ == w.Id).Select(b =>
                                $"{b.Id} ж{elems[b.Id].StiffnessId} {(b.NodeI == w.Id ? "I" : "J")} осв {(b.NodeI == w.Id ? b.ReleaseI : b.ReleaseJ)} " +
                                $"y=({string.Join(",", b.RefVec!.Select(v => v.ToString("0.##", CultureInfo.InvariantCulture)))})")));
                    }
            }
        }
    }
}
