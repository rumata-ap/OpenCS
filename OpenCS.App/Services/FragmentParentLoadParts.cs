using System.Globalization;
using CScore.Fem;
using CScore.Fem.Loads;
using CScore.Planar;
using CSfea.CScoreBridge.Structural;

namespace OpenCS.Services;

/// <summary>
/// Нагрузки одного загружения родителя, разнесённые по КЭ (спека 2026-11-05, разд. 3): пластины — согласованные силы
/// своего КЭ, стержни модели — концевые силы нагрузок (<see cref="RcLoadCase.BeamEnds"/>), прочее — узловые нагрузки
/// сетки и узлов схемы (в т. ч. сосредоточенные нагрузки КонЭ в узле сетки). Теги — узлы и КЭ сетки родителя;
/// 6 компонент, общие оси. <see cref="Pieces"/> — те же нагрузки КЭ (пластин и стержней модели) частями по виду: для
/// части Ω_f КЭ, пересекающего грань, правилом построителя (спека 2026-11-05 «нагрузки КЭ на гранях по виду»).
/// </summary>
public sealed class FragmentParentCaseLoads
{
    public Dictionary<string, Dictionary<string, double[]>> Shells { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, (string NodeI, double[] I, string NodeJ, double[] J)> Beams { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, double[]> Nodes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<FemLoadPiece>> Pieces { get; } = new(StringComparer.Ordinal);

    internal void AddPieces(string elemTag, IEnumerable<FemLoadPiece> pieces)
    {
        if (!Pieces.TryGetValue(elemTag, out var list)) Pieces[elemTag] = list = [];
        list.AddRange(pieces);
    }
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
            bool Owned(FemElement e) => e.ElemType == "shell" || e.ElemType == "beam" && beamTags.Contains(e.ElemTag);
            if (lc.SelfWeightFactor is { } k && k != 0)
                foreach (var e in input.MeshElements.Where(Owned)) p.AddPieces(e.ElemTag, FemElementLoadPieces.SelfWeight(k, e, mesh));
            foreach (var load in input.ElementLoads.Where(l => l.LoadCaseId == lc.Id))
                foreach (var e in FemLoadTargets.Resolve(load, mesh, []))
                    if (Owned(e)) p.AddPieces(e.ElemTag, FemElementLoadPieces.Of(load, e, mesh, 1));
            MemberLoads(input, mesh, beamTags, input.MemberLoads.Where(l => l.LoadCaseId == lc.Id).ToList(), p);
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

    /// <summary>
    /// Нагрузки КонЭ-стержней по КЭ сетки (как <c>FemRcModelAdapter.AddMemberLoads</c>): участки и сосредоточенные внутри
    /// КЭ — частями по виду, сосредоточенные в узле сетки — узловыми нагрузками. Местные оси КонЭ не определены — пропуск
    /// (адаптер о них предупреждает).
    /// </summary>
    static void MemberLoads(FemRcModelInput input, FemLoadMeshContext mesh, HashSet<string> beamTags, List<FemMemberLoad> loads,
        FragmentParentCaseLoads p)
    {
        if (loads.Count == 0) return;
        var seg = FemMemberLoadSegmenter.Segment(input.MeshNodes, input.MeshElements, input.Nodes, input.Members, loads);
        var members = input.Members.GroupBy(m => m.ElemTag).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var nodeByTag = new Dictionary<string, FemNode>(StringComparer.Ordinal);
        foreach (var n in input.Nodes) nodeByTag.TryAdd(n.NodeTag, n);
        var frames = new Dictionary<string, (PlanarVector3 X, PlanarVector3 Y, PlanarVector3 Z)?>(StringComparer.Ordinal);
        PlanarVector3? Global(PlanarVector3 v, string cs, string memberTag)
        {
            if (!cs.Equals("local", StringComparison.OrdinalIgnoreCase)) return v;
            if (!frames.TryGetValue(memberTag, out var f))
            {
                f = null;
                if (members.TryGetValue(memberTag, out var m) && m.Node1 is { } a && m.Node2 is { } b && m.Node3 == null
                    && nodeByTag.TryGetValue(a.ToString(CultureInfo.InvariantCulture), out var na)
                    && nodeByTag.TryGetValue(b.ToString(CultureInfo.InvariantCulture), out var nb))
                    try { f = BeamLocalAxisConvention.Frame(new(na.X, na.Y, na.Z), new(nb.X, nb.Y, nb.Z), m.RotationDeg); }
                    catch (InvalidOperationException) { }
                frames[memberTag] = f;
            }
            return f is { } x ? x.X * v.X + x.Y * v.Y + x.Z * v.Z : null;
        }
        FemElementGeometry? Bar(string tag) =>
            beamTags.Contains(tag) && mesh.ElementsByTag.TryGetValue(tag, out var e) && mesh.Geometry(e) is { IsBar: true } g ? g : null;

        foreach (var d in seg.Distributed)
        {
            if (Bar(d.MeshElementTag) is not { } g || Global(d.QAtA, d.CoordinateSystem, d.MemberTag) is not { } qa
                || Global(d.QAtB, d.CoordinateSystem, d.MemberTag) is not { } qb) continue;
            var u = g.Points[1] - g.Points[0];
            p.AddPieces(d.MeshElementTag, [new FemLinePiece(g.Points[0] + u * d.AOverL, g.Points[0] + u * d.BOverL, qa, qb)]);
        }
        foreach (var d in seg.InElements)
        {
            if (Bar(d.MeshElementTag) is not { } g || Global(d.Force, d.CoordinateSystem, d.MemberTag) is not { } f) continue;
            p.AddPieces(d.MeshElementTag, [new FemPointPiece(g.Points[0] + (g.Points[1] - g.Points[0]) * d.XOverL, f, PlanarVector3.Zero)]);
        }
        foreach (var d in seg.OnNodes)
        {
            if (!mesh.NodesByTag.ContainsKey(d.MeshNodeTag) || Global(d.Force, d.CoordinateSystem, d.MemberTag) is not { } f
                || Global(d.Moment, d.CoordinateSystem, d.MemberTag) is not { } m) continue;
            Add(p.Nodes, d.MeshNodeTag, [f.X, f.Y, f.Z, m.X, m.Y, m.Z]);
        }
    }

    static string Tag(int id) => id.ToString(CultureInfo.InvariantCulture);

    static void Add(Dictionary<string, double[]> d, string tag, double[] v)
    {
        if (!d.TryGetValue(tag, out var s)) d[tag] = s = new double[6];
        for (int i = 0; i < 6; i++) s[i] += v[i];
    }
}
