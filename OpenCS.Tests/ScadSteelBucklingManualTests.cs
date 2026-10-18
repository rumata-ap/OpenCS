using System.Diagnostics;
using System.Globalization;
using CSfea.CScoreBridge.Structural;
using OpenCS.OpenSees.CScore;
using OpenCS.Services.Scad;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручной прогон автоматических расчётных длин (срезы 0–3) по схеме SCAD: .SPR → <see cref="ScadRcModelAdapter"/>
/// (упругие стержни и пластины, ГУ, загружения) → расчётные элементы — цепочки соосных стержней между узлами
/// с примыканиями, опорами, пружинами или жёсткими телами → сочетания: каждое загружение и сумма всех →
/// определяющие сочетания, общая и локальные задачи, μ. OPENCS_SCAD_BUCKLING_SPR — модель (без неё тест выходит);
/// OPENCS_SCAD_BUCKLING_MODES — число форм (20).
/// </summary>
public class ScadSteelBucklingManualTests(ITestOutputHelper output)
{
    [Fact]
    public void EffectiveLengths()
    {
        string? spr = Environment.GetEnvironmentVariable("OPENCS_SCAD_BUCKLING_SPR");
        if (string.IsNullOrWhiteSpace(spr)) return;
        int modes = int.TryParse(Environment.GetEnvironmentVariable("OPENCS_SCAD_BUCKLING_MODES"), out int mm) ? mm : 20;
        string dir = Environment.GetEnvironmentVariable("OPENCS_SCAD_DIR") ?? ScadInstallLocator.FindDllDirectory()!;
        var sw = Stopwatch.StartNew();
        var ci = CultureInfo.InvariantCulture;
        void Log(string s) => output.WriteLine($"[{sw.Elapsed.TotalSeconds,6:0.0} с] {s}");

        var native = ScadApiNative.Load(dir);
        using var s = new ScadApiSession(native);
        s.Open(spr);
        var data = ScadApiReader.Read(s, new ScadReadOptions(), null, CancellationToken.None).Data;
        var am = data.AnalysisModel!;
        Log($"Схема: узлов {data.Nodes.Count}, КЭ {data.Elements.Count}, загружений {am.LoadCases.Count}");
        foreach (var lc in am.LoadCases) output.WriteLine($"  L{lc.Num} «{lc.Name}»");
        var stiff = data.Stiffnesses.ToDictionary(x => x.Id);
        foreach (var x in data.Stiffnesses)
            output.WriteLine($"  ж{x.Id}: {x.Text?[..Math.Min(x.Text.Length, 90)]}");
        var elems = data.Elements.ToDictionary(e => e.Id);
        var steel = CScore.Import.ScadSteelProfiles.ResolveAll(
            data.Stiffnesses.Where(x => x.Text != null).Select(x => (x.Id, x.Text!)),
            b => File.Exists(Path.Combine(dir, b + ".PRF")) ? CScore.Import.ScadPrfReader.Read(Path.Combine(dir, b + ".PRF")) : null);
        foreach (var p in steel)
            output.WriteLine($"  жёсткость {p.Num} {p.Source} → {(p.Shape is { } sh ? $"{sh.Kind} {sh.Name}" : p.Reason)}");

        // Сочетания: каждое загружение и сумма всех.
        var stages = am.LoadCases.Select(l => new ScadShellStage($"L{l.Num}", [(l.Num, 1.0)], 1.0)).ToList();
        stages.Add(new ScadShellStage("ΣL", am.LoadCases.Select(l => (l.Num, 1.0)).ToList(), 1.0));
        var adapted = ScadRcModelAdapter.Adapt(new ScadRcModelInput
        {
            Data = data,
            PlateSection = id => new ScadShellElementSection(new CScore.PlateSection
                { Tag = $"ж{elems[id].StiffnessId}", H = stiff.GetValueOrDefault(elems[id].StiffnessId)?.ThicknessM ?? 0.2 },
                $"elastic|{elems[id].StiffnessId}"),
            ShellSection = ScadRcModelAdapter.ElasticShells(3e10, 0.2),
            SteelShapes = steel.Where(p => p.Shape != null).ToDictionary(p => p.Num, p => p.Shape!),
            Stages = stages,
        });
        var m = adapted.Model;
        Log($"Модель: узлов {m.Nodes.Count}, пластин {m.Shells.Count}, стержней {m.Beams.Count}, тел {m.RigidBodies.Count}, пружин {m.Springs.Count}");
        foreach (var line in adapted.Report.Take(30)) output.WriteLine("  " + line);

        var units = Chains(m);
        Log($"Расчётных элементов (цепочек): {units.Count}, стержней в цепочках: {units.Sum(u => u.Count)}");
        var options = new RcBucklingOptions(Modes: modes);
        var model = RcBucklingAnalysis.Prepare(m, units, options);
        Log($"Модель устойчивости: стержней сетки {model.Build.Mesh.Beams.Count}, NDof {model.Build.Mesh.NDof}");
        var combos = m.Stages.Select(st => st.Loads).ToList();
        var governing = RcBucklingAnalysis.Governing(model, units, combos);
        Log($"Определяющие сочетания: {string.Join(", ", governing.Where(g => g.Combination >= 0).GroupBy(g => g.Combination).Select(g => $"{m.Stages[g.Key].Name} — {g.Count()}"))}; без сжатия — {governing.Count(g => g.Combination < 0)}");

        var lengths = new RcBucklingUnitLength?[units.Count];
        foreach (var group in governing.Select((g, i) => (g.Combination, Unit: i)).Where(x => x.Combination >= 0).GroupBy(x => x.Combination))
        {
            var r = RcBucklingAnalysis.Solve(model, combos[group.Key], options);
            Log($"{m.Stages[group.Key].Name}: λ = {string.Join("; ", r.Lambda.Take(8).Select(l => l.ToString("0.###", ci)))}{(r.Lambda.Length > 8 ? " …" : "")}" +
                $"{(r.Converged ? "" : " (не сошлось)")}");
            if (r.Lambda.Length == 0 && Environment.GetEnvironmentVariable("OPENCS_SCAD_BUCKLING_DIAG") == "1")
            {
                var mesh = model.Build.Mesh;
                Log($"  ДИАГ: max|u| = {r.URef.Max(Math.Abs):g4}, max|N| = {Enumerable.Range(0, mesh.Beams.Count).Max(b => Math.Abs(mesh.BeamAxialForce(b, r.URef))):g4}");
                foreach (int nm in new[] { 1, 5, 40 })
                {
                    var t = mesh.SolveBuckling(r.URef, model.Build.Bc, nm, maxIter: 1000);
                    Log($"  ДИАГ: форм {nm}: итераций {t.Iterations}, сошлось {t.Converged}, λ = {string.Join("; ", t.Lambda.Take(5).Select(l => l.ToString("g5", ci)))}");
                }
            }
            if (r.Lambda.Length > 0 && r.Lambda[0] <= 1) { Log("  схема неустойчива — μ не назначаются"); continue; }
            var ids = group.Select(x => x.Unit).ToList();
            var local = RcBucklingAnalysis.SolveLocal(r, units, ids);
            Log($"  локальные задачи: {ids.Count} эл.");
            var all = RcBucklingLengths.Assign(r, units, null, local);
            foreach (int u in ids) lengths[u] = all[u];
        }

        string P(RcBucklingPlaneLength p) => FormattableString.Invariant($"{p.Source,-11} μ = {p.Mu,6:0.###}") +
            (p.Mode >= 0 ? FormattableString.Invariant($" (ф.{p.Mode + 1}, λ {p.Lambda:0.##}, ρ/λ {p.Rho / p.Lambda:0.##})") : "") +
            (double.IsFinite(p.LocalNcr) ? FormattableString.Invariant($" лок{(p.LocalBraced ? "б/с" : "")} {p.LocalNcr / 1e3:0.#} кН") : "");
        output.WriteLine("Элемент | ж | l, м | N, кН | сочетание | вокруг z | вокруг y");
        for (int i = 0; i < units.Count; i++)
        {
            if (lengths[i] is not { } x) continue;
            var u = units[i];
            output.WriteLine(string.Create(ci, $"КЭ {u[0]}{(u.Count > 1 ? $"–{u[^1]} ({u.Count})" : "")} | ж{elems[u[0]].StiffnessId} | {x.Length:0.##} | " +
                $"{x.N / 1e3:0.#} | {m.Stages[governing[i].Combination].Name} | {P(x.AboutZ)} | {P(x.AboutY)}"));
        }
        foreach (var g in lengths.Where(x => x != null).SelectMany(x => new[] { x!.AboutZ.Source, x.AboutY.Source }).GroupBy(x => x))
            output.WriteLine($"  {g.Key}: {g.Count()} (по плоскостям)");
        Log("Готово.");
    }

