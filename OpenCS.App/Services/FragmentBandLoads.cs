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
        IReadOnlyDictionary<int, FragmentParentCaseLoads> parts, FragmentBounds bounds, PlanarVector3 origin) =>
        new Prepared(input, parts).Compute(bounds, origin);

    /// <summary>
    /// Разбор нагрузок, подготовленный для многих границ (узлы продавливания схемы): геометрия и габариты КЭ строятся
    /// один раз, на границы — только КЭ, чьи габариты не лежат целиком снаружи какой-либо плоскости (такие
    /// <see cref="FragmentElementShare.Of"/> всё равно отбросил бы).
    /// </summary>
    public sealed class Prepared
    {
        /// <summary>КЭ с нагрузками: точки (концы стержня или контур пластины), габариты, нагрузки по индексу загружения.</summary>
        sealed class Item(PlanarVector3[] points)
        {
            public readonly PlanarVector3[] Points = points;
            public readonly PlanarVector3 Min = new(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z));
            public readonly PlanarVector3 Max = new(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z));
            public readonly List<(int Case, (PlanarVector3 At, double[] V)[] Forces, List<FemLoadPiece>? Pieces)> Loads = [];
        }

        readonly int[] _ids;
        readonly List<(PlanarVector3 At, List<(int Case, double[] V)> Loads)> _nodes = [];
        readonly List<Item> _items = [];

        public Prepared(FemRcModelInput input, IReadOnlyDictionary<int, FragmentParentCaseLoads> parts)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(parts);
            var nodes = new Dictionary<string, PlanarVector3>(StringComparer.Ordinal);
            var meshNodes = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
            foreach (var n in input.MeshNodes)
                if (meshNodes.TryAdd(n.NodeTag, n)) nodes[n.NodeTag] = new PlanarVector3(n.X, n.Y, n.Z);
            var elements = new Dictionary<string, FemElement>(StringComparer.Ordinal);
            foreach (var e in input.MeshElements) elements.TryAdd(e.ElemTag, e);

            _ids = parts.Keys.ToArray();
            var nodeIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var shells = new Dictionary<string, Item?>(StringComparer.Ordinal);
            var beams = new Dictionary<(string, string, string), Item>();
            for (int c = 0; c < _ids.Length; c++)
            {
                var p = parts[_ids[c]];
                foreach (var (tag, v) in p.Nodes)
                {
                    if (!nodes.TryGetValue(tag, out var at)) continue;
                    if (!nodeIndex.TryGetValue(tag, out int k))
                    {
                        nodeIndex[tag] = k = _nodes.Count;
                        _nodes.Add((at, []));
                    }
                    _nodes[k].Loads.Add((c, v));
                }
                foreach (var (tag, d) in p.Shells)
                {
                    if (!shells.TryGetValue(tag, out var item))
                    {
                        item = elements.TryGetValue(tag, out var e) && FemElementGeometry.Of(e, meshNodes) is { } g
                            ? new Item(g.Contour.Select(i => g.Points[i]).ToArray())
                            : null;
                        shells[tag] = item;
                        if (item != null) _items.Add(item);
                    }
                    item?.Loads.Add((c, d.Where(kv => nodes.ContainsKey(kv.Key)).Select(kv => (nodes[kv.Key], kv.Value)).ToArray(),
                        p.Pieces.GetValueOrDefault(tag)));
                }
                foreach (var (tag, b) in p.Beams)
                {
                    if (!nodes.TryGetValue(b.NodeI, out var pi) || !nodes.TryGetValue(b.NodeJ, out var pj)) continue;
                    if (!beams.TryGetValue((tag, b.NodeI, b.NodeJ), out var item))
                    {
                        beams[(tag, b.NodeI, b.NodeJ)] = item = new Item([pi, pj]);
                        _items.Add(item);
                    }
                    item.Loads.Add((c, [(pi, b.I), (pj, b.J)], p.Pieces.GetValueOrDefault(tag)));
                }
            }
        }

        /// <summary>Габариты целиком снаружи (или на) какой-либо плоскости границ — КЭ вне фрагмента.</summary>
        static bool Outside(FragmentBounds bounds, PlanarVector3 min, PlanarVector3 max)
        {
            foreach (var pl in bounds.Planes)
            {
                var n = pl.Normal;
                double lowest = (n.X > 0 ? n.X * min.X : n.X * max.X) + (n.Y > 0 ? n.Y * min.Y : n.Y * max.Y)
                    + (n.Z > 0 ? n.Z * min.Z : n.Z * max.Z) - pl.Offset;
                if (lowest >= -bounds.Tol) return true;
            }
            return false;
        }

        /// <summary>Нагрузки части родителя внутри <paramref name="bounds"/> по Id загружения входа.</summary>
        public Dictionary<int, (PlanarVector3 F, PlanarVector3 M)> Compute(FragmentBounds bounds, PlanarVector3 origin)
        {
            ArgumentNullException.ThrowIfNull(bounds);
            var sums = new Sum[_ids.Length];
            for (int c = 0; c < sums.Length; c++) sums[c] = new Sum();
            foreach (var (at, loads) in _nodes)
            {
                if (!bounds.InBox(at)) continue;
                foreach (var (c, v) in loads) sums[c].Add(at - origin, new(v[0], v[1], v[2]), new(v[3], v[4], v[5]));
            }
            foreach (var item in _items)
            {
                if (Outside(bounds, item.Min, item.Max) || FragmentElementShare.Of(bounds, item.Points) is not { } part) continue;
                foreach (var (c, forces, pieces) in item.Loads)
                {
                    var s = sums[c];
                    if (part.Whole)
                    {
                        foreach (var (at, v) in forces) s.Add(at - origin, new(v[0], v[1], v[2]), new(v[3], v[4], v[5]));
                        continue;
                    }
                    if (pieces is { Count: > 0 })
                    {
                        foreach (var t in FragmentLoadInside.Terms(bounds, pieces)) s.Add(t.At - origin, t.F, t.M);
                        continue;
                    }
                    var f = PlanarVector3.Zero;
                    var m = PlanarVector3.Zero;
                    foreach (var (_, v) in forces) { f += new PlanarVector3(v[0], v[1], v[2]); m += new PlanarVector3(v[3], v[4], v[5]); }
                    s.Add(part.Centroid - origin, f * part.Share, m * part.Share);
                }
            }
            var result = new Dictionary<int, (PlanarVector3, PlanarVector3)>();
            for (int c = 0; c < _ids.Length; c++) result[_ids[c]] = (sums[c].F, sums[c].M);
            return result;
        }
    }
}
