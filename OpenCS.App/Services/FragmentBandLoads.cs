using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Loads;
using CScore.Planar;
using CSfea.CScoreBridge.Structural;

namespace OpenCS.Services;

/// <summary>
/// Нагрузки полосы родителя для сверки равновесия масок (спека 2026-10-28, разд. 4.4): равнодействующая (сила и момент
/// относительно точки) нагрузок, приложенных к части родителя между отметками z₁…z₂, по загружениям — из того же входа
/// CSfea, что считал результат. Нагрузки разносятся по КЭ (<see cref="FragmentParentLoadParts"/>), чтобы нагрузка КЭ
/// отброшенной части, попавшая согласованной силой в узел на плоскости разреза, не считалась нагрузкой полосы:
/// <list type="bullet">
/// <item>стержни — концевые силы нагрузок КЭ модели (<see cref="RcLoadCase.BeamEnds"/>: нагрузки на КЭ и нагрузки КонЭ);</item>
/// <item>пластины — согласованные силы нагрузок своего КЭ (<see cref="FemLoadCaseNodalForces"/> с приёмником на КЭ);</item>
/// <item>узловые нагрузки (сетки и узлов схемы) — по положению узла, плоскости разрезов входят;</item>
/// <item>КЭ целиком во фрагменте — точно, вне его — нет, пересекающий плоскость разреза (полосы или грани призмы) —
/// частью нагрузки внутри по виду (<see cref="FragmentLoadInside"/>: сосредоточенная — по точке, погонная — обрезком,
/// по площади — интегралом по части КЭ), как переносит построитель; без частей — долей длины или площади
/// (<see cref="FragmentElementShare"/>) в центре части.</item>
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

    /// <summary>Нагрузки полосы z₁…z₂ по Id загружения входа; <paramref name="adapted"/> — модель адаптера этого входа.</summary>
    public static Dictionary<int, (PlanarVector3 F, PlanarVector3 M)> Compute(FemRcModelInput input, FemRcModelResult adapted,
        double z1, double z2, double tol, PlanarVector3 origin) =>
        Compute(input, adapted, new FragmentBounds(z1, z2, null, tol), origin);

    /// <summary>Нагрузки части родителя внутри <paramref name="bounds"/> (полоса и призма) по Id загружения входа.</summary>
    public static Dictionary<int, (PlanarVector3 F, PlanarVector3 M)> Compute(FemRcModelInput input, FemRcModelResult adapted,
        FragmentBounds bounds, PlanarVector3 origin) =>
        Compute(input, FragmentParentLoadParts.Compute(input, adapted), bounds, origin);

    /// <summary>Нагрузки части родителя внутри <paramref name="bounds"/> по готовому разбору <paramref name="parts"/>.</summary>
    public static Dictionary<int, (PlanarVector3 F, PlanarVector3 M)> Compute(FemRcModelInput input,
        IReadOnlyDictionary<int, FragmentParentCaseLoads> parts, FragmentBounds bounds, PlanarVector3 origin)
    {
        var nodes = new Dictionary<string, PlanarVector3>(StringComparer.Ordinal);
        var meshNodes = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in input.MeshNodes)
            if (meshNodes.TryAdd(n.NodeTag, n)) nodes[n.NodeTag] = new PlanarVector3(n.X, n.Y, n.Z);
        var elements = new Dictionary<string, FemElement>(StringComparer.Ordinal);
        foreach (var e in input.MeshElements) elements.TryAdd(e.ElemTag, e);

        // points — концы стержня (2) или контур пластины.
        void Element(Sum s, IReadOnlyList<PlanarVector3> points, IEnumerable<(PlanarVector3 At, double[] V)> forces,
            IReadOnlyList<FemLoadPiece>? pieces)
        {
            if (FragmentElementShare.Of(bounds, points) is not { } part) return;
            if (part.Whole)
            {
                foreach (var (at, v) in forces) s.Add(at - origin, new(v[0], v[1], v[2]), new(v[3], v[4], v[5]));
                return;
            }
            if (pieces is { Count: > 0 })
            {
                foreach (var t in FragmentLoadInside.Terms(bounds, pieces)) s.Add(t.At - origin, t.F, t.M);
                return;
            }
            var f = PlanarVector3.Zero;
            var m = PlanarVector3.Zero;
            foreach (var (_, v) in forces) { f += new PlanarVector3(v[0], v[1], v[2]); m += new PlanarVector3(v[3], v[4], v[5]); }
            s.Add(part.Centroid - origin, f * part.Share, m * part.Share);
        }

        var result = new Dictionary<int, (PlanarVector3, PlanarVector3)>();
        foreach (var (id, p) in parts)
        {
            var s = new Sum();
            foreach (var (tag, v) in p.Nodes)
                if (nodes.TryGetValue(tag, out var at) && bounds.InBox(at))
                    s.Add(at - origin, new(v[0], v[1], v[2]), new(v[3], v[4], v[5]));
            foreach (var (tag, d) in p.Shells)
            {
                if (!elements.TryGetValue(tag, out var e) || FemElementGeometry.Of(e, meshNodes) is not { } g) continue;
                Element(s, g.Contour.Select(i => g.Points[i]).ToList(),
                    d.Where(kv => nodes.ContainsKey(kv.Key)).Select(kv => (nodes[kv.Key], kv.Value)), p.Pieces.GetValueOrDefault(tag));
            }
            foreach (var (tag, b) in p.Beams)
            {
                if (!nodes.TryGetValue(b.NodeI, out var pi) || !nodes.TryGetValue(b.NodeJ, out var pj)) continue;
                Element(s, [pi, pj], [(pi, b.I), (pj, b.J)], p.Pieces.GetValueOrDefault(tag));
            }
            result[id] = (s.F, s.M);
        }
        return result;
    }
}
