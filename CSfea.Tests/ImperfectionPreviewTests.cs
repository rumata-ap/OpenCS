using System.Text.Json;
using CScore.Fem;
using CScore.Fem.Imperfections;
using CSfea.CScoreBridge.Structural;

namespace CSfea.Tests;

/// <summary>
/// Показ u₀ (срез 3): узловые перемещения несовершенств модели (<see cref="RcStructuralMeshBuild.ImperfectionNodeTranslations"/>)
/// — тот же расчёт, что поле решателя (<see cref="RcStructuralMeshBuild.Imperfection(RcStructuralModel)"/>): крен
/// φ·(z − z₀) и парабола погиби 4e₀·ξ(1 − ξ) складываются, в узле — наибольшее по модулю из сходящихся КЭ.
/// </summary>
[HarnessChecks]
public class ImperfectionPreviewTests
{
    private const double E = 30e9, G = 12.5e9, A = 0.16, I = 0.4 * 0.4 * 0.4 * 0.4 / 12, L = 6.0;
    private const int N = 8;

    private sealed class Props : IFemElementStiffnessSource
    {
        public FemShellStiffness? Shell(FemElement e) => null;
        public FemBarStiffness? Bar(FemElement e) => new(E, G, A, I, I, 2 * I);
        public double? UnitWeight(FemElement e) => null;
        public bool IsWithoutStiffness(FemElement e) => false;
        public double? BarArea(FemElement e) => A;
    }

    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("Несовершенство: узловые u₀ для показа — крен + погибь колонны");
        // Колонна «C» по z (узлы 1…N+1) и ригель «R» из верха по +Y (узлы N+2, N+3): погибь — только у колонны.
        var nodes = Enumerable.Range(0, N + 1).Select(i => new FemMeshNode { NodeTag = (i + 1).ToString(), Z = L * i / N }).ToList();
        nodes.Add(new FemMeshNode { NodeTag = (N + 2).ToString(), Y = 2, Z = L / 2 });
        var elements = Enumerable.Range(1, N).Select(i => new FemElement
        {
            ElemTag = i.ToString(), ElemType = "beam", NodeIdsJson = JsonSerializer.Serialize(new[] { i, i + 1 }), SourceMemberTag = "C",
        }).ToList();
        elements.Add(new FemElement
        {
            ElemTag = (N + 1).ToString(), ElemType = "beam", NodeIdsJson = JsonSerializer.Serialize(new[] { N / 2 + 1, N + 2 }), SourceMemberTag = "R",
        });
        var members = new List<FemMember> { new() { ElemTag = "C" }, new() { ElemTag = "R" } };
        var items = new[]
        {
            (new FemImperfection { Id = 1, Kind = FemImperfectionKinds.Sway, Tag = "Крен",
                ParamsJson = new FemSwayParams { PhiInv = 200, AngleDeg = 0 }.ToJson() }, 1.0),
            (new FemImperfection { Id = 2, Kind = FemImperfectionKinds.Bow, Tag = "Погибь",
                ParamsJson = new FemBowParams { MemberTags = ["C"], Plane = "y", Sign = FemBowParams.SignPlus,
                    Activity = FemBowParams.ActivityAlways, LOverE0 = 250 }.ToJson() }, 1.0),
        };
        var resolved = FemImperfectionResolver.Resolve(items, elements, nodes, members);
        TestHarness.Check("привязка без ошибок", !resolved.HasErrors, string.Join("; ", resolved.Diagnostics.Select(d => d.Message)));
        var input = new FemRcModelInput
        {
            MeshNodes = nodes, MeshElements = elements, Members = members, Properties = new Props(),
            Supports = [new FemMeshNodeSupport { NodeTag = "1", Mask = 0x3F }],
            LoadCases = [new FemLoadCase { Id = 1, Tag = "P" }, new FemLoadCase { Id = 9, Tag = "Imp", Sp20Type = FemSp20Types.Imperfection }],
            MeshNodeLoads = [new FemMeshNodeLoad { LoadCaseId = 1, MeshNodeTag = (N + 1).ToString(), Fz = -1e5 }],
            Stages = [new FemRcStage("S", [(1, 1.0)])], Imperfections = resolved.Items,
        };
        var adapted = FemRcModelAdapter.Adapt(input);
        TestHarness.Check("адаптер без ошибок", !adapted.HasErrors, string.Join("; ", adapted.Report));
        if (adapted.HasErrors) return;

        var u = RcStructuralMeshBuild.ImperfectionNodeTranslations(adapted.Model);
        double phi = 1.0 / 200, e0 = L / 250;
        TestHarness.CheckRel("верх колонны: ux = φ·L", u[N + 1][0], phi * L, 1e-12);
        TestHarness.CheckLess("низ колонны: u₀ = 0", Math.Abs(u[1][0]) + Math.Abs(u[1][1]), 1e-15);
        TestHarness.CheckRel("середина: ux = φ·L/2 + e₀ (погибь вдоль местной y — глобальная X)", u[N / 2 + 1][0], phi * L / 2 + e0, 1e-12);
        TestHarness.CheckRel("четверть: ux = φ·L/4 + 0,75·e₀", u[N / 4 + 1][0], phi * L / 4 + 0.75 * e0, 1e-12);
        TestHarness.CheckRel("конец ригеля: ux = φ·L/2 (только крен)", u[N + 2][0], phi * L / 2, 1e-12);

        // Сверка с полем решателя: u₀ концов КЭ колонны совпадает с узловым значением.
        var build = RcStructuralMeshBuilder.Build(adapted.Model, new LinearRcSectionFactory());
        var field = build.Imperfection(adapted.Model)!;
        double worst = 0;
        for (int e = 0; e < build.BeamIds.Length; e++)
        {
            var b = build.Mesh.Beams[e];
            if (field.Beam[e] is not { } fe || build.BeamIds[e] == N + 1) continue;
            for (int c = 0; c < 3; c++)
            {
                worst = Math.Max(worst, Math.Abs(fe[c] - u[build.NodeIds[b.I]][c]));
                worst = Math.Max(worst, Math.Abs(fe[6 + c] - u[build.NodeIds[b.J]][c]));
            }
        }
        TestHarness.CheckLess("узловые u₀ колонны ≡ поле решателя", worst, 1e-15);
        TestHarness.Check("без несовершенств — пусто", RcStructuralMeshBuild.ImperfectionNodeTranslations(new RcStructuralModel()).Count == 0);
    }
}
