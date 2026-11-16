using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using CScore.Fem.Loads;
using CScore.Planar;
using CScore.Submodel;

namespace CScore.Sp63Punching.Fem;

/// <summary>Сила по разрезу основания пирамиды по одному ключу.</summary>
/// <param name="Force">−(действие внешней части плиты на основание)·n, кН: со знаком силы продавливания (+ — снизу).</param>
/// <param name="Mx">Момент действия основания на внешнюю часть плиты относительно оси x узла, кН·м (справочно).</param>
/// <param name="My">То же относительно оси y, кН·м.</param>
/// <param name="Terms">Слагаемые разреза (точка, сила в общих осях, Н) — для рисунка и разбиения по кускам.</param>
/// <param name="WallExcluded">Узел стены: сила на гранях основания поперёк стены, кН (в <paramref name="Force"/> не входит).</param>
public sealed record PunchingFemCutForce(double Force, double Mx, double My, IReadOnlyList<FragmentNodalTerm> Terms, double WallExcluded = 0);

/// <summary>Итог разреза узла: силы по ключам или причина отказа.</summary>
public sealed record PunchingFemCutResult(
    IReadOnlyDictionary<FragmentMaskKey, PunchingFemCutForce> ByKey, string? Failure, IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>
/// Способ C (спека среза 3): тело — КЭ плиты с центром внутри основания пирамиды продавливания (площадь, смещённая на
/// h0 и обрезанная краями, — п. 8.1.46: сила «за вычетом сил в пределах основания пирамиды»); сила внешней части плиты
/// в узлах ступеньки — по узловым силам КЭ (<see cref="FragmentNodalCut"/>, с поправкой на полоску между ступенькой и
/// гранью и самопроверкой равновесия узлов). Прочие КЭ у узлов тела (стержни колонн, балки) — ни тело, ни внешняя
/// часть: их силы в разрез не входят. Из равновесия тела сила по разрезу равна действию колонн плюс нагрузке основания.
/// </summary>
public static class PunchingFemCut
{
    /// <summary>Допуск попадания узла в плоскость плиты, м.</summary>
    const double PlaneTol = 1e-3;

    /// <summary>Источник с узловыми нагрузками, реакциями и связями только у узлов, прошедших фильтр.</summary>
    sealed class PlaneSource(IFragmentParentNodalForces inner, Func<string, bool> keep) : IFragmentParentNodalForces
    {
        public bool TryElementNodeForces(FragmentMaskKey key, string elemTag, out IReadOnlyDictionary<string, Dof6> forces) =>
            inner.TryElementNodeForces(key, elemTag, out forces);
        public IReadOnlyDictionary<string, Dof6> ElementLoads(FragmentMaskKey key, string elemTag) => inner.ElementLoads(key, elemTag);
        public IReadOnlyList<FemLoadPiece> ElementLoadPieces(FragmentMaskKey key, string elemTag) => inner.ElementLoadPieces(key, elemTag);
        public IReadOnlyDictionary<string, Dof6> NodeLoads(FragmentMaskKey key) => Filter(inner.NodeLoads(key));
        public IReadOnlyDictionary<string, Dof6> Reactions(FragmentMaskKey key) => Filter(inner.Reactions(key));
        public IReadOnlyCollection<(string A, string? B)> NodeLinks =>
            _links ??= inner.NodeLinks.Where(l => keep(l.A) || l.B != null && keep(l.B)).ToList();
        public IReadOnlyCollection<string> RemoteElements(string nodeTag) => inner.RemoteElements(nodeTag);
        IReadOnlyCollection<(string A, string? B)>? _links;

        Dictionary<string, Dof6> Filter(IReadOnlyDictionary<string, Dof6> values) =>
            values.Where(kv => keep(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Геометрия КЭ схемы и КЭ у каждого узла — строятся один раз на схему и служат всем её узлам продавливания
    /// (без индекса каждый узел перебирал бы все КЭ схемы).
    /// </summary>
    public sealed class MeshIndex
    {
        public MeshIndex(IReadOnlyDictionary<string, FemMeshNode> nodes, IReadOnlyList<FemElement> elements)
        {
            ArgumentNullException.ThrowIfNull(nodes);
            ArgumentNullException.ThrowIfNull(elements);
            foreach (var e in elements)
            {
                if (FemElementGeometry.Of(e, nodes) is not { } g) continue;
                Ordered.Add((e, g));
                foreach (var t in g.NodeTags)
                {
                    if (!At.TryGetValue(t, out var l)) At[t] = l = [];
                    l.Add(e);
                }
            }
        }

        /// <summary>КЭ с геометрией в порядке схемы.</summary>
        internal List<(FemElement Element, FemElementGeometry Geometry)> Ordered { get; } = [];
        /// <summary>КЭ у узла.</summary>
        internal Dictionary<string, List<FemElement>> At { get; } = new(StringComparer.Ordinal);
    }

    public static PunchingFemCutResult Compute(PunchingFemJoint joint, IReadOnlyDictionary<string, FemMeshNode> nodes,
        IReadOnlyList<FemElement> elements, IReadOnlySet<string> slabShells, IReadOnlyList<FragmentMaskKey> keys,
        IFragmentParentNodalForces source) =>
        Compute(joint, nodes, new MeshIndex(nodes, elements), slabShells, keys, source);

    public static PunchingFemCutResult Compute(PunchingFemJoint joint, IReadOnlyDictionary<string, FemMeshNode> nodes,
        MeshIndex index, IReadOnlySet<string> slabShells, IReadOnlyList<FragmentMaskKey> keys,
        IFragmentParentNodalForces source)
    {
        ArgumentNullException.ThrowIfNull(joint);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(source);
        var faces = PunchingFemColumnAction.BaseFaces(joint);
        var bounds = PunchingFemColumnAction.BaseBounds(joint);

        // КЭ плиты у основания: центр в основании (тело) и все КЭ у узлов тела.
        var bodyNodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (e, g) in index.Ordered)
            if (slabShells.Contains(e.ElemTag) && bounds.InBox(g.Centroid))
                bodyNodes.UnionWith(g.NodeTags);
        var selected = new Dictionary<string, FemElement>(StringComparer.Ordinal);
        foreach (var n in bodyNodes)
            if (index.At.TryGetValue(n, out var es))
                foreach (var e in es) selected.TryAdd(e.ElemTag, e);
        var list = selected.Values.ToList();
        var others = list.Where(e => !slabShells.Contains(e.ElemTag)).Select(e => e.ElemTag).ToHashSet(StringComparer.Ordinal);

        var cuts = faces.Select(f => new FragmentMaskCutInput(
            new FragmentCut("punching_base", FragmentCutKind.Edge, FragmentCutSide.Lateral, joint.Origin.Z, null, [(f.A, f.B)], f.Face.Normal),
            null, joint.Normal)).ToList();
        // Узловые нагрузки, реакции и связи — только у узлов в плоскости плиты: дальние концы стержней колонн в полосе
        // границ (±50 м) иначе вошли бы в поправку ступеньки своими опорными реакциями.
        bool InPlane(string t) => nodes.TryGetValue(t, out var mn)
            && Math.Abs((new PlanarVector3(mn.X, mn.Y, mn.Z) - joint.Origin).Dot(joint.Normal)) <= PlaneTol;
        var r = FragmentNodalCut.Compute(cuts, bounds, nodes, list, others, keys, new PlaneSource(source, InPlane));
        if (!r.Ok)
            return new(new Dictionary<FragmentMaskKey, PunchingFemCutForce>(),
                r.Diagnostics.FirstOrDefault(d => d.Code == "fragment_mask_nodal_selfcheck")?.Message ?? "самопроверка узловых сил не прошла",
                r.Diagnostics);

        var byKey = new Dictionary<FragmentMaskKey, PunchingFemCutForce>();
        foreach (var key in keys)
        {
            var terms = r.Terms.TryGetValue(key, out var map) ? map.Values.SelectMany(x => x).ToList() : [];
            // Узел стены (СП 430 п. 6.3.8): сила — по длине контура, грани основания поперёк стены не входят.
            double excluded = 0;
            if (joint.Wall is { } wall)
            {
                var kept = new List<FragmentNodalTerm>(terms.Count);
                foreach (var t in terms)
                    if (WallCrossing(joint, wall, t.At)) excluded -= t.F.Dot(joint.Normal) / 1e3;
                    else kept.Add(t);
                terms = kept;
            }
            var f = PlanarVector3.Zero;
            var m = PlanarVector3.Zero;
            foreach (var t in terms)
            {
                f += t.F;
                m += t.M + (t.At - joint.Origin).Cross(t.F);
            }
            // Термы — действие внешней части на тело; сила продавливания — обратная по нормали.
            byKey[key] = new PunchingFemCutForce(-f.Dot(joint.Normal) / 1e3, -m.Dot(joint.AxisX) / 1e3, -m.Dot(joint.AxisY) / 1e3, terms, excluded);
        }
        return new(byKey, null, r.Diagnostics);
    }

    /// <summary>Слагаемое разреза лежит у грани основания, пересекающей стену (ближайшая грань — поперёк стены).</summary>
    static bool WallCrossing(PunchingFemJoint joint, PunchingWall wall, PlanarVector3 at)
    {
        var p = joint.ToLocal(at);
        var poly = joint.PyramidBase;
        int best = -1;
        double dmin = double.MaxValue;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var ab = poly[(i + 1) % poly.Count] - a;
            double l2 = ab.Dot(ab);
            double t = l2 > 0 ? Math.Clamp((p - a).Dot(ab) / l2, 0, 1) : 0;
            double d = (p - (a + t * ab)).Length;
            if (d < dmin) { dmin = d; best = i; }
        }
        return best >= 0 && PunchingWallGeometry.CrossesWall(wall, poly[best], poly[(best + 1) % poly.Count]);
    }
}
