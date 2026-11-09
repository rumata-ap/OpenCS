using System.Globalization;
using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using CScore.Submodel;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Маски кромок пластин из узловых сил КЭ родителя (спека 2026-11-05).</summary>
public sealed partial class StoreyFragmentMaskServiceTests
{
    /// <summary>
    /// Σ по КЭ узла (f_int − f_нагр) − P − R по всем узлам модели без связей: максимум по узлам, доля от макс. |f_int|.
    /// </summary>
    static double NodalResidual(IFragmentParentNodalForces source, FragmentMaskKey key, RcStructuralModel model)
    {
        Worst.Clear();
        var elementsAt = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void At(int node, int elem)
        {
            string n = node.ToString(CultureInfo.InvariantCulture);
            if (!elementsAt.TryGetValue(n, out var l)) elementsAt[n] = l = [];
            l.Add(elem.ToString(CultureInfo.InvariantCulture));
        }
        foreach (var s in model.Shells) foreach (int n in s.NodeIds) At(n, s.Id);
        foreach (var b in model.Beams) { At(b.NodeI, b.Id); At(b.NodeJ, b.Id); }
        var linked = source.NodeLinks.SelectMany(l => l.B == null ? [l.A] : new[] { l.A, l.B }).ToHashSet(StringComparer.Ordinal);
        double scale = 0;
        var residuals = new List<double>();
        foreach (var (n, elems) in elementsAt)
        {
            if (linked.Contains(n)) continue;
            var sum = Dof6.Zero;
            foreach (var e in elems)
            {
                Assert.True(source.TryElementNodeForces(key, e, out var f), $"нет сил КЭ {e}");
                sum += f[n];
                scale = Math.Max(scale, (f[n] + (source.ElementLoads(key, e).GetValueOrDefault(n) ?? Dof6.Zero)).Force.Length);
            }
            sum -= (source.NodeLoads(key).GetValueOrDefault(n) ?? Dof6.Zero) + (source.Reactions(key).GetValueOrDefault(n) ?? Dof6.Zero);
            residuals.Add(sum.Force.Length);
            Worst.Add((n, sum.Force.Length, string.Join(",", elems)));
        }
        return residuals.Count == 0 || scale == 0 ? 0 : residuals.Max() / scale;
    }

    /// <summary>Невязки узлов последнего вызова <see cref="NodalResidual"/> (узел, |ΣF|, КЭ узла) — для сообщения теста.</summary>
    static readonly List<(string N, double R, string E)> Worst = [];
    static string WorstText() => string.Join(" | ", Worst.OrderByDescending(d => d.R).Take(8).Select(d => $"{d.N}:{d.R:0.#}:[{d.E}]"));

    /// <summary>Поля линейного результата по Id загружений родителя (шаг i + 1 — загружение i).</summary>
    static Dictionary<FragmentMaskKey, RcSecantStepFields> LinearSteps(DatabaseService db, FemAnalysis analysis, IReadOnlyList<FemLoadCase> lc)
    {
        var steps = new Dictionary<FragmentMaskKey, RcSecantStepFields>();
        for (int i = 0; i < lc.Count; i++)
            steps[FragmentMaskKey.OfLoadCase(lc[i].Id)] = FemCsfeaStepFieldsCodec.Unpack(db.GetFemResultStep(analysis.ResultId!.Value, i + 1)!);
        return steps;
    }

    static CsfeaFieldsParentResult Fields(DatabaseService db, FemSchema parent, IReadOnlyDictionary<FragmentMaskKey, RcSecantStepFields> steps) =>
        new(steps.Select(kv => (kv.Key, kv.Value)).ToList(),
            db.GetFemMeshNodes(parent.Id).ToDictionary(n => n.NodeTag, StringComparer.Ordinal),
            db.GetFemMeshElements(parent.Id), db.GetFemMembers(parent.Id), null);

    [Fact]
    public void NodalSource_Linear_NodesInEquilibrium()
    {
        var p = Create();
        using var db = p.Db;
        var lc = db.GetFemLoadCases(p.Schema.Id);
        var stages = new[] { Single("Пост.", lc[0].Id), Single("Ветер", lc[1].Id) };
        var analysis = SaveResult(db, p.Schema, FemCsfeaRunner.LinearAnalysisKind, stages, stages);
        var input = FemCsfeaInputBuilder.Build(db, p.Schema.Id, FemCsfeaSetup.FromAnalysis(analysis, lc) with { Stages = [] });
        var adapted = StoreyFragmentMaskService.AdaptAllCases(input);
        Assert.False(adapted.HasErrors, string.Join("; ", adapted.Report));
        var steps = LinearSteps(db, analysis, lc);
        var source = CsfeaNodalForcesSource.Linear(adapted.Model, FemCsfeaLinearSetup.Elastic, Fields(db, p.Schema, steps), steps,
            FragmentParentLoadParts.Compute(input, adapted), id => id);

        foreach (var key in steps.Keys)
        {
            double r = NodalResidual(source, key, adapted.Model);
            Assert.True(r <= 1e-6, $"{key}: невязка {r}; {WorstText()}");
        }
    }