    // Цепочки соосных стержней: узел внутри цепочки — ровно два стержня на одной прямой, без пластин, опор, пружин и
    // жёстких тел.
    static List<IReadOnlyList<int>> Chains(RcStructuralModel m)
    {
        var xyz = m.Nodes.ToDictionary(n => n.Id, n => new[] { n.X, n.Y, n.Z });
        var byNode = new Dictionary<int, List<RcBeam>>();
        foreach (var b in m.Beams)
            foreach (int n in new[] { b.NodeI, b.NodeJ })
            {
                if (!byNode.TryGetValue(n, out var list)) byNode[n] = list = [];
                list.Add(b);
            }
        var blocked = m.Shells.SelectMany(sh => sh.NodeIds).Concat(m.Supports.Select(x => x.NodeId))
            .Concat(m.Springs.Select(x => x.NodeId)).Concat(m.RigidBodies.SelectMany(r => r.Slaves.Append(r.Master))).ToHashSet();
        double[] Dir(RcBeam b)
        {
            var (a, c) = (xyz[b.NodeI], xyz[b.NodeJ]);
            var d = new[] { c[0] - a[0], c[1] - a[1], c[2] - a[2] };
            double len = Math.Sqrt(d.Sum(v => v * v));
            return d.Select(v => v / len).ToArray();
        }
        bool Inner(int n)
        {
            if (blocked.Contains(n) || byNode[n].Count != 2) return false;
            var (a, b) = (Dir(byNode[n][0]), Dir(byNode[n][1]));
            return Math.Abs(a[0] * b[0] + a[1] * b[1] + a[2] * b[2]) > 0.9999;
        }
        var done = new HashSet<int>();
        var result = new List<IReadOnlyList<int>>();
        foreach (var b in m.Beams)
        {
            if (!done.Add(b.Id)) continue;
            var chain = new LinkedList<int>([b.Id]);
            foreach (bool forward in new[] { true, false })
            {
                var cur = b;
                int node = forward ? b.NodeJ : b.NodeI;
                while (Inner(node))
                {
                    var next = byNode[node].First(x => x.Id != cur.Id);
                    if (!done.Add(next.Id)) break;
                    if (forward) chain.AddLast(next.Id); else chain.AddFirst(next.Id);
                    node = next.NodeI == node ? next.NodeJ : next.NodeI;
                    cur = next;
                }
            }
            result.Add(chain.ToList());
        }
        return result;
    }
}
