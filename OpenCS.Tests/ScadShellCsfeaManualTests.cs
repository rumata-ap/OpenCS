using System.Globalization;
using System.IO;
using System.Text;
using CScore.Import;
using CSfea.CScoreBridge.Structural;
using OpenCS.OpenSees.CScore;
using OpenCS.Services.Scad;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручной прогон «плита Дорфмана» в CSfea: схема SCAD → RcStructuralModel → StructuralMesh (спека «Нелинейный расчёт
/// ЖБ оболочечно-стержневых схем в CSfea», срез 1). OPENCS_SCAD_NL_SPR — модель SCAD; OPENCS_SCAD_DIR — каталог
/// SCADAPIX (по умолчанию — найденная установка SCAD); OPENCS_CSFEA_OUT — каталог выгрузки CSV (необязательно).
/// Без OPENCS_SCAD_NL_SPR тест сразу выходит.
/// </summary>
public class ScadShellCsfeaManualTests(ITestOutputHelper output)
{
    /// <summary>Точки прогибомеров опыта (рис. 45 книги): 2, 4, 9.</summary>
    static readonly (string Name, double X, double Y)[] Points = [("2", 1.5, 4.5), ("4", 4.5, 4.5), ("9", 4.5, 7.5)];

    /// <summary>Узел центра плиты в модели SCAD.</summary>
    const int CenterNode = 510;

    /// <summary>Линейный расчёт L1 и L2 по отдельности против протокола SCAD: ΣZ(L1) = 43,32 т, центр −3,868 / −6,873 мм.</summary>
    [Fact]
    public void Linear()
    {
        if (Read() is not { } data) return;
        var (e, nu) = ScadShellScenario.ElasticPlate(data);
        var report = new List<string>();
        var scenario = ScadShellScenario.Build(data, new ScadShellScenarioOptions(true, ScadShellMaterialMode.Experiment,
            [new ScadShellStage("L1", [(1, 1.0)], 1.0), new ScadShellStage("L2", [(2, 1.0)], 1.0)]), report);
        var adapted = ScadRcModelAdapter.Adapt(new ScadRcModelInput
        {
            Data = data, PlateSection = scenario.PlateSection, ShellSection = ScadRcModelAdapter.ElasticShells(e, nu),
            Stages = scenario.Stages,
        });
        var build = RcStructuralMeshBuilder.Build(adapted.Model, new LinearRcSectionFactory());
        foreach (var s in report.Concat(adapted.Report).Concat(build.Report).Distinct()) output.WriteLine(s);
        output.WriteLine($"E = {e / 1e6:0} МПа, ν = {nu}; узлов {build.Mesh.NNodes}, оболочек {build.Mesh.Shells.Count}, " +
            $"стержней {build.Mesh.Beams.Count}, жёстких связей {build.Mesh.Links?.Links.Count ?? 0}");

        // Протокол SCAD «Суммарные внешние нагрузки» — без нагрузок в защемлённых узлах.
        var fixedNodes = adapted.Model.Supports.Where(s => s.Mask == 0x3F).Select(s => s.NodeId).ToHashSet();
        var lc1 = adapted.Model.LoadCases.Single(l => l.Id == 1);
        double free = -lc1.Nodal.Where(p => !fixedNodes.Contains(p.NodeId)).Sum(p => p.Force[2]) / 9810;
        output.WriteLine($"ΣZ L1 = {adapted.StageTotalDownN[0] / 9810:0.###} т, без защемлённых узлов {free:0.###} т (SCAD 43,32 т)");

        var u = adapted.Model.Stages.Select(st => build.Mesh.SolveLinear(build.Combination(st.Loads), build.Bc)).ToArray();
        for (int k = 0; k < u.Length; k++)
        {
            var f = build.Combination(adapted.Model.Stages[k].Loads);
            var r = build.Mesh.ComputeReactions(u[k], build.Bc, fExternal: f);
            double sumR = build.Bc.FixedDofs.Where(d => d % 6 == 2).Sum(d => r[d]);
            output.WriteLine($"{adapted.Model.Stages[k].Name}: ΣRz = {sumR / 9810:0.###} т, нагрузка {adapted.StageTotalDownN[k] / 9810:0.###} т");
        }
        var points = PointNodes(data);
        foreach (var (name, node) in points)
            output.WriteLine($"Точка {name} (узел {node}): L1 = {Uz(build, u[0], node) * 1000:0.###} мм, " +
                $"L2 = {Uz(build, u[1], node) * 1000:0.###} мм");
        double w1 = Uz(build, u[0], CenterNode) * 1000, w2 = Uz(build, u[1], CenterNode) * 1000;
        output.WriteLine($"Узел {CenterNode}: L1 = {w1:0.###} мм ({(w1 / -3.868 - 1) * 100:+0.00;-0.00} %), " +
            $"L2 = {w2:0.###} мм ({(w2 / -6.873 - 1) * 100:+0.00;-0.00} %) (SCAD линейный: −3,868 / −6,873 мм)");
        Export("linear", build, adapted.Model, u);

        Assert.Equal(43.32, free, 2);
        Assert.InRange(w1 / -3.868, 0.99, 1.01);
        Assert.InRange(w2 / -6.873, 0.99, 1.01);
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

    static List<(string Name, int Node)> PointNodes(ScadSchemaData data) =>
        Points.Select(p => (p.Name, data.Nodes.Where(n => Math.Abs(n.Z) < 1e-6)
            .OrderBy(n => Math.Pow(n.X - p.X, 2) + Math.Pow(n.Y - p.Y, 2)).First().Id)).ToList();

    static double Uz(RcStructuralMeshBuild b, double[] u, int node) => u[b.Dof(node, 2)];

    /// <summary>CSV: стадия, узел, x, y, z, uz (м) — все узлы плиты (z = 0) по стадиям.</summary>
    void Export(string name, RcStructuralMeshBuild b, RcStructuralModel model, IReadOnlyList<double[]> u)
    {
        string? dir = Environment.GetEnvironmentVariable("OPENCS_CSFEA_OUT");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("stage,node,x,y,z,uz\n");
        for (int k = 0; k < u.Count; k++)
            foreach (var n in model.Nodes.Where(n => Math.Abs(n.Z) < 1e-6))
                sb.Append(string.Join(",", model.Stages[k].Name, n.Id, F(n.X), F(n.Y), F(n.Z), F(Uz(b, u[k], n.Id)))).Append('\n');
        string path = Path.Combine(dir, $"csfea-{name}-displacements.csv");
        File.WriteAllText(path, sb.ToString());
        output.WriteLine($"Выгрузка: {path}");
    }

    static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
}