    /// <summary>Жёсткости и K_σ секущего источника второго порядка (в памяти): узлы в равновесии с теми же (K + K_σ).</summary>
    [Fact]
    public void NodalSource_StiffnessSourcePDelta_NodesInEquilibrium()
    {
        var p = Create();
        using var db = p.Db;
        var lc = db.GetFemLoadCases(p.Schema.Id);
        var input = FemCsfeaInputBuilder.Build(db, p.Schema.Id, new FemCsfeaSetup { GrossBars = true, Stages = [Single("Пост.", lc[0].Id)] });
        var adapted = FemRcModelAdapter.Adapt(input);
        Assert.False(adapted.HasErrors, string.Join("; ", adapted.Report));
        var src = RcSecantAnalysis.Run(adapted.Model, new RcSecantOptions
        {
            PhysicallyLinear = true, Solver = new CSfea.Core.SecantPicardOptions { Geometric = true, GeometricSecondOrder = true },
        });
        var record = src.LastConvergedFields()!;
        Assert.True(record.HasMatrices);
        var setup = new FemCsfeaLinearSetup(null, new RecordedRcSectionFactory(record, 0.01), record, true, 0.01);
        var all = StoreyFragmentMaskService.AdaptAllCases(input);
        // Стадии all.Model пусты — каждое загружение решается своей стадией (как в линейном расчёте).
        foreach (var c in all.Model.LoadCases) all.Model.Stages.Add(new RcStage(c.Id.ToString(CultureInfo.InvariantCulture), [(c.Id, 1.0)], 1));
        var run = RcLinearAnalysis.Run(all.Model, new RcLinearOptions
        {
            ShellForceAngles = all.ShellForceAngles, Sections = new RecordedRcSectionFactory(record, 0.01),
            GeometricFields = record, ShellInPlanePDelta = true,
        });
        var steps = new Dictionary<FragmentMaskKey, RcSecantStepFields>();
        for (int i = 0; i < run.Cases.Count; i++) steps[FragmentMaskKey.OfLoadCase(all.Model.LoadCases[i].Id)] = run.Cases[i].Fields;
        var source = CsfeaNodalForcesSource.Linear(all.Model, setup, Fields(db, p.Schema, steps), steps,
            FragmentParentLoadParts.Compute(input, all), id => id);

        foreach (var key in steps.Keys)
        {
            double r = NodalResidual(source, key, all.Model);
            Assert.True(r <= 1e-6, $"{key}: невязка {r}; {WorstText()}");
        }
    }
}

public sealed partial class StoreyFragmentMaskServiceTests
{
    /// <summary>
    /// Призма вокруг C1: линейный CSfea → силы кромок плиты по узловым силам КЭ, равновесие масок с нагрузками
    /// фрагмента до 1e-5; полуразмер 0,9 — грани поперёк КЭ (поправка на полоску).
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(0.9)]
    public void Extract_Linear_Prism_NodalForces_BalanceExact(double half)
    {
        var p = Create(Band with { Prism = CScore.Fem.Fragment.FragmentPrism.Create(-2.5, -2.5, 1, 0, half, half) });
        using var db = p.Db;
        var lc = db.GetFemLoadCases(p.Schema.Id);
        var stages = new[] { Single("Пост.", lc[0].Id), Single("Ветер", lc[1].Id) };
        var analysis = SaveResult(db, p.Schema, FemCsfeaRunner.LinearAnalysisKind, stages, stages);

        var r = StoreyFragmentMaskService.Extract(db, p.Fragment, FragmentMaskSet.SourceCsfeaLinear, S(analysis.Id));

        Assert.False(r.HasErrors, string.Join("\n", r.Diagnostics.Select(d => d.Message)));
        Assert.DoesNotContain(r.Diagnostics, d => d.Code is "fragment_mask_nodal_selfcheck" or "fragment_mask_nodal_unsupported"
            or "fragment_mask_nodal_unassigned" or "fragment_mask_cut_resultant" or "fragment_mask_balance");
        Assert.Contains(r.Diagnostics, d => d.Code == "fragment_mask_summary" && d.Message.Contains("узловые силы КЭ"));
        Assert.True(r.BalanceResidual is <= 1e-5, $"невязка {r.BalanceResidual}; " + string.Join("\n", r.Diagnostics.Select(d => d.Message)));
    }
}

