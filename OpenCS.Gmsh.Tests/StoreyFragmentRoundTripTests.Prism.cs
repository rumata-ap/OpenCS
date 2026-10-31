using System.Globalization;
using CScore;
using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using CScore.Planar;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using static OpenCS.Gmsh.Tests.StoreyFragmentGmshFixture;

namespace OpenCS.Gmsh.Tests;

/// <summary>
/// Круговая проверка фрагмента вокруг колонны (спека призмы 6, срез 2): тот же родитель, призма 3 × 3 м вокруг колонны
/// (3, 3) — грани x, y = 1,5 и 4,5 на линиях сетки; полоса 1,5…4,5 м. Плита обрезана (4 боковых кромки), колонна —
/// 2 конца; узел 99 (1, 1, 3) с силами и стена y = 6 — вне призмы.
/// Сетка родителя и фрагмента — 0,25 м: при 0,5 м маски кромок плиты на гранях (поперечная сила и моменты по патчу КЭ
/// у колонны) не уравновешивают нагрузки призмы на 4–5 % (порог 2 %); при 0,25 м — без предупреждения. Сверка
/// равнодействующей кромки +Y с прямой суммой по КЭ и при 0,25 м — 5 % (предупреждение): грань на рёбрах КЭ, прямая
/// сумма берёт КЭ одной стороны, патч — обе.
/// </summary>
public sealed partial class StoreyFragmentRoundTripTests
{
    const double PrismStep = 0.25;

    static readonly FragmentPrism ColumnPrism = FragmentPrism.Create(3, 3, 1, 0, 1.5, 1.5);

    static bool InPrism(FemMeshNode n) => ColumnPrism.Contains(n.X, n.Y, Tol);

    /// <summary>Фрагмент вокруг колонны: сборка, сетка, маски <c>csfea_linear</c> без ошибок и без расхождения равновесия.</summary>
    async Task<FemSchema> CreatePrismFragment(DatabaseService db, Parent parent, string root, double step)
    {
        var built = StoreyFragmentService.Build(db, parent.Schema.Id, new StoreyFragmentRequest(Z1, Z2, "Вокруг колонны", Prism: ColumnPrism));
        foreach (var d in built.Draft.Diagnostics) output.WriteLine($"{(d.IsError ? "ОШИБКА" : "предупр.")} {d.Code}: {d.Message}");
        var fragment = Assert.IsType<FemSchema>(built.Schema);
        var cuts = db.GetFragmentCuts(fragment.Id);
        Assert.Equal(4, cuts.Count(c => c.Side == FragmentCutSide.Lateral && c.Kind == FragmentCutKind.Edge));
        Assert.Equal(2, cuts.Count(c => c.Kind == FragmentCutKind.End));
        Assert.DoesNotContain(db.GetFemMembers(fragment.Id), m => m.Kind == "wall");

        var meshed = await Mesh(db, fragment, step, root);
        output.WriteLine($"Фрагмент вокруг колонны: узлов {meshed.Mesh!.Nodes.Count}, КЭ {meshed.Mesh.Elements.Count}");

        var extracted = StoreyFragmentMaskService.Extract(db, fragment, FragmentMaskSet.SourceCsfeaLinear, S(parent.Analysis.Id));
        foreach (var d in extracted.Diagnostics) output.WriteLine($"маски {(d.IsError ? "ОШИБКА" : "предупр.")} {d.Code}: {d.Message}");
        Assert.False(extracted.HasErrors);
        Assert.DoesNotContain(extracted.Diagnostics, d => d.Code == "fragment_mask_balance");

        db.LoadAll();
        Restore(db, parent.Sections);
        return db.FemSchemas.Single(s => s.Id == fragment.Id);
    }

