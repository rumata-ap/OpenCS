using CScore.Fem.Fields;
using CScore.Fem.Loads;
using CScore.Planar;

namespace CScore.Fem.Fragment;

/// <summary>
/// Нагрузки-поля фрагмента на конкретной сетке (спека разд. 4): новый КЭ берёт значения своего КЭ родителя
/// (<see cref="ParentMeshMapper"/> по центру нового КЭ): <c>uniform</c> — q; <c>nodal</c> — значения в узлах нового
/// КЭ по функциям формы КЭ родителя (узел за его краем — по КЭ родителя, содержащему узел); <c>edge</c> — в рёбра
/// новой сетки на той же линии, линейно по длине. Результат — <see cref="FemElementLoad"/> с целью «КЭ» в глобальных
/// осях с загружением поля.
/// </summary>
public static class FragmentFieldLoadResolver
{
    public static List<FemElementLoad> Resolve(IReadOnlyList<FragmentFieldLoad> fields, ParentMeshMapper mapper,
        IReadOnlyList<FragmentRefElement> refs, FemLoadMeshContext mesh, List<FemValidationDiagnostic> diagnostics,
        double toleranceM = 0.001)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(mesh);
        var refByTag = refs.GroupBy(r => r.Tag).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var shells = mesh.Elements.Select(e => (Element: e, Geometry: mesh.Geometry(e)))
            .Where(x => x.Geometry is { IsShell: true }).Select(x => (x.Element, Geometry: x.Geometry!)).ToList();
        var mapped = new Dictionary<string, FragmentRefElement?>(StringComparer.Ordinal);
        var unmapped = new List<string>();
        // Отсев до сопоставления: пара КЭ — КЭ родителя, содержащий центр или ближе половины размера КЭ, поэтому центр
        // её не дальше «радиус КЭ родителя + половина размера + допуск». КЭ, у которых ни один КЭ родителя с полем не
        // ближе этого, поле не получат — их не сопоставляем (пробная сетка полосы — сотни тысяч КЭ на десятки КЭ поля).
        var fieldRefs = fields.SelectMany(f => f.Entries).Select(x => refByTag.GetValueOrDefault(x.RefTag))
            .OfType<FragmentRefElement>().Where(r => r.IsShell).Distinct().ToList();
        var fieldTree = new KdTree3(fieldRefs.Select(r => new FieldVec3(r.Centroid.X, r.Centroid.Y, r.Centroid.Z)).ToList());
        double fieldRadius = fieldRefs.Count == 0 ? 0 : fieldRefs.Max(r => r.Radius);
        var (fieldMin, fieldMax) = fieldRefs.Count == 0 ? default : Box(fieldRefs.Select(r => r.Centroid).ToList());
        bool NearField(FemElementGeometry g)
        {
            if (fieldRefs.Count == 0) return false;
            var c = g.Centroid;
            double reach = fieldRadius + Math.Sqrt(g.Area) / 2 + 2 * toleranceM;
            // Габарит центров — до дерева: для дальней точки k-d дерево обходит почти все узлы.
            if (c.X < fieldMin.X - reach || c.X > fieldMax.X + reach || c.Y < fieldMin.Y - reach || c.Y > fieldMax.Y + reach
                || c.Z < fieldMin.Z - reach || c.Z > fieldMax.Z + reach) return false;
            return fieldTree.Nearest(new FieldVec3(c.X, c.Y, c.Z)).Distance <= reach;
        }
        FragmentRefElement? Map(FemElement e, FemElementGeometry g)
        {
            if (!NearField(g)) return null;
            if (!mapped.TryGetValue(e.ElemTag, out var r))
            {
                mapped[e.ElemTag] = r = FragmentParentMap.MapElement(mapper, g);
                if (r is null) unmapped.Add(e.ElemTag);
            }
            return r;
        }

