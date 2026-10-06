using CScore;
using CScore.Import;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

/// <summary>Сборка ShellOpenSeesModel из расчётной модели SCAD на синтетической схеме: плита 2 × 2 КЭ на колонне.</summary>
public class ScadShellModelAssemblerTests
{
    /// <summary>
    /// Плита 2 × 2 м из четырёх Q4 (узлы 1–9, сетка 1 м, порядок SCAD «1 2 4 3»), колонна 0,3 × 0,3 высотой 3 м под
    /// центром: узел 20 (z = −3, защемлён) → 10 (верх), жёсткое тело 10 → 5.
    /// </summary>
    internal static ScadSchemaData Data()
    {
        var d = new ScadSchemaData();
        int id = 1;
        for (int j = 0; j <= 2; j++)
            for (int i = 0; i <= 2; i++)
                d.Nodes.Add(new ScadNodeRecord(id++, i, j, 0));
        d.Nodes.Add(new ScadNodeRecord(10, 1, 1, 0));
        d.Nodes.Add(new ScadNodeRecord(20, 1, 1, -3));
        int N(int i, int j) => j * 3 + i + 1;
        int e = 1;
        for (int j = 0; j < 2; j++)
            for (int i = 0; i < 2; i++)
                d.Elements.Add(new ScadElementRecord(e++, 444, 1, [N(i, j), N(i + 1, j), N(i, j + 1), N(i + 1, j + 1)]));
        d.Elements.Add(new ScadElementRecord(11, 405, 2, [20, 10]));
        d.Elements.Add(new ScadElementRecord(12, 100, 3, [10, 5]));
        d.Stiffnesses.Add(ScadStiffnessParams.Parse(1, "GE 3e10 0.2 0.2 RO 25000", "плита", 1, 1));
        d.Stiffnesses.Add(ScadStiffnessParams.Parse(2, "S0 3e10 0.3 0.3 NU 0.2 RO 25000", "колонна", 1, 1));
        d.Stiffnesses.Add(ScadStiffnessParams.Parse(3, "SPRING 1 1 1 1 1 1 Type 100", null, 1, 1));
        int[] shells = [1, 2, 3, 4];
        d.AnalysisModel = new ScadAnalysisModel
        {
            Bounds = { [20] = 0x3F },
            RigidBodies = { new ScadRigidBody(12, 3, 10, [5], 0x3F) },
            LoadCases =
            {
                new ScadLoadCase(1, "вес", [], [new ScadLoadRecord(96, 3, [1.0], [.. shells]), new ScadLoadRecord(96, 3, [1.0], [11])], []),
                new ScadLoadCase(2, "q", [], [new ScadLoadRecord(16, 3, [1000], [.. shells])], []),
            },
        };
        return d;
    }

    static ScadShellModelInput Input(ScadSchemaData d) => new()
    {
        Data = d,
        PlateSection = _ => new ScadShellElementSection(new PlateSection { H = 0.2, ConcreteMaterialId = 1, RebarMaterialId = 2 }, "h200"),
        Resolver = new ElasticPlateMaterialResolver(3e10, 0.2),
        Stages =
        [
            new ScadShellStage("L1", [(1, 1.0)], 1.0),
            new ScadShellStage("L2", [(2, 1.0)], 0.2),
        ],
    };

    [Fact]
    public void Assemble_SyntheticPlateOnColumn()
    {
        var r = ScadShellModelAssembler.Assemble(Input(Data()));
        var m = r.Model;

        Assert.Equal(4, m.Elements.Count);
        Assert.All(m.Elements, e => Assert.Equal(ShellElementKind.ASDShellQ4, e.Kind));
        // Порядок SCAD «1 2 4 3» → обход контура.
        Assert.Equal([1, 2, 5, 4], m.Elements.Single(e => e.Tag == 1).NodeTags);
        Assert.Single(m.Sections);
        var beam = Assert.Single(m.BeamElements);
        Assert.Equal((20, 10), (beam.NodeI, beam.NodeJ));
        Assert.Equal(0.09, beam.A, 9);
        Assert.Equal(3e10 / 2.4, beam.G, 3);
        var link = Assert.Single(m.RigidLinks);
        Assert.Equal((10, 5, ShellRigidLinkType.Beam), (link.MasterNode, link.SlaveNode, link.Type));
        Assert.All(m.Nodes.Single(n => n.Tag == 20).Fixed, Assert.True);
        Assert.DoesNotContain(true, m.Nodes.Single(n => n.Tag == 5).Fixed);

        // Вес: плита 25 000·0,2·4 = 20 000 Н, колонна 25 000·0,09·3 = 6750 Н; давление 1000·4 = 4000 Н.
        Assert.Equal(26750, r.StageTotalDownN[0], 6);
        Assert.Equal(4000, r.StageTotalDownN[1], 6);
        var l2 = m.Stages[1].Loads.ToDictionary(l => l.NodeTag, l => l.Fz);
        Assert.Equal(-1000, l2[5], 9);
        Assert.Equal(-250, l2[1], 9);
        Assert.Equal(-500, l2[2], 9);
        Assert.Equal(0.2, m.Stages[1].LoadFactorStep);
        Assert.Empty(r.Report);
    }

    [Fact]
    public void AreaWeights_SkewQuad_SumToArea()
    {
        ScadNodeRecord[] p = [new(1, 0, 0, 0), new(2, 2, 0, 0), new(3, 2.5, 1, 0), new(4, 0, 1.5, 0)];
        double[] w = ScadShellModelAssembler.AreaWeights(p);
        // Площадь четырёхугольника по Гауссу (шнурование).
        double area = 0.5 * Math.Abs(0 * 0 - 2 * 0 + 2 * 1 - 2.5 * 0 + 2.5 * 1.5 - 0 * 1 + 0 * 0 - 0 * 1.5);
        Assert.Equal(area, w.Sum(), 9);
    }

    [Fact]
    public void Assemble_UnsupportedLoad_Reported()
    {
        var d = Data();
        d.AnalysisModel!.LoadCases.Add(new ScadLoadCase(3, "сосредоточенная", [new ScadLoadRecord(0, 3, [5], [5])], [], []));
        var input = Input(d);
        var r = ScadShellModelAssembler.Assemble(new ScadShellModelInput
        {
            Data = d, PlateSection = input.PlateSection, Resolver = input.Resolver,
            Stages = [new ScadShellStage("L3", [(3, 1.0)], 1.0), new ScadShellStage("L1", [(1, 1.0)], 1.0)],
        });
        Assert.Contains(r.Report, s => s.Contains("узловые Qw 0"));
    }
}
