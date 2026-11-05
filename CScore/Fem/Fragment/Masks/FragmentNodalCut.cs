using System.Globalization;
using CScore.Fem.Loads;
using CScore.Planar;
using CScore.Submodel;

namespace CScore.Fem.Fragment.Masks;

/// <summary>Итог узловых сил кромок: слагаемые по ключу и номеру сечения входа экстрактора; <see cref="Ok"/> = false — путь МНК.</summary>
public sealed class FragmentNodalCutResult
{
    public bool Ok { get; internal set; }
    public Dictionary<FragmentMaskKey, Dictionary<int, List<FragmentNodalTerm>>> Terms { get; } = [];
    public List<FemValidationDiagnostic> Diagnostics { get; } = [];
}

/// <summary>
/// Силы кромок пластин по узловым силам КЭ родителя (спека 2026-11-05, разд. 3): тело — КЭ с центром внутри
/// фрагмента, отброшенная часть — прочие (стержни разрезов «конец стержня» — ни то ни другое); сила отброшенной части в
/// граничном узле <c>S_N = −Σ (f_int,e − f_нагр,e)|_N</c>; поправка на полоску между ступенькой и гранью
/// <c>нагр(Ω_s) − нагр(Ω_f)</c> (КЭ, пересекающий грань, — частью по виду нагрузки <see cref="FragmentLoadInside"/>, без
/// частей у источника — долей <see cref="FragmentElementShare"/>; узловые нагрузки и реакции — по
/// принадлежности узла); слагаемое — в разрез плоскости наибольшего <c>Eval</c> центра КЭ (узла), ближайший к точке.
/// Перед снятием — самопроверка равновесия узлов тела.
/// </summary>
public static class FragmentNodalCut
{
    /// <summary>Допуск самопроверки — доля макс. |f_int| узла по телу (момент — с характерной длиной тела).</summary>
    public const double SelfCheckTolerance = 1e-4;

    /// <summary>Допуск совпадения отрезка разреза с плоскостью границ — во столько раз больше допуска границ.</summary>
    const double FaceToleranceFactor = 10;

