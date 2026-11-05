using System.Globalization;
using CScore.Fem;
using CScore.Fem.Loads;
using CSfea.CScoreBridge.Structural;

namespace OpenCS.Services;

/// <summary>
/// Нагрузки одного загружения родителя, разнесённые по КЭ (спека 2026-11-05, разд. 3): пластины — согласованные силы
/// своего КЭ, стержни модели — концевые силы нагрузок (<see cref="RcLoadCase.BeamEnds"/>), прочее — узловые нагрузки
/// сетки и узлов схемы. Теги — узлы и КЭ сетки родителя; 6 компонент, общие оси.
/// </summary>
public sealed class FragmentParentCaseLoads
{
    public Dictionary<string, Dictionary<string, double[]>> Shells { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, (string NodeI, double[] I, string NodeJ, double[] J)> Beams { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, double[]> Nodes { get; } = new(StringComparer.Ordinal);
}

/// <summary>Разбор нагрузок родителя по КЭ — общий для нагрузок полосы (<see cref="FragmentBandLoads"/>) и узловых сил масок.</summary>
public static class FragmentParentLoadParts
{
    /// <summary>По Id загружения входа; <paramref name="adapted"/> — модель адаптера этого входа.</summary>
    public static Dictionary<int, FragmentParentCaseLoads> Compute(FemRcModelInput input, FemRcModelResult adapted)
    {
        var meshTags = input.MeshNodes.Select(n => n.NodeTag).ToHashSet(StringComparer.Ordinal);
        var meshBySource = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var n in input.MeshNodes)
            if (n.SourceNodeTag is { } s) meshBySource.TryAdd(s, n.NodeTag);
        var schemaNodes = input.Nodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
        var beamTags = adapted.Model.Beams.Select(b => Tag(b.Id)).ToHashSet(StringComparer.Ordinal);
        var beams = adapted.Model.Beams.ToDictionary(b => b.Id);
        var mesh = new FemLoadMeshContext(input.MeshNodes, input.MeshElements, input.Groups, input.Properties,
            input.Members.GroupBy(m => m.ElemTag).ToDictionary(g => g.Key, g => g.First().RotationDeg, StringComparer.Ordinal));
        var caseById = input.LoadCases.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        var result = new Dictionary<int, FragmentParentCaseLoads>();
        foreach (var rc in adapted.Model.LoadCases)
        {
            if (!caseById.TryGetValue(rc.Id, out var lc)) continue;
            var p = new FragmentParentCaseLoads();
            var dropped = new Dictionary<string, double[]>(StringComparer.Ordinal);
            // Пластины — по своим КЭ; стержни модели — по концевым силам ниже (здесь отбрасываются); прочее — в узлы.
            Dictionary<string, double[]>? Sink(FemElement e)
            {
                if (e.ElemType == "beam") return beamTags.Contains(e.ElemTag) ? dropped : null;
                if (e.ElemType != "shell") return null;
                if (!p.Shells.TryGetValue(e.ElemTag, out var d)) p.Shells[e.ElemTag] = d = new(StringComparer.Ordinal);
                return d;
            }
            var resolved = FemLoadCaseNodalForces.Resolve([(lc, 1.0)], input.ElementLoads, input.MeshNodeLoads, mesh, Sink);
            foreach (var f in resolved.Forces)
                if (meshTags.Contains(f.NodeTag)) Add(p.Nodes, f.NodeTag, [f.Fx, f.Fy, f.Fz, f.Mx, f.My, f.Mz]);
            foreach (var l in input.NodeLoads.Where(l => l.LoadCaseId == lc.Id))
                if (schemaNodes.TryGetValue(l.NodeId, out var n) && meshBySource.TryGetValue(n.NodeTag, out var tag) && meshTags.Contains(tag))
                    Add(p.Nodes, tag, [l.Fx, l.Fy, l.Fz, l.Mx, l.My, l.Mz]);
            foreach (var b in rc.BeamEnds)
                if (beams.TryGetValue(b.BeamId, out var beam))
                    p.Beams[Tag(b.BeamId)] = (Tag(beam.NodeI), b.Forces[..6], Tag(beam.NodeJ), b.Forces[6..]);
            result[rc.Id] = p;
        }
        return result;
    }

    static string Tag(int id) => id.ToString(CultureInfo.InvariantCulture);

    static void Add(Dictionary<string, double[]> d, string tag, double[] v)
    {
        if (!d.TryGetValue(tag, out var s)) d[tag] = s = new double[6];
        for (int i = 0; i < 6; i++) s[i] += v[i];
    }
}