    async Task<CalcResult> RunPrism(FemCsfeaRunContext ctx, FemSchema fragment, FragmentBcPreset preset, FragmentBcPreset lateral)
    {
        ctx.Db.SetFragmentBcPreset(fragment, preset.ToString());
        ctx.Db.SetFragmentLateralBcPreset(fragment, lateral.ToString());
        var analysis = LinearByLoadCases(ctx.Db, fragment, $"Призма · {preset}/{lateral}");
        return await FemCsfeaRunner.RunAsync(ctx, fragment, analysis, null, CancellationToken.None);
    }

    /// <summary>
    /// ΣF реакций узлов сетки и ΣF масок по группе разрезов (низ, верх, все боковые) в загружении фрагмента: узлы —
    /// на плоскостях разрезов группы и внутри призмы.
    /// </summary>
    static (PlanarVector3 Reaction, PlanarVector3 Mask) GroupForces(DatabaseService db, FemSchema fragment, RcSecantStepFields fields,
        int loadCaseId, FragmentCutSide side)
    {
        bool On(FemMeshNode n) => side switch
        {
            FragmentCutSide.Bottom => Math.Abs(n.Z - Z1) < Tol,
            FragmentCutSide.Top => Math.Abs(n.Z - Z2) < Tol,
            _ => n.Z > Z1 + Tol && n.Z < Z2 - Tol
                 && ColumnPrism.Faces.Any(f => Math.Abs(f.Eval(new PlanarVector3(n.X, n.Y, n.Z))) < Tol),
        };
        var atCut = db.GetFemMeshNodes(fragment.Id).Where(On).Select(n => int.Parse(n.NodeTag, CultureInfo.InvariantCulture)).ToHashSet();
        var reaction = PlanarVector3.Zero;
        for (int i = 0; i < fields.ReactionNodeIds.Length; i++)
            if (atCut.Contains(fields.ReactionNodeIds[i]))
            {
                int k = RcSecantStepFields.NodeComponents * i;
                reaction += new PlanarVector3(fields.Reactions[k], fields.Reactions[k + 1], fields.Reactions[k + 2]);
            }
        var cuts = db.GetFragmentCutsWithIds(fragment.Id).ToDictionary(c => c.Id, c => c.Cut);
        var mask = PlanarVector3.Zero;
        foreach (var item in db.GetFragmentMasks(fragment.Id)!.Items.Where(x => x.Key == FragmentMaskKey.OfLoadCase(loadCaseId)))
        {
            var cut = cuts[item.CutId];
            if (cut.Side != side) continue;
            mask += cut.Kind == FragmentCutKind.End
                ? item.Samples[0].F!.Force
                : FragmentMaskBalance.Resultant(new FragmentMaskCutInput(cut, null, null), item.Samples, PlanarVector3.Zero)!.Value.F;
        }
        return (reaction, mask);
    }