public sealed partial class StoreyFragmentMaskServiceTests
{
    /// <summary>
    /// Секущий расчёт без геом. нелинейности: K_e пластин и стержней — записанные секущие матрицы шага ключа, на которых
    /// решено u, — узлы в равновесии точно (не до допуска итераций).
    /// </summary>
    [Fact]
    public void NodalSource_Secant_NodesInEquilibrium()
    {
        var p = Create();
        using var db = p.Db;
        var lc = db.GetFemLoadCases(p.Schema.Id);
        var input = FemCsfeaInputBuilder.Build(db, p.Schema.Id, new FemCsfeaSetup { Stages = [Single("Пост.", lc[0].Id)] });
        var adapted = FemRcModelAdapter.Adapt(input);
        Assert.False(adapted.HasErrors, string.Join("; ", adapted.Report));
        var run = RcSecantAnalysis.Run(adapted.Model, FemCsfeaSetup.SecantOptions(new FemCsfeaParams()));
        var record = run.LastConvergedFields()!;
        Assert.True(record.HasElementMatrices);
        var key = FragmentMaskKey.OfStageTerms([(lc[0].Id, 1.0)]);
        var steps = new Dictionary<FragmentMaskKey, RcSecantStepFields> { [key] = record };
        var all = StoreyFragmentMaskService.AdaptAllCases(input);
        var source = CsfeaNodalForcesSource.Secant(all.Model, Fields(db, p.Schema, steps), steps,
            FragmentParentLoadParts.Compute(input, all), id => id);

        double r = NodalResidual(source, key, all.Model);
        Assert.True(r <= 1e-6, $"невязка {r}; {WorstText()}");
    }

    /// <summary>
    /// Секущий расчёт второго порядка (спека 2026-11-06, срез 2): узловые силы (K_s + K_σ(u))·u − f_нагр — узлы в
    /// равновесии до допуска итераций (K_σ решателя — по перемещениям предыдущей итерации); без K_σ невязка — член P-Δ.
    /// </summary>
    [Fact]
    public void NodalSource_SecantSecondOrder_NodesInEquilibrium()
    {
        var p = Create();
        using var db = p.Db;
        var lc = db.GetFemLoadCases(p.Schema.Id);
        var input = FemCsfeaInputBuilder.Build(db, p.Schema.Id, new FemCsfeaSetup { Stages = [Single("Пост.", lc[0].Id), Sum("Пост.+Ветер", lc[0].Id, lc[1].Id)] });
        var adapted = FemRcModelAdapter.Adapt(input);
        Assert.False(adapted.HasErrors, string.Join("; ", adapted.Report));
        // Упругий материал: на «Ветре» физически нелинейная модель фикстуры не сходится и без второго порядка; для узловых
        // сил важна только согласованность (K_s + K_σ)·u с решением, секущие матрицы пишутся и в упругом прогоне.
        var csfea = new FemCsfeaParams { GeomNonlinear = true, PhysicalNonlinearity = false, TolDisplacement = 1e-9 };
        var run = RcSecantAnalysis.Run(adapted.Model, FemCsfeaSetup.SecantOptions(csfea));
        Assert.True(run.Result.Completed, run.Result.Message);
        var record = run.LastConvergedFields()!;
        Assert.True(record.HasElementMatrices);
        // Ключ — накопленные стадии: «Пост.» + «Пост.+Ветер» (как у FragmentMaskSourceKeys).
        var key = FragmentMaskKey.OfStageTerms([(lc[0].Id, 2.0), (lc[1].Id, 1.0)]);
        var steps = new Dictionary<FragmentMaskKey, RcSecantStepFields> { [key] = record };
        var all = StoreyFragmentMaskService.AdaptAllCases(input);
        var loads = FragmentParentLoadParts.Compute(input, all);
        var fields = Fields(db, p.Schema, steps);

        double r = NodalResidual(CsfeaNodalForcesSource.Secant(all.Model, fields, steps, loads, id => id, secondOrder: true), key, all.Model);
        string worst = WorstText();
        double first = NodalResidual(CsfeaNodalForcesSource.Secant(all.Model, fields, steps, loads, id => id), key, all.Model);
        // Факт 08.11: 7,3·10⁻¹⁴ против 6,8·10⁻⁴ без K_σ.
        Assert.True(r <= 1e-6, $"невязка {r}; {worst}");
        Assert.True(first > 100 * r, $"без K_σ невязка {first} не больше второго порядка {r} — P-Δ в модели мал, тест не различает");
    }

