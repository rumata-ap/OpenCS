using CScore.Fem.Loads;
using CScore.Planar;

namespace CScore.Fem.Fragment;

public static partial class StoreyFragmentBuilder
{
    /// <summary>Многоугольник в 3D, отсечённый всеми плоскостями фрагмента (полоса и призма); пусто — снаружи.</summary>
    static List<PlanarVector3> ClipBox(Ctx c, List<PlanarVector3> polygon) => c.Bounds.ClipPolygon(polygon);

    /// <summary>Площадь и центр плоского выпуклого многоугольника в 3D (веер треугольников).</summary>
    static (double Area, PlanarVector3 Centroid) AreaCentroid(List<PlanarVector3> polygon) => FragmentBounds.AreaCentroid(polygon);

    /// <summary>
    /// Нагрузки КЭ пластин родителя (спека разд. 4): равномерные копятся по КЭ вектором в глобальных осях и поднимаются
    /// на КонЭ при одном векторе на все КЭ части, иначе — поле; по узлам (неравные) и на ребро — поле; сосредоточенные —
    /// узловые нагрузки фрагмента в той же точке; собственный вес КЭ — на КонЭ при одном k на все КЭ части. Родитель —
    /// часть нагрузки КЭ внутри границ по виду (<see cref="FragmentLoadInside"/>, общий помощник с масками).
    /// </summary>
    sealed class ShellLoads(LoadCtx l)
    {
        readonly Dictionary<(int LoadCase, string Elem), PlanarVector3> _uniform = [];
        readonly Dictionary<(int LoadCase, string Elem), double> _selfWeight = [];
        readonly Dictionary<(int LoadCase, string Kind, string Axis), List<FragmentFieldEntry>> _field = [];

        public void Add(FemElementLoad load, FemElement e, FemElementGeometry g)
        {
            var polygon = l.ShellClip(e, g).Polygon;
            if (polygon.Count < 3) return;
            int lc = load.LoadCaseId;
            var v = load.Values;
            if (FemElementLoadNodalizer.Direction(load, g, null) is not { } dir) { l.Lost.Add(e.ElemTag); return; }
            // Равнодействующая родителя — часть нагрузки внутри границ по виду (общий помощник с масками).
            void Parent(string category) => l.AddParentPieces(lc, category, FemElementLoadPieces.Of(load, e, l.Mesh, 1));
            switch (load.LoadKind)
            {
                case FemElementLoadKinds.Uniform when v.Count >= 1:
                    Uniform(lc, e.ElemTag, dir * v[0]);
                    Parent(LoadCategory.Uniform);
                    break;
                case FemElementLoadKinds.Nodal when v.Count == g.Points.Count:
                    if (v.All(x => Math.Abs(x - v[0]) <= 1e-12 * Math.Max(1, Math.Abs(v[0]))))
                    {
                        Uniform(lc, e.ElemTag, dir * v[0]);
                        Parent(LoadCategory.Uniform);
                        break;
                    }
                    Parent(LoadCategory.Nodal);
                    Field(lc, FemElementLoadKinds.Nodal, dir, e.ElemTag, d => v.Select(x => x * d).ToArray());
                    break;
                case FemElementLoadKinds.Edge when v.Count >= 4 && FemElementLoadNodalizer.ShellEdge(g, v[0], v[1]) is not null:
                    Parent(LoadCategory.Edge);
                    Field(lc, FemElementLoadKinds.Edge, dir, e.ElemTag, d => [v[0], v[1], v[2] * d, v[3] * d]);
                    break;
                case FemElementLoadKinds.Point when v.Count >= 3:
                {
                    var at = FemElementLoadNodalizer.ShellPointAt(g, v[1], v[2]);
                    if (!l.C.InBox(at)) break;
                    l.AddParent(lc, LoadCategory.Point, at, dir * v[0], PlanarVector3.Zero);
                    l.AddNodeLoad(lc, LoadCategory.Point, at, dir * v[0], PlanarVector3.Zero);
                    break;
                }
                default:
                    l.Lost.Add(e.ElemTag);
                    break;
            }
        }

        /// <summary>Коэффициент собственного веса КЭ (стержня или пластины), заходящего в полосу.</summary>
        public void SelfWeight(FemElementLoad load, FemElement e, FemElementGeometry g)
        {
            if (load.Values.Count < 1 || l.PartOf(e, g) is null) return;
            var key = (load.LoadCaseId, e.ElemTag);
            _selfWeight[key] = _selfWeight.GetValueOrDefault(key) + load.Values[0];
        }

