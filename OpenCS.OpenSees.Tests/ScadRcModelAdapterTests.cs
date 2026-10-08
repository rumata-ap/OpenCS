using CScore;
using CSfea.CScoreBridge.Structural;
using CSfea.Core;
using OpenCS.OpenSees.CScore;

namespace OpenCS.OpenSees.Tests;

/// <summary>Адаптер схемы SCAD → RcStructuralModel (CSfea) на синтетической схеме «плита 2 × 2 КЭ на колонне».</summary>
public class ScadRcModelAdapterTests
{
    static ScadRcModelInput Input(global::CScore.Import.ScadSchemaData d) => new()
    {
        Data = d,
        PlateSection = _ => new ScadShellElementSection(new PlateSection { H = 0.2, ConcreteMaterialId = 1, RebarMaterialId = 2 }, "h200"),
        ShellSection = ScadRcModelAdapter.ElasticShells(3e10, 0.2),
        Stages =
        [
            new ScadShellStage("L1", [(1, 1.0)], 1.0),
            new ScadShellStage("L2", [(2, 1.0)], 0.2),
        ],
    };

    [Fact]
    public void Adapt_SyntheticPlateOnColumn()
    {
        var d = ScadShellModelAssemblerTests.Data();
        d.PlateAxisAngles[1] = 30;
        var r = ScadRcModelAdapter.Adapt(Input(d));
        var m = r.Model;

        Assert.Empty(r.Report);
        Assert.Equal(11, m.Nodes.Count);
        Assert.Equal(4, m.Shells.Count);
        Assert.Equal([1, 2, 5, 4], m.Shells.Single(s => s.Id == 1).NodeIds);
        Assert.Single(m.Shells.Select(s => s.Section).Distinct());
        var beam = Assert.Single(m.Beams);
        Assert.Equal((20, 10), (beam.NodeI, beam.NodeJ));
        Assert.Equal(0.09, beam.Section.Elastic!.A, 9);
        var body = Assert.Single(m.RigidBodies);
        Assert.Equal((10, 0x3F), (body.Master, body.Mask));
        Assert.Equal([5], body.Slaves);
        Assert.Equal(0x3F, Assert.Single(m.Supports, s => s.NodeId == 20).Mask);

        // Оси пластин: КЭ 1 — ось X1 (узел 1 → 2, глобальная X), повёрнутая на 30°; остальные — X1.
        var ax1 = m.Shells.Single(s => s.Id == 1).SectionAxisX!;
        Assert.Equal(Math.Cos(Math.PI / 6), ax1[0], 9);
        Assert.Equal(Math.Sin(Math.PI / 6), ax1[1], 9);
        Assert.Equal(1.0, m.Shells.Single(s => s.Id == 2).SectionAxisX![0], 9);

        // Нагрузки — как в сборке OpenSees: вес 26 750 Н, давление 4000 Н; L2 по 0,2 → 5 шагов.
        Assert.Equal(26750, r.StageTotalDownN[0], 6);
        Assert.Equal(4000, r.StageTotalDownN[1], 6);
        Assert.Equal(5, m.Stages[1].Steps);
        var os = ScadShellModelAssembler.Assemble(new ScadShellModelInput
        {
            Data = d, PlateSection = Input(d).PlateSection, Resolver = new ElasticPlateMaterialResolver(3e10, 0.2),
            Stages = Input(d).Stages,
        });
        Assert.All(os.StageTotalDownN.Zip(r.StageTotalDownN), t => Assert.Equal(t.First, t.Second, 6));
    }

    [Fact]
    public void Adapt_BuildAndSolveLinear_Equilibrium()
    {
        var d = ScadShellModelAssemblerTests.Data();
        var r = ScadRcModelAdapter.Adapt(Input(d));
        var build = RcStructuralMeshBuilder.Build(r.Model, new LinearRcSectionFactory());
        Assert.Empty(build.Report);
        Assert.NotNull(build.Mesh.Links);
        foreach (var (stage, total) in r.Model.Stages.Zip(r.StageTotalDownN))
        {
            var f = build.Combination(stage.Loads);
            var u = build.Mesh.SolveLinear(f, build.Bc);
            var reactions = build.Mesh.ComputeReactions(u, build.Bc, fExternal: f);
            // Опора — низ колонны: вертикальная реакция = вся нагрузка стадии (включая вес в опорном узле).
            Assert.Equal(total, reactions[build.Dof(20, 2)], 6);
            // Плита симметрична относительно колонны: прогибы углов равны, жёсткое тело переносит перемещение.
            double w1 = u[build.Dof(1, 2)], w9 = u[build.Dof(9, 2)];
            Assert.True(w1 < 0);
            Assert.Equal(w1, w9, 12);
            Assert.Equal(u[build.Dof(10, 2)], u[build.Dof(5, 2)], 15);
        }
    }

