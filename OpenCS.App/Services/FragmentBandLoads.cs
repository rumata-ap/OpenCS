using System.Globalization;
using CScore.Fem;
using CScore.Fem.Loads;
using CScore.Planar;
using CSfea.CScoreBridge.Structural;

namespace OpenCS.Services;

/// <summary>
/// Нагрузки полосы родителя для сверки равновесия масок (спека 2026-10-28, разд. 4.4): равнодействующая (сила и момент
/// относительно точки) нагрузок, приложенных к части родителя между отметками z₁…z₂, по загружениям — из того же входа
/// CSfea, что считал результат. Нагрузки разносятся по КЭ, чтобы нагрузка КЭ отброшенной части, попавшая согласованной
/// силой в узел на плоскости разреза, не считалась нагрузкой полосы:
/// <list type="bullet">
/// <item>стержни — концевые силы нагрузок КЭ модели (<see cref="RcLoadCase.BeamEnds"/>: нагрузки на КЭ и нагрузки КонЭ);</item>
/// <item>пластины — согласованные силы нагрузок своего КЭ (<see cref="FemLoadCaseNodalForces"/> с приёмником на КЭ);</item>
/// <item>узловые нагрузки (сетки и узлов схемы) — по положению узла, плоскости разрезов входят;</item>
/// <item>КЭ целиком в полосе — точно, вне её — нет, пересекающий плоскость разреза — долей по высоте
/// (равномерная нагрузка) в середине своей части.</item>
/// </list>
/// </summary>
public static class FragmentBandLoads
{
    sealed class Sum
    {
        public PlanarVector3 F;
        public PlanarVector3 M;
        public void Add(PlanarVector3 arm, PlanarVector3 f, PlanarVector3 m)
        {
            F += f;
            M += m + arm.Cross(f);
        }
    }

    /// <summary>Нагрузки полосы по Id загружения входа; <paramref name="adapted"/> — модель адаптера этого входа.</summary>
    public static Dictionary<int, (PlanarVector3 F, PlanarVector3 M)> Compute(FemRcModelInput input, FemRcModelResult adapted,
        double z1, double z2, double tol, PlanarVector3 origin)
    {
        var nodes = new Dictionary<string, PlanarVector3>(StringComparer.Ordinal);
        var meshNodes = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in input.MeshNodes)
            if (meshNodes.TryAdd(n.NodeTag, n)) nodes[n.NodeTag] = new PlanarVector3(n.X, n.Y, n.Z);
        var meshBySource = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var n in input.MeshNodes)
            if (n.SourceNodeTag is { } s) meshBySource.TryAdd(s, n.NodeTag);
        var schemaNodes = input.Nodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
        var beamTags = adapted.Model.Beams.Select(b => b.Id.ToString(CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        var beams = adapted.Model.Beams.ToDictionary(b => b.Id);
        var elements = new Dictionary<string, FemElement>(StringComparer.Ordinal);
        foreach (var e in input.MeshElements) elements.TryAdd(e.ElemTag, e);
        var mesh = new FemLoadMeshContext(input.MeshNodes, input.MeshElements, input.Groups, input.Properties,
            input.Members.GroupBy(m => m.ElemTag).ToDictionary(g => g.Key, g => g.First().RotationDeg, StringComparer.Ordinal));
        bool InBand(double z) => z >= z1 - tol && z <= z2 + tol;

        void Element(Sum s, IReadOnlyList<PlanarVector3> points, IEnumerable<(PlanarVector3 At, double[] V)> forces)
        {
            double zmin = points.Min(p => p.Z), zmax = points.Max(p => p.Z);
            if (zmax <= z1 + tol || zmin >= z2 - tol) return;
            if (zmin >= z1 - tol && zmax <= z2 + tol)
            {
                foreach (var (at, v) in forces) s.Add(at - origin, new(v[0], v[1], v[2]), new(v[3], v[4], v[5]));
                return;
            }
            double lo = Math.Max(zmin, z1), hi = Math.Min(zmax, z2), share = (hi - lo) / (zmax - zmin);
            var f = PlanarVector3.Zero;
            var m = PlanarVector3.Zero;
            foreach (var (_, v) in forces) { f += new PlanarVector3(v[0], v[1], v[2]); m += new PlanarVector3(v[3], v[4], v[5]); }
            var c = points.Aggregate(PlanarVector3.Zero, (a, p) => a + p) * (1.0 / points.Count);
            s.Add(new PlanarVector3(c.X, c.Y, (lo + hi) / 2) - origin, f * share, m * share);
        }

        var result = new Dictionary<int, (PlanarVector3, PlanarVector3)>();
        var caseById = input.LoadCases.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var rc in adapted.Model.LoadCases)
        {
            if (!caseById.TryGetValue(rc.Id, out var lc)) continue;
            var s = new Sum();
            // Пластины — по своим КЭ; стержни модели — ниже, по концевым силам (здесь отбрасываются); прочее — в узлы.
            var perShell = new Dictionary<string, Dictionary<string, double[]>>(StringComparer.Ordinal);
            var dropped = new Dictionary<string, double[]>(StringComparer.Ordinal);
            Dictionary<string, double[]>? Sink(FemElement e)
            {
                if (e.ElemType == "beam") return beamTags.Contains(e.ElemTag) ? dropped : null;
                if (e.ElemType != "shell") return null;
                if (!perShell.TryGetValue(e.ElemTag, out var d)) perShell[e.ElemTag] = d = new(StringComparer.Ordinal);
                return d;
            }
            var resolved = FemLoadCaseNodalForces.Resolve([(lc, 1.0)], input.ElementLoads, input.MeshNodeLoads, mesh, Sink);
            foreach (var f in resolved.Forces)
                if (nodes.TryGetValue(f.NodeTag, out var at) && InBand(at.Z))
                    s.Add(at - origin, new(f.Fx, f.Fy, f.Fz), new(f.Mx, f.My, f.Mz));
            foreach (var l in input.NodeLoads.Where(l => l.LoadCaseId == lc.Id))
                if (schemaNodes.TryGetValue(l.NodeId, out var n) && meshBySource.TryGetValue(n.NodeTag, out var tag)
                    && nodes.TryGetValue(tag, out var at) && InBand(at.Z))
                    s.Add(at - origin, new(l.Fx, l.Fy, l.Fz), new(l.Mx, l.My, l.Mz));
            foreach (var (tag, d) in perShell)
            {
                if (!elements.TryGetValue(tag, out var e) || FemElementGeometry.Of(e, meshNodes) is not { } g)
                    continue;
                Element(s, g.Points, d.Where(kv => nodes.ContainsKey(kv.Key)).Select(kv => (nodes[kv.Key], kv.Value)));
            }
            foreach (var b in rc.BeamEnds)
            {
                if (!beams.TryGetValue(b.BeamId, out var beam)
                    || !nodes.TryGetValue(beam.NodeI.ToString(CultureInfo.InvariantCulture), out var pi)
                    || !nodes.TryGetValue(beam.NodeJ.ToString(CultureInfo.InvariantCulture), out var pj))
                    continue;
                Element(s, [pi, pj], [(pi, b.Forces[..6]), (pj, b.Forces[6..])]);
            }
            result[rc.Id] = (s.F, s.M);
        }
        return result;
    }
}