        void Uniform(int lc, string elem, PlanarVector3 q) => _uniform[(lc, elem)] = _uniform.GetValueOrDefault((lc, elem)) + q;

        /// <summary>Записи поля по глобальным осям: компонента d вектора <paramref name="direction"/> масштабирует значения.</summary>
        void Field(int lc, string kind, PlanarVector3 direction, string refTag, Func<double, double[]> values)
        {
            double eps = 1e-12 * direction.Length;
            foreach (var (axis, d) in new[] { ("x", direction.X), ("y", direction.Y), ("z", direction.Z) })
            {
                if (Math.Abs(d) <= eps) continue;
                if (!_field.TryGetValue((lc, kind, axis), out var list)) _field[(lc, kind, axis)] = list = [];
                list.Add(new FragmentFieldEntry(refTag, values(d)));
            }
        }

        public void Flush()
        {
            var byPart = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var e in l.Band)
            {
                if (l.Mesh.Geometry(e) is not { } g || !(g.IsBar || g.IsShell) || l.PartOf(e, g) is not { } part) continue;
                if (!byPart.TryGetValue(part, out var list)) byPart[part] = list = [];
                list.Add(e.ElemTag);
            }
            var inPart = byPart.Values.SelectMany(x => x).ToHashSet(StringComparer.Ordinal);

            foreach (int lc in _uniform.Keys.Select(k => k.LoadCase).Distinct().Order())
            {
                foreach (var (part, elems) in byPart)
                {
                    var q = elems.Select(t => _uniform.GetValueOrDefault((lc, t))).ToList();
                    double scale = q.Max(x => x.Length);
                    if (scale == 0) continue;
                    if (q.All(x => (x - q[0]).Length <= 1e-9 * scale))
                        foreach (var (axis, value) in Components(q[0])) Lift(lc, part, FemElementLoadKinds.Uniform, axis, value, LoadCategory.Uniform);
                    else
                        for (int k = 0; k < elems.Count; k++)
                            if (q[k].Length > 0) Field(lc, FemElementLoadKinds.Uniform, q[k], elems[k], d => [d]);
                }
                foreach (var key in _uniform.Keys.Where(k => k.LoadCase == lc && !inPart.Contains(k.Elem))) l.Lost.Add(key.Elem);
            }

            foreach (int lc in _selfWeight.Keys.Select(k => k.LoadCase).Distinct().Order())
                foreach (var (part, elems) in byPart)
                {
                    var k = elems.Select(t => _selfWeight.GetValueOrDefault((lc, t))).ToList();
                    if (k.All(x => x == 0)) continue;
                    if (k.All(x => Math.Abs(x - k[0]) <= 1e-12 * Math.Abs(k[0]))) Lift(lc, part, FemElementLoadKinds.SelfWeight, "z", k[0], null);
                    else l.C.Warn("fragment_self_weight_mixed",
                        $"Собственный вес КЭ с разными коэффициентами на КонЭ «{part}» не перенесён — задайте коэффициент загружения.", part);
                }

            foreach (var ((lc, kind, axis), entries) in _field)
            {
                var field = new FragmentFieldLoad(lc, kind, axis, entries);
                l.C.Draft.FieldLoads.Add(field);
                l.CategoryOf[field] = kind switch
                {
                    FemElementLoadKinds.Uniform => LoadCategory.Uniform,
                    FemElementLoadKinds.Nodal => LoadCategory.Nodal,
                    _ => LoadCategory.Edge,
                };
            }
        }

        void Lift(int lc, string part, string kind, string axis, double value, string? category)
        {
            var load = new FemElementLoad
            {
                LoadCaseId = lc, TargetKind = FemLoadTargetKinds.Members, LoadKind = kind, CoordinateSystem = "global", Axis = axis,
            };
            load.SetTargetTags([part]);
            load.SetValues([value]);
            l.C.Draft.ElementLoads.Add(load);
            if (category is not null) l.CategoryOf[load] = category;
        }

        static IEnumerable<(string Axis, double Value)> Components(PlanarVector3 q)
        {
            double eps = 1e-12 * q.Length;
            if (Math.Abs(q.X) > eps) yield return ("x", q.X);
            if (Math.Abs(q.Y) > eps) yield return ("y", q.Y);
            if (Math.Abs(q.Z) > eps) yield return ("z", q.Z);
        }
    }
}