    [Fact]
    public void Adapt_PartialRigidMask_Kept()
    {
        var d = ScadShellModelAssemblerTests.Data();
        d.AnalysisModel!.RigidBodies[0] = new global::CScore.Import.ScadRigidBody(12, 3, 10, [5], 0b011100);
        var r = ScadRcModelAdapter.Adapt(Input(d));
        Assert.Equal(0b011100, Assert.Single(r.Model.RigidBodies).Mask);
    }

    /// <summary>
    /// Трапеция в общей системе (Qw 17, Qn 2) и узловая сила (Qw 0, Qn 1): значения трапеции — по узлам в порядке SCAD
    /// «1 2 4 3», положительные силы SCAD — против осей. КЭ 1 (узлы 1, 2, 4, 5 → x = 0, 1, 0, 1): q = 6000 Па при x = 0,
    /// 0 при x = 1 → согласованные силы +Y: 1000 Н в узлах 1, 4 и 500 Н в узлах 2, 5.
    /// </summary>
    [Fact]
    public void Adapt_TrapezoidGlobalY_AndNodeForce()
    {
        var d = ScadShellModelAssemblerTests.Data();
        d.AnalysisModel!.LoadCases.Add(new global::CScore.Import.ScadLoadCase(3, "грунт",
            [new global::CScore.Import.ScadLoadRecord(0, 1, [-200], [9])],
            [new global::CScore.Import.ScadLoadRecord(17, 2, [-6000, 0, -6000, 0], [1])], []));
        var r = ScadRcModelAdapter.Adapt(Input(d));

        Assert.Empty(r.Report);
        var lc = r.Model.LoadCases.Single(l => l.Id == 3);
        double[] F(int node) => lc.Nodal.Single(p => p.NodeId == node).Force;
        Assert.Equal(1000, F(1)[1], 9);
        Assert.Equal(1000, F(4)[1], 9);
        Assert.Equal(500, F(2)[1], 9);
        Assert.Equal(500, F(5)[1], 9);
        Assert.All(lc.Nodal, p => Assert.Equal(0, p.Force[2], 12));
        Assert.Equal(200, F(9)[0], 12);
    }

    /// <summary>Основание под двумя КЭ плиты: C1 из вложения, ΣR опоры + Σ сил основания = нагрузка стадии.</summary>
    [Fact]
    public void Adapt_FoundationC1_ToShells_AndEquilibrium()
    {
        var d = ScadShellModelAssemblerTests.Data();
        d.AnalysisModel!.Beds.Add(new global::CScore.Import.ScadBed(73, [2e7, 0, 1e7], [1, 2]));
        var r = ScadRcModelAdapter.Adapt(Input(d));

        Assert.Equal(2e7, r.Model.Shells.Single(s => s.Id == 1).FoundationC1);
        Assert.Null(r.Model.Shells.Single(s => s.Id == 3).FoundationC1);
        Assert.Contains(r.Report, l => l.Contains("C1: 2 пластин"));
        Assert.Contains(r.Report, l => l.Contains("C2 и прочие"));

        var build = RcStructuralMeshBuilder.Build(r.Model, new LinearRcSectionFactory());
        var f = build.Combination(r.Model.Stages[0].Loads);
        var u = build.Mesh.SolveLinear(f, build.Bc);
        var reactions = build.Mesh.ComputeReactions(u, build.Bc, fExternal: f);
        var bed = build.Bc.AssembleKSpring().ToCsc().Multiply(u);
        double bedUp = -bed.Where((_, i) => i % 6 == 2).Sum();
        Assert.True(bedUp > 0);
        Assert.Equal(r.StageTotalDownN[0], reactions[build.Dof(20, 2)] + bedUp, 6);
    }
}
