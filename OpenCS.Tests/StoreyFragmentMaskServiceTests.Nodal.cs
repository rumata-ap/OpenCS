using System.Globalization;
using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using CScore.Submodel;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
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