    /// <summary>Реакции против масок по группам разрезов: ≤ 2 % ΣFz родителя по стадии.</summary>
    void AssertGroupReactions(DatabaseService db, Parent parent, FemSchema fragment, CalcResult result, FemCsfeaResultSummary summary,
        params FragmentCutSide[] sides)
    {
        var cases = db.GetFemLoadCases(fragment.Id);
        for (int i = 0; i < summary.Stages.Count; i++)
        {
            var fields = FemCsfeaStepFieldsCodec.Unpack(db.GetFemResultStep(result.Id, i + 1)!);
            string tag = summary.Stages[i].Tag;
            int lc = cases.Single(c => c.Tag == tag).Id;
            double scale = Math.Abs(parent.ExpectedFz[tag]);
            foreach (var side in sides)
            {
                var (r, m) = GroupForces(db, fragment, fields, lc, side);
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"«{tag}» {side}: реакции ({r.X / 1e3:0.###}, {r.Y / 1e3:0.###}, {r.Z / 1e3:0.###}) кН, " +
                    $"маски ({m.X / 1e3:0.###}, {m.Y / 1e3:0.###}, {m.Z / 1e3:0.###}) кН"));
                Assert.True((r - m).Length <= 0.02 * scale, $"«{tag}» {side}: |R − маска| = {(r - m).Length / 1e3:0.###} кН > 2 % ΣFz родителя");
            }
        }
    }

    /// <summary>Кинематика на всех разрезах (горизонтальных и боковых) ⇒ фрагмент повторяет прогибы родителя в призме.</summary>
    [Fact]
    public async Task Prism_KinematicEverywhere_RepeatsParent()
    {
        await InTempRoot("opencs-fragment-prism", async root =>
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var ctx = new FemCsfeaRunContext(db, GmshSettings(root), new ListLog(output));
            var parent = await CreateParent(ctx, root, PrismStep);
            var fragment = await CreatePrismFragment(db, parent, root, PrismStep);

            var result = await RunPrism(ctx, fragment, FragmentBcPreset.Kinematic, FragmentBcPreset.Kinematic);
            var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
            foreach (var line in summary.Errors.Concat(summary.Report)) output.WriteLine(line);
            Assert.Equal("ok", result.Status);
            foreach (var (tag, scale, diff) in CompareSlab(db, parent, fragment, result, "Призма Kinematic", where: InPrism))
                Assert.True(diff <= 0.02 * scale, $"«{tag}»: расхождение прогиба {diff / scale:P2} > 2 %");
            AssertGroupReactions(db, parent, fragment, result, summary, FragmentCutSide.Bottom, FragmentCutSide.Top, FragmentCutSide.Lateral);
        });
    }

    /// <summary>
    /// Горизонтальные — кинематика, боковые — силы из маски (пресет боковых по умолчанию) ⇒ расчёт идёт, реакции концов
    /// колонны совпадают с масками; прогибы плиты против родителя — с допуском 5 %.
    /// </summary>
    [Fact]
    public async Task Prism_LateralStatic_Equilibrium()
    {
        await InTempRoot("opencs-fragment-prism", async root =>
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var ctx = new FemCsfeaRunContext(db, GmshSettings(root), new ListLog(output));
            var parent = await CreateParent(ctx, root, PrismStep);
            var fragment = await CreatePrismFragment(db, parent, root, PrismStep);

            var result = await RunPrism(ctx, fragment, FragmentBcPreset.Kinematic, FragmentBcPreset.Static);
            var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
            foreach (var line in summary.Errors.Concat(summary.Report)) output.WriteLine(line);
            Assert.Equal("ok", result.Status);
            Assert.DoesNotContain(summary.Report, l => l.Contains("не уравновешена", StringComparison.Ordinal));
            foreach (var (tag, scale, diff) in CompareSlab(db, parent, fragment, result, "Призма Kinematic/Static", where: InPrism))
                Assert.True(diff <= 0.05 * scale, $"«{tag}»: расхождение прогиба {diff / scale:P2} > 5 %");
            AssertGroupReactions(db, parent, fragment, result, summary, FragmentCutSide.Bottom, FragmentCutSide.Top);
        });
    }

    /// <summary>Все разрезы силовые, опор у фрагмента нет ⇒ отказ <c>fragment_bc_all_static</c>.</summary>
    [Fact]
    public async Task Prism_AllStatic_Refused()
    {
        await InTempRoot("opencs-fragment-prism", async root =>
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var ctx = new FemCsfeaRunContext(db, GmshSettings(root), new ListLog(output));
            var parent = await CreateParent(ctx, root, PrismStep);
            var fragment = await CreatePrismFragment(db, parent, root, PrismStep);

            var result = await RunPrism(ctx, fragment, FragmentBcPreset.Static, FragmentBcPreset.Static);
            var summary = FemCsfeaResultSummary.Parse(result.DataJson);
            foreach (var line in summary?.Errors ?? []) output.WriteLine(line);
            Assert.NotEqual("ok", result.Status);
            Assert.Contains(summary!.Errors, l => l.Contains("кинематику хотя бы на одном горизонтальном разрезе", StringComparison.Ordinal));
        });
    }
}
