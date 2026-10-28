using System.Globalization;
using System.Text.Json;
using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// ГУ разрезов во входе CSfea фрагмента (срез 4 C/D, задача 5): маски линейного родителя → пресеты → линейный CSfea
/// фрагмента. Сетка фрагмента собрана вручную совпадающей с сеткой родителя в полосе (плита 4×4, куски колонн одним КЭ),
/// поэтому при «Как в родителе» прогибы плиты повторяют родителя точно: у колонн без хомутов нет сдвиговой податливости,
/// и перемещения маски внутри КЭ колонны по Эрмиту — точные.
/// </summary>
public sealed partial class StoreyFragmentMaskServiceTests
{
    /// <summary>
    /// Сетка фрагмента: плита — те же узлы и КЭ, что у родителя (SourceMemberTag — плита фрагмента), каждый КонЭ-стержень
    /// — один КЭ с сечением колонн <paramref name="columnId"/> (части колонн синтетического родителя построитель называет
    /// «КЭ без КонЭ · n» и сечения не переносит — пробел среза 3, здесь не проверяется); узлы схемы фрагмента привязаны к
    /// узлам сетки по координате.
    /// </summary>
    static void MeshFragment(DatabaseService db, FemSchema fragment, int columnId)
    {
        var nodes = db.GetFemNodes(fragment.Id);
        var members = db.GetFemMembers(fragment.Id);
        var slab = members.Single(m => m.ElemType == "shell");
        var mesh = new List<FemMeshNode>();
        var elements = new List<FemElement>();
        for (int j = 0; j <= 4; j++)
            for (int i = 0; i <= 4; i++)
                mesh.Add(new FemMeshNode { NodeTag = S(Slab(i, j)), X = -3 + 1.5 * i, Y = -3 + 1.5 * j, Z = 3 });
        for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++)
                elements.Add(new FemElement
                {
                    ElemTag = S(1 + j * 4 + i), ElemType = "shell", SourceMemberTag = slab.ElemTag, ThicknessM = 0.2,
                    NodeIdsJson = $"[{Slab(i, j)},{Slab(i + 1, j)},{Slab(i, j + 1)},{Slab(i + 1, j + 1)}]",
                });
        int next = 7000;
        FemMeshNode At(FemNode n)
        {
            var found = mesh.FirstOrDefault(m => Math.Abs(m.X - n.X) + Math.Abs(m.Y - n.Y) + Math.Abs(m.Z - n.Z) < 1e-6);
            if (found != null) return found;
            found = new FemMeshNode { NodeTag = S(next++), X = n.X, Y = n.Y, Z = n.Z };
            mesh.Add(found);
            return found;
        }
        // NodeIdsJson КонЭ — теги узлов схемы.
        FemNode Node(int v) => nodes.Single(n => n.NodeTag == S(v));
        foreach (var m in members.Where(m => m.ElemType == "beam"))
        {
            var ids = JsonSerializer.Deserialize<int[]>(m.NodeIdsJson)!;
            var (a, b) = (At(Node(ids[0])), At(Node(ids[1])));
            elements.Add(new FemElement
            {
                ElemTag = S(next++), ElemType = "beam", SourceMemberTag = m.ElemTag, CrossSectionId = columnId,
                NodeIdsJson = $"[{a.NodeTag},{b.NodeTag}]",
            });
        }
        foreach (var n in nodes) At(n).SourceNodeTag = n.NodeTag;
        db.SaveFemMeshSnapshot(fragment.Id, mesh, elements);
    }

    /// <summary>Родитель посчитан линейно, маски сняты, сетка фрагмента собрана; стадии фрагмента — по загружениям.</summary>
    (Parent P, FemAnalysis Analysis, FemAnalysisStage[] Stages) Prepared()
    {
        var p = Create();
        var db = p.Db;
        var lc = db.GetFemLoadCases(p.Schema.Id);
        var parentStages = new[] { Single("Пост.", lc[0].Id), Single("Ветер", lc[1].Id) };
        var analysis = SaveResult(db, p.Schema, FemCsfeaRunner.LinearAnalysisKind, parentStages, parentStages);
        var extracted = StoreyFragmentMaskService.Extract(db, p.Fragment, FragmentMaskSet.SourceCsfeaLinear, S(analysis.Id));
        Assert.False(extracted.HasErrors, string.Join("\n", extracted.Diagnostics.Select(d => d.Message)));
        MeshFragment(db, p.Fragment, p.Column.Id);
        db.LoadAll();
        db.CrossSections.Add(p.Column);
        var flc = db.GetFemLoadCases(p.Fragment.Id);
        return (p with { Fragment = db.FemSchemas.Single(s => s.Id == p.Fragment.Id) }, analysis,
            [Single("Пост.", flc[0].Id), Single("Ветер", flc[1].Id)]);
    }

    static (FemRcModelInput Input, FragmentCutBoundaryResult? Bc, RcLinearRun Run) RunFragment(DatabaseService db, FemSchema fragment,
        FemAnalysisStage[] stages)
    {
        var input = FemCsfeaInputBuilder.Build(db, fragment.Id, new FemCsfeaSetup { Stages = stages, GrossBars = true }, out var bc);
        Assert.DoesNotContain(input.Diagnostics, d => d.IsError);
        var adapted = FemRcModelAdapter.Adapt(input);
        Assert.False(adapted.HasErrors, string.Join("; ", adapted.Report));
        return (input, bc, RcLinearAnalysis.Run(adapted.Model, new RcLinearOptions { ShellForceAngles = adapted.ShellForceAngles }));
    }

    static Dictionary<int, double[]> Displacements(CSfea.CScoreBridge.Structural.RcSecantStepFields f)
    {
        var result = new Dictionary<int, double[]>();
        for (int i = 0; i < f.NodeIds.Length; i++)
            result[f.NodeIds[i]] = f.Displacements.Skip(RcSecantStepFields.NodeComponents * i).Take(6).ToArray();
        return result;
    }

    [Fact]
    public void Build_Kinematic_FragmentRepeatsParentSlab()
    {
        var (p, analysis, stages) = Prepared();
        using var db = p.Db;

        var (input, bc, run) = RunFragment(db, p.Fragment, stages);

        Assert.NotNull(bc);
        Assert.Empty(bc.Gauge);
        Assert.Equal(8 * 6 * 2, bc.Prescribed.Count);
        Assert.Equal(8 * 6 * 2, input.MeshNodeKinematicLoads.Count);
        for (int i = 0; i < 2; i++)
        {
            var parent = Displacements(FemCsfeaStepFieldsCodec.Unpack(db.GetFemResultStep(analysis.ResultId!.Value, i + 1)!));
            var fragment = Displacements(run.Cases[i].Fields);
            // Узлы разрезов — ровно маска.
            foreach (var pr in bc.Prescribed.Where(x => x.LoadCaseId == stages[i].LoadCaseId()))
                Assert.Equal(pr.Value, fragment[int.Parse(pr.MeshNodeTag, CultureInfo.InvariantCulture)][pr.Dof], 12);
            // Плита — как у родителя.
            var slab = Enumerable.Range(0, 25).Select(k => Slab(k % 5, k / 5)).ToList();
            double scale = slab.Max(n => Math.Max(Math.Abs(parent[n][0]), Math.Max(Math.Abs(parent[n][1]), Math.Abs(parent[n][2]))));
            Assert.True(scale > 1e-6, $"стадия {i}: перемещения плиты родителя не малы");
            foreach (int n in slab)
                for (int d = 0; d < 3; d++)
                    Assert.True(Math.Abs(fragment[n][d] - parent[n][d]) <= 1e-6 * scale,
                        $"стадия {i}, узел {n}, DOF {d}: фрагмент {fragment[n][d]:E4}, родитель {parent[n][d]:E4}, масштаб {scale:E3}");
        }
    }

    [Fact]
    public void Build_Static_GaugeReactionsVanish()
    {
        var (p, _, stages) = Prepared();
        using var db = p.Db;
        db.SetFragmentBcPreset(p.Fragment, nameof(FragmentBcPreset.Static));

        var (input, bc, run) = RunFragment(db, p.Fragment, stages);

        Assert.NotNull(bc);
        Assert.Equal(6, bc.Gauge.Count);
        Assert.Empty(input.MeshNodeKinematicLoads);
        Assert.Contains(input.Diagnostics, d => d.Code == "fragment_gauge");
        for (int i = 0; i < 2; i++)
        {
            var f = run.Cases[i].Fields;
            var index = f.ReactionNodeIds.Select((id, k) => (id, k)).ToDictionary(x => x.id, x => x.k);
            double? Reaction(string tag, int dof) => index.TryGetValue(int.Parse(tag, CultureInfo.InvariantCulture), out int k)
                ? f.Reactions[6 * k + dof] : null;
            var terms = input.Stages[i].Terms;
            Assert.True(bc.ForceScale[terms[0].LoadCaseId] > 1e3);
            Assert.Null(FragmentCutBoundary.CheckGaugeReactions(bc, terms, Reaction, input.Stages[i].Tag));
            // Та же сверка с порогом в 50 раз жёстче — запас по равновесию масок.
            double sum = bc.Gauge.Sum(g => Math.Abs(Reaction(g.MeshNodeTag, g.Dof) ?? 0));
            Assert.True(sum <= 2e-4 * bc.ForceScale[terms[0].LoadCaseId], $"стадия {i}: Σ|R| автозакреплений {sum:0.###} Н");
        }
    }

    [Fact]
    public void Build_WithoutMasks_KinematicIsInputError_IsolationRuns()
    {
        var p = Create();
        using var db = p.Db;
        MeshFragment(db, p.Fragment, p.Column.Id);
        db.LoadAll();
        db.CrossSections.Add(p.Column);
        var fragment = db.FemSchemas.Single(s => s.Id == p.Fragment.Id);
        var flc = db.GetFemLoadCases(fragment.Id);
        var setup = new FemCsfeaSetup { Stages = [Single("Пост.", flc[0].Id)], GrossBars = true };

        var kinematic = FemCsfeaInputBuilder.Build(db, fragment.Id, setup);
        db.SetFragmentBcPreset(fragment, nameof(FragmentBcPreset.Isolation));
        var isolation = FemCsfeaInputBuilder.Build(db, fragment.Id, setup);

        Assert.Contains(kinematic.Diagnostics, d => d.Code == "fragment_masks_missing" && d.IsError);
        Assert.DoesNotContain(isolation.Diagnostics, d => d.IsError);
        Assert.Equal(8, isolation.Supports.Count(s => s.Origin == FemCsfeaInputBuilder.FragmentCutOrigin));
        Assert.False(FemRcModelAdapter.Adapt(isolation).HasErrors);
    }
}

static class FemAnalysisStageTestExtensions
{
    /// <summary>Id единственного загружения стадии «Single».</summary>
    public static int LoadCaseId(this FemAnalysisStage stage) => FemLoadExpression.Parse(stage.LoadExpressionJson).LoadCaseIds.Single();
}
