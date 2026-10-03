using System.Globalization;
using System.IO;
using System.Text;
using CScore.Import;
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.Services.Scad;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручной прогон «плита Дорфмана»: схема SCAD → ShellOpenSeesModel → OpenSees.exe (спека «Плита SCAD → нелинейный
/// OpenSees», срезы 2–4). OPENCS_SCAD_NL_SPR — модель SCAD; OPENSEES_HOME — каталог OpenSees; OPENCS_SCAD_SHELL_OUT —
/// каталог выгрузки CSV (необязательно). Без OPENCS_SCAD_NL_SPR тест сразу выходит.
/// </summary>
public class ScadShellOpenSeesManualTests(ITestOutputHelper output)
{
    /// <summary>Точки прогибомеров опыта (рис. 45 книги): 2, 4, 9.</summary>
    static readonly (string Name, double X, double Y)[] Points = [("2", 1.5, 4.5), ("4", 4.5, 4.5), ("9", 4.5, 7.5)];

    [Fact]
    public async Task Linear()
    {
        if (Read() is not { } data) return;
        var report = new List<string>();
        var stages = new List<ScadShellStage> { new("L1", [(1, 1.0)], 1.0), new("L2", [(2, 1.0)], 1.0) };
        var input = ScadShellScenario.Build(data, new ScadShellScenarioOptions(true, ScadShellMaterialMode.Experiment, stages), report);
        var built = ScadShellModelAssembler.Assemble(input);
        foreach (var s in report.Concat(built.Report)) output.WriteLine(s);
        output.WriteLine($"ΣZ: L1 = {built.StageTotalDownN[0] / 9810:0.###} т, L2 = {built.StageTotalDownN[1] / 9810:0.###} т");
        // Протокол SCAD «Суммарные внешние нагрузки» — без нагрузок в защемлённых узлах (половина веса нижних КЭ колонн).
        var fixedNodes = built.Model.Nodes.Where(n => n.Fixed.All(f => f)).Select(n => n.Tag).ToHashSet();
        double free = -built.Model.Stages[0].Loads.Where(l => !fixedNodes.Contains(l.NodeTag)).Sum(l => l.Fz) / 9810;
        output.WriteLine($"ΣZ L1 без защемлённых узлов = {free:0.###} т (SCAD 43,32 т)");
        Assert.Equal(43.32, free, 2);

        var result = await Run(built.Model, TimeSpan.FromMinutes(10));
        var steps = result.Steps.Where(s => s.Converged).ToList();
        var s1 = steps.Last(s => s.StageIndex == 0);
        var s2 = steps.Last(s => s.StageIndex == 1);
        var nodes = PointNodes(data);
        foreach (var (name, node) in nodes)
        {
            double w1 = Uz(s1, node), w2 = Uz(s2, node) - w1;
            output.WriteLine($"Точка {name} (узел {node}): L1 = {w1 * 1000:0.###} мм, L2 = {w2 * 1000:0.###} мм");
        }
        output.WriteLine($"Узел 510: L1 = {Uz(s1, 510) * 1000:0.###} мм, L2 = {(Uz(s2, 510) - Uz(s1, 510)) * 1000:0.###} мм " +
            "(SCAD линейный: −3,868 / −6,873 мм)");
        Export("linear", data, steps);
    }

    [Fact]
    public async Task Nonlinear()
    {
        if (Read() is not { } data) return;
        var report = new List<string>();
        var stages = new List<ScadShellStage> { new("L1", [(1, 1.0)], 0.25), new("L2", [(2, 1.0)], 0.2) };
        var input = ScadShellScenario.Build(data, new ScadShellScenarioOptions(false, ScadShellMaterialMode.Experiment, stages), report);
        var built = ScadShellModelAssembler.Assemble(input);
        foreach (var s in report.Concat(built.Report).Distinct()) output.WriteLine(s);
        output.WriteLine($"Секций пластин {built.Model.Sections.Count}, материалов {built.Model.Materials.Count}, " +
            $"fiber-стержней {built.Model.NonlinearBeamElements.Count}, упругих {built.Model.BeamElements.Count}");

        var result = await Run(built.Model, TimeSpan.FromMinutes(60));
        var steps = result.Steps.Where(s => s.Converged).ToList();
        var nodes = PointNodes(data);
        double[] w0 = nodes.Select(n => steps.Where(s => s.StageIndex == 0).Select(s => Uz(s, n.Node)).LastOrDefault()).ToArray();
        foreach (var s in steps)
            output.WriteLine($"стадия {s.StageIndex} λ = {s.LoadFactor:0.###}{(s.IsRefinement ? " (дробление)" : "")}: " +
                string.Join(", ", nodes.Select((n, k) => $"т.{n.Name} {Uz(s, n.Node) * 1000:0.##} мм" +
                    (s.StageIndex == 1 ? $" (L2 {(Uz(s, n.Node) - w0[k]) * 1000:0.##})" : ""))));
        output.WriteLine($"Статус: {result.Status}; артефакты: {result.ArtifactDirectory}");
        Export("nonlinear", data, steps);
    }

    ScadSchemaData? Read()
    {
        string? spr = Environment.GetEnvironmentVariable("OPENCS_SCAD_NL_SPR");
        if (string.IsNullOrWhiteSpace(spr)) return null;
        string dir = Environment.GetEnvironmentVariable("OPENCS_SCAD_DIR") ?? ScadInstallLocator.FindDllDirectory()!;
        var native = ScadApiNative.Load(dir);
        using var s = new ScadApiSession(native);
        s.Open(spr);
        return ScadApiReader.Read(s, new ScadReadOptions(), null, CancellationToken.None).Data;
    }

    async Task<ShellResult> Run(ShellOpenSeesModel model, TimeSpan timeout)
    {
        string exe = Path.Combine(Environment.GetEnvironmentVariable("OPENSEES_HOME") ?? @"C:\Tools\OpenSees", "bin", "OpenSees.exe");
        string root = Path.Combine(Path.GetTempPath(), "opencs-scad-shell");
        var runner = new ShellAnalysisRunner(new ShellTclGenerator(), new OpenSeesArtifactStore(root),
            new OpenSeesProcessRunner(), new ShellResultParser(), timeout);
        var run = await runner.RunAsync(model, exe, CancellationToken.None);
        output.WriteLine($"OpenSees: {run.Outcome} {run.ErrorMessage}; {run.ArtifactDirectory}");
        Assert.NotNull(run.Result);
        return run.Result!;
    }

    static List<(string Name, int Node)> PointNodes(ScadSchemaData data) =>
        Points.Select(p => (p.Name, data.Nodes.Where(n => Math.Abs(n.Z) < 1e-6)
            .OrderBy(n => Math.Pow(n.X - p.X, 2) + Math.Pow(n.Y - p.Y, 2)).First().Id)).ToList();

    static double Uz(RCShellStepResult s, int node) => s.Displacements.FirstOrDefault(d => d.NodeTag == node)?.Uz ?? double.NaN;

    /// <summary>CSV: шаг, стадия, λ, узел, x, y, uz (м) — все узлы плиты на каждом сошедшемся шаге.</summary>
    void Export(string name, ScadSchemaData data, IReadOnlyList<RCShellStepResult> steps)
    {
        string? dir = Environment.GetEnvironmentVariable("OPENCS_SCAD_SHELL_OUT");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        var xy = data.Nodes.ToDictionary(n => n.Id);
        var sb = new StringBuilder("step,stage,lambda,refinement,node,x,y,z,uz\n");
        foreach (var s in steps)
            foreach (var d in s.Displacements)
                if (xy.TryGetValue(d.NodeTag, out var n))
                    sb.Append(string.Join(",", s.StepIndex, s.StageIndex, F(s.LoadFactor), s.IsRefinement ? 1 : 0, d.NodeTag,
                        F(n.X), F(n.Y), F(n.Z), F(d.Uz))).Append('\n');
        File.WriteAllText(Path.Combine(dir, $"{name}-displacements.csv"), sb.ToString());
        output.WriteLine($"Выгрузка: {Path.Combine(dir, $"{name}-displacements.csv")}");
    }

    static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
}