        var result = new List<FemElementLoad>();
        foreach (var f in fields)
            switch (f.LoadKind)
            {
                case FemElementLoadKinds.Uniform: Uniform(f, shells, Map, result); break;
                case FemElementLoadKinds.Nodal: Nodal(f, shells, Map, mapper, result); break;
                case FemElementLoadKinds.Edge: Edge(f, shells, refByTag, toleranceM, result, diagnostics); break;
                default:
                    diagnostics.Add(new("fragment_field_kind_unknown", $"Нагрузка-поле вида «{f.LoadKind}» не поддержана — пропущена.", false));
                    break;
            }
        if (unmapped.Count > 0)
            diagnostics.Add(new("fragment_field_unmapped",
                $"КЭ фрагмента без пары в опорной сетке (нагрузка-поле на них — 0): {FemLoadTargets.Sample(unmapped)}.", false, unmapped.ToArray()));
        return result;
    }

    static FemElementLoad Load(FragmentFieldLoad f, string kind, IEnumerable<string> tags, IEnumerable<double> values)
    {
        var load = new FemElementLoad
        {
            LoadCaseId = f.LoadCaseId, TargetKind = FemLoadTargetKinds.Elements, LoadKind = kind,
            CoordinateSystem = f.CoordinateSystem, Axis = f.Axis,
        };
        load.SetTargetTags(tags);
        load.SetValues(values);
        return load;
    }

    static void Uniform(FragmentFieldLoad f, List<(FemElement Element, FemElementGeometry Geometry)> shells,
        Func<FemElement, FemElementGeometry, FragmentRefElement?> map, List<FemElementLoad> result)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var entry in f.Entries)
            if (entry.Values.Count > 0) values[entry.RefTag] = values.GetValueOrDefault(entry.RefTag) + entry.Values[0];
        var groups = new Dictionary<double, List<string>>();
        foreach (var (e, g) in shells)
        {
            if (map(e, g) is not { } r || !values.TryGetValue(r.Tag, out double q) || q == 0) continue;
            if (!groups.TryGetValue(q, out var tags)) groups[q] = tags = [];
            tags.Add(e.ElemTag);
        }
        foreach (var (q, tags) in groups) result.Add(Load(f, FemElementLoadKinds.Uniform, tags, [q]));
    }

    static void Nodal(FragmentFieldLoad f, List<(FemElement Element, FemElementGeometry Geometry)> shells,
        Func<FemElement, FemElementGeometry, FragmentRefElement?> map, ParentMeshMapper mapper, List<FemElementLoad> result)
    {
        var values = f.Entries.GroupBy(x => x.RefTag).ToDictionary(g => g.Key, g => g.First().Values, StringComparer.Ordinal);
        foreach (var (e, g) in shells)
        {
            if (map(e, g) is not { } r || !values.TryGetValue(r.Tag, out var v)) continue;
            var q = g.Points.Select(p => At(p, r, v)
                ?? (mapper.MapShell(p, r.Direction) is { } r2 && values.TryGetValue(r2.Tag, out var v2) ? At(p, r2, v2) : null)
                ?? 0).ToArray();
            if (q.All(x => x == 0)) continue;
            result.Add(Load(f, FemElementLoadKinds.Nodal, [e.ElemTag], q));
        }
    }

    static (PlanarVector3 Min, PlanarVector3 Max) Box(IReadOnlyList<PlanarVector3> points) =>
        (new(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z)),
         new(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z)));

    /// <summary>Значение по узлам КЭ родителя <paramref name="r"/> в точке; null — точка за его краем.</summary>
    static double? At(PlanarVector3 p, FragmentRefElement r, IReadOnlyList<double> v) =>
        v.Count == r.Points.Count && r.Geometry.ShellShapeAt(p) is { } n ? n.Select((x, k) => x * v[k]).Sum() : null;

    static void Edge(FragmentFieldLoad f, List<(FemElement Element, FemElementGeometry Geometry)> shells,
        Dictionary<string, FragmentRefElement> refByTag, double tol, List<FemElementLoad> result, List<FemValidationDiagnostic> diagnostics)
    {
        var missed = new List<string>();
        // Габариты КЭ — один раз: на запись проверяются только КЭ, габарит которых задевает отрезок.
        var boxes = shells.Select(x => Box(x.Geometry.Points)).ToArray();
        foreach (var entry in f.Entries)
        {
            var v = entry.Values;
            if (!refByTag.TryGetValue(entry.RefTag, out var r) || v.Count < 4
                || FemElementLoadNodalizer.ShellEdge(r.Geometry, v[0], v[1]) is not var (i, j))
            {
                missed.Add(entry.RefTag);
                continue;
            }
            PlanarVector3 a = r.Points[i], b = r.Points[j];
            double len = (b - a).Length;
            var u = (b - a) * (1 / len);
            double? T(PlanarVector3 p)
            {
                double t = (p - a).Dot(u);
                return t >= -tol && t <= len + tol && (p - (a + u * t)).Length <= tol ? Math.Clamp(t, 0, len) : null;
            }
            double Q(double t) => v[2] + (v[3] - v[2]) * t / len;
            // Ребро новой сетки — один раз на запись, даже если оно общее у двух КЭ.
            var assigned = new HashSet<(string, string)>();
            var (lo, hi) = Box([a, b]);
            for (int s = 0; s < shells.Count; s++)
            {
                var (blo, bhi) = boxes[s];
                if (blo.X > hi.X + tol || bhi.X < lo.X - tol || blo.Y > hi.Y + tol || bhi.Y < lo.Y - tol
                    || blo.Z > hi.Z + tol || bhi.Z < lo.Z - tol) continue;
                var (e, g) = shells[s];
                for (int k = 0; k < g.Contour.Count; k++)
                {
                    int ia = g.Contour[k], ib = g.Contour[(k + 1) % g.Contour.Count];
                    if (T(g.Points[ia]) is not { } ta || T(g.Points[ib]) is not { } tb || Math.Abs(tb - ta) < tol) continue;
                    var key = string.CompareOrdinal(g.NodeTags[ia], g.NodeTags[ib]) < 0
                        ? (g.NodeTags[ia], g.NodeTags[ib]) : (g.NodeTags[ib], g.NodeTags[ia]);
                    if (assigned.Add(key)) result.Add(Load(f, FemElementLoadKinds.Edge, [e.ElemTag], [ia + 1, ib + 1, Q(ta), Q(tb)]));
                }
            }
            if (assigned.Count == 0) missed.Add(entry.RefTag);
        }
        if (missed.Count > 0)
            diagnostics.Add(new("fragment_field_edge_unmapped",
                $"Нагрузка на ребро не легла на рёбра сетки фрагмента (КЭ родителя {FemLoadTargets.Sample(missed)}).", false, missed.ToArray()));
    }
}