    sealed class Body
    {
        public Dictionary<string, FemElementGeometry> Geometry { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, PlanarVector3> Center { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, PlanarVector3> Positions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> ElementsAt { get; } = new(StringComparer.Ordinal);
        public HashSet<string> BodyElements { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Discarded { get; } = new(StringComparer.Ordinal);
        public HashSet<string> BodyNodes { get; } = new(StringComparer.Ordinal);
        public HashSet<string> BoundaryNodes { get; } = new(StringComparer.Ordinal);
        public double Extent { get; private set; }

        /// <summary>
        /// Узел принадлежит телу Ω_s: узел КЭ тела; узел без КЭ (опорный узел АЖТ, узел пружины) — по положению, как в
        /// Ω_f, иначе его реакция и сила связи с телом выпали бы из равновесия.
        /// </summary>
        public bool Holds(string n, IReadOnlyDictionary<string, FemMeshNode> nodes, FragmentBounds bounds) =>
            BodyNodes.Contains(n)
            || !ElementsAt.ContainsKey(n) && nodes.TryGetValue(n, out var m) && bounds.InBox(new PlanarVector3(m.X, m.Y, m.Z));

        public IReadOnlyList<PlanarVector3> Outline(string tag)
        {
            var g = Geometry[tag];
            return g.IsBar ? [g.Points[0], g.Points[1]] : g.Contour.Select(i => g.Points[i]).ToList();
        }

        public static Body Classify(FragmentBounds bounds, IReadOnlyDictionary<string, FemMeshNode> nodes,
            IReadOnlyList<FemElement> elements, IReadOnlySet<string> endCutBars)
        {
            var b = new Body();
            foreach (var e in elements)
            {
                if (FemElementGeometry.Of(e, nodes) is not { } g || !(g.IsShell || g.IsBar) || !b.Geometry.TryAdd(e.ElemTag, g))
                    continue;
                var c = g.IsBar ? (g.Points[0] + g.Points[1]) * 0.5 : g.Centroid;
                b.Center[e.ElemTag] = c;
                for (int k = 0; k < g.NodeTags.Count; k++)
                {
                    b.Positions.TryAdd(g.NodeTags[k], g.Points[k]);
                    if (!b.ElementsAt.TryGetValue(g.NodeTags[k], out var list)) b.ElementsAt[g.NodeTags[k]] = list = [];
                    list.Add(e.ElemTag);
                }
                if (endCutBars.Contains(e.ElemTag)) continue;
                (bounds.InBox(c) ? b.BodyElements : b.Discarded).Add(e.ElemTag);
            }
            foreach (var tag in b.BodyElements) b.BodyNodes.UnionWith(b.Geometry[tag].NodeTags);
            foreach (var tag in b.Discarded)
                foreach (var n in b.Geometry[tag].NodeTags)
                    if (b.BodyNodes.Contains(n)) b.BoundaryNodes.Add(n);
            if (b.BodyNodes.Count > 0)
            {
                var pts = b.BodyNodes.Select(n => b.Positions[n]).ToList();
                var lo = new PlanarVector3(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Min(p => p.Z));
                var hi = new PlanarVector3(pts.Max(p => p.X), pts.Max(p => p.Y), pts.Max(p => p.Z));
                b.Extent = (hi - lo).Length;
            }
            return b;
        }
    }

    /// <summary>Разрезы кромок по плоскостям границ; слагаемое — в разрез плоскости наибольшего <c>Eval</c>, ближайший к точке.</summary>
    sealed class CutIndex
    {
        readonly FragmentBounds _bounds;
        readonly IReadOnlyList<FragmentMaskCutInput> _cuts;
        readonly List<int>[] _byPlane;

        public CutIndex(IReadOnlyList<FragmentMaskCutInput> cuts, FragmentBounds bounds)
        {
            _cuts = cuts;
            _bounds = bounds;
            _byPlane = bounds.Planes.Select(_ => new List<int>()).ToArray();
            for (int i = 0; i < cuts.Count; i++)
                if (cuts[i].Cut.Kind == FragmentCutKind.Edge && cuts[i].Cut.Segments.Count > 0 && PlaneOf(cuts[i].Cut, bounds) is int p)
                    _byPlane[p].Add(i);
        }

        public int? For(PlanarVector3 classify, PlanarVector3 near)
        {
            int best = 0;
            double bestEval = double.NegativeInfinity;
            for (int k = 0; k < _bounds.Planes.Count; k++)
            {
                double v = _bounds.Planes[k].Eval(classify);
                if (v > bestEval) { bestEval = v; best = k; }
            }
            int? cut = null;
            double d = double.MaxValue;
            foreach (int i in _byPlane[best])
            {
                double di = _cuts[i].Cut.Segments.Min(s => SegmentDistance(s.P, s.Q, near));
                if (di < d) { d = di; cut = i; }
            }
            return cut;
        }
    }

    /// <summary>Номер плоскости <see cref="FragmentBounds.Planes"/>, на которой лежит кромка разреза; null — нет такой.</summary>
    public static int? PlaneOf(FragmentCut cut, FragmentBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(cut);
        ArgumentNullException.ThrowIfNull(bounds);
        double tol = FaceToleranceFactor * bounds.Tol;
        for (int k = 0; k < bounds.Planes.Count; k++)
        {
            var pl = bounds.Planes[k];
            if (pl.Side != cut.Side) continue;
            if (pl.Side == FragmentCutSide.Lateral && pl.Normal.Dot(cut.Outward) < 1 - 1e-6) continue;
            if (cut.Segments.All(s => Math.Abs(pl.Eval(s.P)) <= tol && Math.Abs(pl.Eval(s.Q)) <= tol)) return k;
        }
        return null;
    }

    static double SegmentDistance(PlanarVector3 a, PlanarVector3 b, PlanarVector3 p)
    {
        var d = b - a;
        double l2 = d.Dot(d);
        double t = l2 > 0 ? Math.Clamp((p - a).Dot(d) / l2, 0, 1) : 0;
        return (a + d * t - p).Length;
    }

    /// <summary>
    /// КЭ стержней, через которые проходят разрезы «конец стержня» (точка разреза на отрезке КЭ в допуске): оба КЭ у
    /// разреза в узле. Их силы — маски концов стержней, в ступеньку кромок они не входят.
    /// </summary>
    public static HashSet<string> EndCutBars(IReadOnlyList<FragmentMaskCutInput> cuts, IReadOnlyDictionary<string, FemMeshNode> nodes,
        IReadOnlyList<FemElement> elements, double tol)
    {
        var points = cuts.Where(c => c.Cut.Kind == FragmentCutKind.End).Select(c => c.EndPoint).OfType<PlanarVector3>().ToList();
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (points.Count == 0) return result;
        foreach (var e in elements)
            if (FemElementGeometry.Of(e, nodes) is { IsBar: true } g && points.Any(p => SegmentDistance(g.Points[0], g.Points[1], p) <= tol))
                result.Add(e.ElemTag);
        return result;
    }

    /// <summary>
    /// Слагаемые сил кромок по ключам <paramref name="keys"/>: самопроверка по всем ключам (отказ — <see cref="FragmentNodalCutResult.Ok"/>
    /// = false и предупреждение), затем S, поправка на полоску и разнесение по разрезам.
    /// </summary>
    public static FragmentNodalCutResult Compute(IReadOnlyList<FragmentMaskCutInput> cuts, FragmentBounds bounds,
        IReadOnlyDictionary<string, FemMeshNode> nodes, IReadOnlyList<FemElement> elements, IReadOnlySet<string> endCutBars,
        IReadOnlyList<FragmentMaskKey> keys, IFragmentParentNodalForces source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var r = new FragmentNodalCutResult();
        var body = Body.Classify(bounds, nodes, elements, endCutBars);
        var linked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (a, b) in source.NodeLinks)
        {
            linked.Add(a);
            if (b != null) linked.Add(b);
        }
        WarnLinks(r, body, nodes, bounds, source.NodeLinks);

        var cache = new Dictionary<(FragmentMaskKey, string), IReadOnlyDictionary<string, Dof6>?>();
        IReadOnlyDictionary<string, Dof6>? Forces(FragmentMaskKey key, string tag)
        {
            if (!cache.TryGetValue((key, tag), out var f))
                cache[(key, tag)] = f = source.TryElementNodeForces(key, tag, out var v) ? v : null;
            return f;
        }

        foreach (var key in keys)
            if (SelfCheck(body, bounds, key, source, linked, Forces) is { } fail)
            {
                r.Diagnostics.Add(new FemValidationDiagnostic("fragment_mask_nodal_selfcheck",
                    fail + " — силы кромок пластин сняты МНК по центрам КЭ.", false));
                return r;
            }

        var index = new CutIndex(cuts, bounds);
        foreach (var key in keys)
        {
            var map = new Dictionary<int, List<FragmentNodalTerm>>();
            var lost = PlanarVector3.Zero;
            double scale = 0;
            void Add(PlanarVector3 classify, PlanarVector3 at, PlanarVector3 f, PlanarVector3 m)
            {
                scale = Math.Max(scale, f.Length);
                if (index.For(classify, at) is not int ci)
                {
                    lost += f;
                    return;
                }
                if (!map.TryGetValue(ci, out var list)) map[ci] = list = [];
                list.Add(new FragmentNodalTerm(at, f, m));
            }

            // S: действие отброшенной части в узлах ступеньки.
            foreach (var n in body.BoundaryNodes)
            {
                var at = body.Positions[n];
                foreach (var e in body.ElementsAt[n])
                    if (body.Discarded.Contains(e) && Forces(key, e)?.GetValueOrDefault(n) is { } f)
                        Add(body.Center[e], at, f.Force * -1, f.Moment * -1);
            }

            // Поправка на полоску: нагрузки КЭ — Ω_s (тело, в своих узлах) минус Ω_f (часть внутри границ по виду нагрузки,
            // без частей у источника — доля).
            foreach (var (tag, _) in body.Geometry)
            {
                if (endCutBars.Contains(tag)) continue;
                var loads = source.ElementLoads(key, tag);
                if (loads.Count == 0) continue;
                bool inBody = body.BodyElements.Contains(tag);
                var part = FragmentElementShare.Of(bounds, body.Outline(tag));
                if (inBody && part is { Whole: true }) continue;
                var c = body.Center[tag];
                if (inBody)
                    foreach (var (n, l) in loads)
                        if (body.Positions.TryGetValue(n, out var at)) Add(c, at, l.Force, l.Moment);
                if (part is not { } p) continue;
                if (p.Whole)
                {
                    foreach (var (n, l) in loads)
                        if (body.Positions.TryGetValue(n, out var at)) Add(c, at, l.Force * -1, l.Moment * -1);
                    continue;
                }
                var pieces = source.ElementLoadPieces(key, tag);
                if (pieces.Count > 0)
                {
                    foreach (var x in FragmentLoadInside.Terms(bounds, pieces)) Add(c, x.At, x.F * -1, x.M * -1);
                    continue;
                }
                var sf = PlanarVector3.Zero;
                var sm = PlanarVector3.Zero;
                foreach (var l in loads.Values) { sf += l.Force; sm += l.Moment; }
                Add(c, p.Centroid, sf * -p.Share, sm * -p.Share);
            }

            // Узловые нагрузки и реакции: (χ_s − χ_f)·P в узле.
            void Point(IReadOnlyDictionary<string, Dof6> values)
            {
                foreach (var (n, v) in values)
                {
                    if (!nodes.TryGetValue(n, out var mn)) continue;
                    var at = new PlanarVector3(mn.X, mn.Y, mn.Z);
                    int k = (body.Holds(n, nodes, bounds) ? 1 : 0) - (bounds.InBox(at) ? 1 : 0);
                    if (k != 0) Add(at, at, v.Force * k, v.Moment * k);
                }
            }
            Point(source.NodeLoads(key));
            Point(source.Reactions(key));

            if (lost.Length > 1e-9 * Math.Max(scale, 1))
                r.Diagnostics.Add(new FemValidationDiagnostic("fragment_mask_nodal_unassigned", string.Create(CultureInfo.CurrentCulture,
                    $"{FragmentMaskExtractor.KeyText(key)}: силы ступеньки без разреза кромки на своей грани — |ΣF| = {lost.Length / 1e3:0.###} кН не вошли в маски."), false));
            r.Terms[key] = map;
        }
        r.Ok = true;
        return r;
    }

    /// <summary>
    /// Равновесие узлов тела без связей: |Σ_e (f_int,e − f_нагр,e) − P_N − R_N| ≤ допуска. Текст отказа или null.
    /// </summary>
    static string? SelfCheck(Body body, FragmentBounds bounds, FragmentMaskKey key, IFragmentParentNodalForces source,
        HashSet<string> linked, Func<FragmentMaskKey, string, IReadOnlyDictionary<string, Dof6>?> forces)
    {
        var loads = source.NodeLoads(key);
        var reactions = source.Reactions(key);
        double scale = 0;
        var residual = new List<(string Node, Dof6 R)>();
        foreach (var n in body.BodyNodes)
        {
            if (linked.Contains(n)) continue;
            var sum = Dof6.Zero;
            foreach (var e in body.ElementsAt[n])
            {
                if (forces(key, e)?.GetValueOrDefault(n) is not { } f)
                    return $"Самопроверка узловых сил КЭ, {FragmentMaskExtractor.KeyText(key)}: нет сил КЭ {e} в узле {n}";
                sum += f;
                var full = f + (source.ElementLoads(key, e).GetValueOrDefault(n) ?? Dof6.Zero);
                scale = Math.Max(scale, full.Force.Length);
            }
            residual.Add((n, sum - (loads.GetValueOrDefault(n) ?? Dof6.Zero) - (reactions.GetValueOrDefault(n) ?? Dof6.Zero)));
        }
        if (scale <= 0 || residual.Count == 0) return null;
        double length = Math.Max(bounds.Z2 - bounds.Z1, body.Extent);
        var worst = residual.Select(x => (x.Node, V: Math.Max(x.R.Force.Length / scale, x.R.Moment.Length / (scale * length))))
            .MaxBy(x => x.V);
        return worst.V > SelfCheckTolerance
            ? string.Create(CultureInfo.CurrentCulture,
                $"Самопроверка узловых сил КЭ, {FragmentMaskExtractor.KeyText(key)}: в узле {worst.Node} невязка {worst.V:0.##E+0} при допуске {SelfCheckTolerance:0.##E+0}")
            : null;
    }

    static void WarnLinks(FragmentNodalCutResult r, Body body, IReadOnlyDictionary<string, FemMeshNode> nodes, FragmentBounds bounds,
        IReadOnlyCollection<(string A, string? B)> links)
    {
        var crossing = new List<string>();
        foreach (var (a, b) in links)
        {
            bool inA = body.Holds(a, nodes, bounds);
            bool cross = b == null
                ? inA != (nodes.TryGetValue(a, out var p) && bounds.InBox(new PlanarVector3(p.X, p.Y, p.Z)))
                : inA != body.Holds(b, nodes, bounds);
            if (cross) crossing.Add(b == null ? a : $"{a}–{b}");
        }
        if (crossing.Count > 0)
            r.Diagnostics.Add(new FemValidationDiagnostic("fragment_mask_link_crosses_cut",
                $"Связи узлов (АЖТ, пружины) через ступеньку разреза: {crossing.Count} ({string.Join(", ", crossing.Take(10))}" +
                $"{(crossing.Count > 10 ? ", …" : "")}) — их силы в маски кромок не входят.", false));
    }
}