    /// <summary>
    /// Секущий расчёт второго порядка с несовершенством (крен колонн 1/200, спека 2026-11-09, срез 1): узловые силы
    /// (K_s + K_σ)·u + K_σ·u₀ − f_нагр — узлы в равновесии до допуска итераций; без K_σ·u₀ невязка — член несовершенства.
    /// </summary>
    [Fact]
    public void NodalSource_SecantImperfection_NodesInEquilibrium()
    {
        var p = Create();
        using var db = p.Db;
        var lc = db.GetFemLoadCases(p.Schema.Id);
        var input = FemCsfeaInputBuilder.Build(db, p.Schema.Id, new FemCsfeaSetup { Stages = [Single("Пост.", lc[0].Id)] });
        var adapted = FemRcModelAdapter.Adapt(input);
        Assert.False(adapted.HasErrors, string.Join("; ", adapted.Report));
        var sway = new RcSway(1.0 / 200, [1.0, 0.0, 0.0]);
        adapted.Model.Imperfections.Add(sway);
        var csfea = new FemCsfeaParams { GeomNonlinear = true, PhysicalNonlinearity = false, TolDisplacement = 1e-9 };
        var run = RcSecantAnalysis.Run(adapted.Model, FemCsfeaSetup.SecantOptions(csfea));
        Assert.True(run.Result.Completed, run.Result.Message);
        var record = run.LastConvergedFields()!;
        Assert.True(record.HasElementMatrices);
        var key = FragmentMaskKey.OfStageTerms([(lc[0].Id, 1.0)]);
        var steps = new Dictionary<FragmentMaskKey, RcSecantStepFields> { [key] = record };
        var fields = Fields(db, p.Schema, steps);

        var plain = StoreyFragmentMaskService.AdaptAllCases(input);
        var loads = FragmentParentLoadParts.Compute(input, plain);
        double first = NodalResidual(CsfeaNodalForcesSource.Secant(plain.Model, fields, steps, loads, id => id, secondOrder: true), key, plain.Model);
        var all = StoreyFragmentMaskService.AdaptAllCases(input);
        all.Model.Imperfections.Add(sway);
        double r = NodalResidual(CsfeaNodalForcesSource.Secant(all.Model, fields, steps, loads, id => id, secondOrder: true), key, all.Model);
        string worst = WorstText();
        Assert.True(r <= 1e-6, $"невязка {r}; {worst}");
        Assert.True(first > 100 * r, $"без K_σ·u₀ невязка {first} не больше {r} — несовершенство в модели мало, тест не различает");
    }

    /// <summary>
    /// Секущий источник второго порядка, у которого узловые силы — не (K_s + K_σ)·u: «Большие перемещения», «только P-δ»
    /// — МНК с причиной (до проверки записанных матриц). Обычный второй порядок без матриц КЭ в записи — причина «нет
    /// секущих матриц» (геомнелин больше не отказ сам по себе), с несовершенствами — та же (их проверка — после матриц).
    /// </summary>
    [Theory]
    [InlineData("cr", "«Большими перемещениями»")]
    [InlineData("local", "«Только P-δ»")]
    [InlineData("imperfection", "нет секущих матриц")]
    [InlineData("plain", "нет секущих матриц")]
    public void Extract_SecantSecondOrder_NodalFallbackReasons(string mode, string reason)
    {
        var p = Create();
        using var db = p.Db;
        var lc = db.GetFemLoadCases(p.Schema.Id);
        var stages = new[] { Single("Пост.", lc[0].Id) };
        var analysis = SaveResult(db, p.Schema, FemCsfeaRunner.AnalysisKind, stages, stages);
        var csfea = new FemCsfeaParams
        {
            GeomNonlinear = true, GeomLargeDisplacements = mode == "cr",
            SecondOrderScope = mode == "local" ? FemCsfeaSecondOrderScope.LocalOnly : FemCsfeaSecondOrderScope.Full,
        };
        analysis.ParamsJson = new FemAnalysisParams { Stages = [.. stages], Csfea = csfea }.ToJson();
        db.SaveFemAnalysis(analysis);
        if (mode == "imperfection")
        {
            var result = db.GetCalcResultById(analysis.ResultId!.Value)!;
            var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
            summary.ImperfectionTags = "Погибь";
            result.DataJson = summary.ToJson();
            db.SaveCalcResult(result);
        }

        var r = StoreyFragmentMaskService.Extract(db, p.Fragment, FragmentMaskSet.SourceCsfeaSecant, S(analysis.Id));

        Assert.False(r.HasErrors, string.Join("\n", r.Diagnostics.Select(d => d.Message)));
        var nodal = Assert.Single(r.Diagnostics, d => d.Code == "fragment_mask_nodal_unsupported");
        Assert.Contains(reason, nodal.Message, StringComparison.Ordinal);
    }
}
