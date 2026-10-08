using System.Diagnostics;
using CScore.Fem;
using CScore.Import;
using CSfea.CScoreBridge.Structural;
using OpenCS.OpenSees.CScore;
using OpenCS.Services.Scad;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручная сверка адаптера схемы FEM → CSfea (срез 4в) с адаптером сырых данных SCAD. OPENCS_SCAD_NL_SPR — плита
/// Дорфмана (.SPR); OPENCS_SCAD_DIR — каталог SCADAPIX (по умолчанию — найденная установка). Без переменной тест
/// сразу выходит.
/// </summary>
public class FemRcAdapterManualTests(ITestOutputHelper output)
{
    /// <summary>Узел центра плиты Дорфмана; линейный SCAD: −3,868 мм от L1, −6,873 мм от L2.</summary>
    const int CenterNode = 510;

    [Fact]
    public void DorfmanFromSpr()
    {
        string? spr = Environment.GetEnvironmentVariable("OPENCS_SCAD_NL_SPR");
        if (string.IsNullOrWhiteSpace(spr)) return;
        string dir = Environment.GetEnvironmentVariable("OPENCS_SCAD_DIR") ?? ScadInstallLocator.FindDllDirectory()!;
        ScadSchemaData data;
        using (var session = new ScadApiSession(ScadApiNative.Load(dir)))
        {
            session.Open(spr);
            data = ScadApiReader.Read(session, new ScadReadOptions(), null, CancellationToken.None).Data;
        }

        // Эталон — адаптер сырых данных SCAD.
        var (e, nu) = ScadShellScenario.ElasticPlate(data);
        var scenario = ScadShellScenario.Build(data, new ScadShellScenarioOptions(true, ScadShellMaterialMode.Experiment,
            [new ScadShellStage("L1", [(1, 1.0)], 1.0), new ScadShellStage("L2", [(2, 1.0)], 1.0)]), []);
        var reference = ScadRcModelAdapter.Adapt(new ScadRcModelInput
        {
            Data = data, PlateSection = scenario.PlateSection, ShellSection = ScadRcModelAdapter.ElasticShells(e, nu),
            Stages = scenario.Stages,
        });

        // Схема FEM в памяти: сетка, перенос ГУ и нагрузок, свойства по жёсткостям.
        var sw = Stopwatch.StartNew();
        var input = ScadSchemaInMemory(data, [("L1", 1), ("L2", 2)]);
        var adapted = FemRcModelAdapter.Adapt(input);
        output.WriteLine($"адаптер схемы FEM: {sw.ElapsedMilliseconds} мс");
        foreach (var line in adapted.Report) output.WriteLine("  " + line);
        Assert.False(adapted.HasErrors);

        var (m, r) = (adapted.Model, reference.Model);
        output.WriteLine($"узлы {m.Nodes.Count}/{r.Nodes.Count}, пластины {m.Shells.Count}/{r.Shells.Count}, " +
            $"стержни {m.Beams.Count}/{r.Beams.Count}, опоры {m.Supports.Count}/{r.Supports.Count}, " +
            $"пружины {m.Springs.Count}/{r.Springs.Count}, тела {m.RigidBodies.Count}/{r.RigidBodies.Count} (FEM / SCAD)");
        Assert.Equal(r.Shells.Count, m.Shells.Count);
        Assert.Equal(r.Beams.Count, m.Beams.Count);
        Assert.Equal(r.RigidBodies.Count, m.RigidBodies.Count);
        Assert.Equal(r.Supports.OrderBy(s => s.NodeId), m.Supports.OrderBy(s => s.NodeId));

        var refShells = r.Shells.ToDictionary(s => s.Id);
        double axisDiff = m.Shells.Max(s => Enumerable.Range(0, 3).Max(c => Math.Abs(s.SectionAxisX![c] - refShells[s.Id].SectionAxisX![c])));
        int contourDiff = m.Shells.Count(s => !s.NodeIds.SequenceEqual(refShells[s.Id].NodeIds));
        output.WriteLine($"оси x пластин: max|Δ| = {axisDiff:e2}; контуров с отличием {contourDiff}");
        Assert.True(axisDiff < 1e-9);
        Assert.Equal(0, contourDiff);

        for (int k = 0; k < 2; k++)
        {
            double down = -adapted.StageTotals[k].Fz;
            output.WriteLine($"{adapted.StageTotals[k].Tag}: ΣFz вниз {down / 9810:0.###} т (SCAD-адаптер {reference.StageTotalDownN[k] / 9810:0.###} т)");
            Assert.InRange(down / reference.StageTotalDownN[k], 0.9999, 1.0001);
        }

        var build = RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());
        double[] expected = [-3.868, -6.873];
        for (int k = 0; k < 2; k++)
        {
            var u = build.Mesh.SolveLinear(build.Combination(m.Stages[k].Loads), build.Bc);
            double w = u[build.Dof(CenterNode, 2)] * 1000;
            output.WriteLine($"{m.Stages[k].Name}: прогиб центра {w:0.###} мм (SCAD {expected[k]} мм, {(w / expected[k] - 1) * 100:+0.00;-0.00} %)");
            Assert.InRange(w / expected[k], 0.99, 1.01);
        }
    }

    /// <summary>
    /// Схема SCAD как схема FEM в памяти: сетка (<see cref="ScadSchemaConverter"/>), ГУ (<see cref="ScadBoundaryTransfer"/>),
    /// загружения и нагрузки (<see cref="ScadLoadTransfer"/>), свойства по жёсткостям; стадии — по номерам загружений SCAD.
    /// </summary>
    static FemRcModelInput ScadSchemaInMemory(ScadSchemaData data, IReadOnlyList<(string Tag, int ScadLoad)> stages)
    {
        var am = data.AnalysisModel!;
        var nodes = ScadSchemaConverter.ToFemMeshNodes(data, 1);
        var elements = ScadSchemaConverter.ToFemMeshElements(data, 1);
        var types = elements.ToDictionary(x => x.ElemTag, x => x.ElemType);
        var bc = ScadBoundaryTransfer.Transfer(am, nodes.Select(n => n.NodeTag).ToHashSet(), types);
        foreach (var el in elements)
            if (bc.ElementProps.TryGetValue(el.ElemTag, out var p))
                (el.ReleaseI, el.ReleaseJ, el.FoundationC1) = (p.ReleaseI, p.ReleaseJ, p.FoundationC1);
        int next = 0;
        var loads = ScadLoadTransfer.Transfer(am, types, [], [], [], () => --next);
        var stiffness = ScadSchemaConverter.ToSchemaStiffnesses(data).ToDictionary(s => s.Id);
        return new FemRcModelInput
        {
            MeshNodes = nodes, MeshElements = elements,
            Supports = bc.Supports, Springs = bc.Springs, RigidBodies = bc.RigidBodies,
            LoadCases = loads.LoadCases, ElementLoads = loads.ElementLoads, MeshNodeLoads = loads.MeshNodeLoads,
            Properties = new ScadElementStiffnessSource(stiffness, am.ForceUnitN, am.LengthUnitM),
            Stages = stages.Select(s => new FemRcStage(s.Tag,
                [(loads.LoadCases.Single(c => c.SourceLoadNum == s.ScadLoad).Id, 1.0)])).ToList(),
        };
    }
}
