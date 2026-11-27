using System.Globalization;
using System.Text.Json;
using CScore.Planar;

namespace CScore.Fem.Fragment;

public static partial class StoreyFragmentBuilder
{
    /// <summary>
    /// Импортные АЖТ ЛИРЫ/SCAD у колонн → явный след стержней фрагмента в узле ведущего (выпуклая оболочка узлов тела
    /// в плоскости плиты; спека A: импортное АЖТ важнее следа по сечению; колонна — стержень ведущего вне плоскости плиты).
    /// Прочие импортные, в т.ч. на концах рёбер в плоскости плиты, — «по точкам»: узлы схемы
    /// фрагмента в точках ведущего и ведомых (спека <c>2026-10-29-storey-fragment-rigid-points-design.md</c>), тела на
    /// узлах сетки строит сборка сетки. Авто-АЖТ родителя пересоздаются фрагментом; ручные — «не перенесено».
    /// При сохранении сетки родителя (спека 2026-11-10-fragment-keep-parent-mesh, разд. 4) тело любого происхождения,
    /// все узлы которого сохранены, переносится по тегам (<see cref="StoreyFragmentDraft.MeshRigidBodies"/>); тело с
    /// частью сохранённых узлов — нынешним путём и строкой <c>fragment_rigid_from_trace</c>.
    /// Отчёт — по строке на причину.
    /// </summary>
    static void TransferRigidBodies(Ctx c, StoreyFragmentParentSnapshot parent)
    {
        var meshNode = parent.MeshNodes.GroupBy(n => n.NodeTag).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var manual = new List<string>();
        var degenerate = new List<string>();
        var masterOutside = new List<string>();
        var slavesOutside = new List<string>();
        var partial = new List<string>();
        var points = new List<string>();
        var byTags = new List<string>();
        var fromTrace = new List<string>();
        var tracePoints = new List<string>();
        var mergedRigid = new List<string>();
        var mergedHinge = new List<string>();
        var hingeMasks = new SortedSet<string>(StringComparer.Ordinal);
        var missingNodes = new SortedSet<string>(FemNodeRigidBodies.TagComparer);
        var origins = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var body in parent.RigidBodies)
        {
            if (!meshNode.TryGetValue(body.MasterNodeTag, out var master)) continue;
            var m = new PlanarVector3(master.X, master.Y, master.Z);
            if (!c.InBox(m)) continue;
            string label = body.SourceElemTag ?? body.MasterNodeTag;
            string origin = BaseOrigin(body.Origin);
            if (c.Keep is not null)
            {
                var tags = body.SlaveNodeTags.Prepend(body.MasterNodeTag).ToList();
                var missing = tags.Where(t => !IsKeptNode(c, t)).ToList();
                if (missing.Count == 0)
                {
                    var kept = new FemRigidBody
                    {
                        MasterNodeTag = body.MasterNodeTag, Mask = body.Mask, Kind = body.Kind,
                        Origin = ParentRigidOriginPrefix + origin, SourceElemTag = body.SourceElemTag,
                    };
                    kept.SetSlaveNodeTags(body.SlaveNodeTags);
                    c.Draft.MeshRigidBodies.Add(kept);
                    byTags.Add(label);
                    continue;
                }
                if (missing.Count < tags.Count)
                {
                    fromTrace.Add(label);
                    missingNodes.UnionWith(missing);
                }
            }
            if (origin == RigidJunctionTraceSet.Provenance) continue;
            if (!origin.StartsWith("import:", StringComparison.Ordinal))
            {
                manual.Add(label);
                continue;
            }
            var slab = c.Slabs.FirstOrDefault(sl => Math.Abs(sl.Z - m.Z) <= c.Tol && RegionContains(sl.Region, m, c.Tol));
            var node = slab is null ? null : c.Nodes.Find(m);
            var bars = node is null ? [] : c.Draft.Members
                .Where(x => x.Member.ElemType == "beam" && NodeTags(x.Member).Contains(int.Parse(node.NodeTag)))
                .ToList();
            var hull = bars.Count == 0 ? [] : PlanarPolygonOps.ConvexHull(body.SlaveNodeTags.Append(body.MasterNodeTag)
                .Select(t => meshNode.GetValueOrDefault(t))
                .Where(n => n is not null && Math.Abs(n.Z - slab!.Z) <= c.Tol)
                .Select(n => new PlanarPoint2D(n!.X, n.Y)));
            bool flat = hull.Count < 3 || Math.Abs(PlanarPolygonOps.SignedArea(hull)) < c.Tol * c.Tol;
            // След — только телу колонны (у ведущего есть стержень вне плоскости плиты). Ведущий лишь на рёбрах в плоскости
            // плиты — «по точкам» независимо от площадки: у ребра в плоскости сетка явный след игнорирует
            // (rigid_junction_rib_trace_ignored), а без сечения с контуром тела нет вовсе («Музей», АЖТ 126043).
            if (bars.Any(x => !InSlabPlane(c, x.Member, slab!.Z)))
            {
                // Ведомые вне плиты (в проёме, за кромкой, вне её плоскости — узлы стен и стержней): след ведёт только узлы
                // сетки плиты, поэтому они переносятся «по точкам» отдельным телом с тем же ведущим.
                var offSlab = new List<string>();
                foreach (var tag in body.SlaveNodeTags)
                {
                    if (meshNode.GetValueOrDefault(tag) is not { } n || !c.InBox(P(n))) continue;
                    var p = new PlanarVector3(n.X, n.Y, n.Z);
                    if (RegionContains(slab!.Region, p, c.Tol) || Attach(c, p) is not { } q) continue;
                    if (c.Nodes.Get(q).NodeTag is var t && t != node!.NodeTag && !offSlab.Contains(t)) offSlab.Add(t);
                }
                if (offSlab.Count > 0)
                {
                    var extra = new FemRigidBody
                    {
                        MasterNodeTag = node!.NodeTag, Mask = body.Mask, Kind = body.Kind, Origin = origin, SourceElemTag = body.SourceElemTag,
                    };
                    extra.SetSlaveNodeTags(offSlab);
                    c.Draft.NodeRigidBodies.Add(extra);
                    tracePoints.Add(label);
                    origins.Add(origin);
                }
                if (flat)
                {
                    degenerate.Add(label);
                    continue;
                }
                string json = JsonSerializer.Serialize(hull.Select(p => new[] { p.U, p.V, slab!.Z }));
                foreach (var bar in bars) bar.Member.RigidJunctionTraceJson = json;
                continue;
            }

            // По точкам: ведущий и ведомые — на стержне или в пластине фрагмента (с привязкой, см. SnapToFragment).
            if (Attach(c, m) is not { } ms)
            {
                masterOutside.Add(label);
                continue;
            }
            m = ms;
            var slaves = new List<PlanarVector3>();
            int lost = 0;
            foreach (var tag in body.SlaveNodeTags)
            {
                if (meshNode.GetValueOrDefault(tag) is { } n && c.InBox(P(n)) && Attach(c, new(n.X, n.Y, n.Z)) is { } p)
                    slaves.Add(p);
                else lost++;
            }
            if (slaves.Count == 0)
            {
                slavesOutside.Add(label);
                continue;
            }
            var masterTag = c.Nodes.Get(m).NodeTag;
            var slaveTags = slaves.Select(p => c.Nodes.Get(p).NodeTag).Where(t => t != masterTag).Distinct().ToList();
            if (slaveTags.Count == 0)
            {
                // Ведомые совпали с ведущим (совпадающие узлы ЛИРЫ/SCAD сливаются во фрагменте в один): тело не нужно,
                // связь — общим узлом. Неполная маска (шарнир, «объединение перемещений») при этом теряется.
                bool full = (body.Mask & FemBoundaryDofs.All) is 0 or FemBoundaryDofs.All;
                (full ? mergedRigid : mergedHinge).Add(label);
                if (!full) hingeMasks.Add(FemBoundaryDofs.Describe(body.Mask & FemBoundaryDofs.All));
                continue;
            }
            if (lost > 0) partial.Add(label);
            var copy = new FemRigidBody
            {
                MasterNodeTag = masterTag, Mask = body.Mask, Kind = body.Kind, Origin = origin, SourceElemTag = body.SourceElemTag,
            };
            copy.SetSlaveNodeTags(slaveTags);
            c.Draft.NodeRigidBodies.Add(copy);
            points.Add(label);
            origins.Add(origin);
        }

        // Шаблон: {0} — число тел, {1} — их ведущие узлы, {2} — уточнение (источник, маски, узлы сетки).
        void Row(string code, string key, List<string> labels, object? extra = null)
        {
            if (labels.Count == 0) return;
            labels.Sort(FemNodeRigidBodies.TagComparer);
            c.Warn(code, new DiagText(key, labels.Count, DiagText.Total(labels.Distinct(), 10), extra), [.. labels]);
        }
        var source = DiagText.Join(", ", origins.Select(o => o switch
        {
            "import:scad" => "SCAD", "import:lira" => (object)new DiagText("fragment_rigid_source_lira"), _ => o,
        }));
        Row("fragment_rigid_body_points", "fragment_rigid_body_points", points, source);
        Row("fragment_rigid_body_merged", "fragment_rigid_body_merged", mergedRigid);
        Row("fragment_rigid_body_hinge_merged", "fragment_rigid_body_hinge_merged", mergedHinge, string.Join("; ", hingeMasks));
        Row("fragment_rigid_body_slaves_dropped", "fragment_rigid_body_slaves_dropped", partial);
        Row("fragment_rigid_body_lost", "fragment_rigid_body_lost_master", masterOutside);
        Row("fragment_rigid_body_lost", "fragment_rigid_body_lost_slaves", slavesOutside);
        Row("fragment_rigid_body_lost", "fragment_rigid_body_lost_manual", manual);
        Row("fragment_rigid_body_trace_points", "fragment_rigid_body_trace_points", tracePoints, source);
        Row("fragment_rigid_body_degenerate", "fragment_rigid_body_degenerate", degenerate);
        Row("fragment_rigid_body_kept", "fragment_rigid_body_kept", byTags);
        Row("fragment_rigid_from_trace", "fragment_rigid_from_trace", fromTrace, DiagText.Total(missingNodes.Distinct(), 10));
    }

    /// <summary>Приставка происхождения АЖТ родителя, перенесённых по тегам сохранённой сетки
    /// (<see cref="StoreyFragmentDraft.MeshRigidBodies"/>): <c>fragment:</c> + исходное. Сборка сетки такие тела не
    /// пересоздаёт (в отличие от <c>auto:junction</c> и <c>auto:node_points</c>) и считает их узлы занятыми.</summary>
    public const string ParentRigidOriginPrefix = "fragment:";

    /// <summary>Происхождение тела без приставок <see cref="ParentRigidOriginPrefix"/> (фрагмент фрагмента).</summary>
    public static string BaseOrigin(string origin)
    {
        while (origin.StartsWith(ParentRigidOriginPrefix, StringComparison.Ordinal)) origin = origin[ParentRigidOriginPrefix.Length..];
        return origin;
    }

    /// <summary>Наибольшее расхождение сетки источника с КонЭ фрагмента, м, при котором точка АЖТ (проекцией) и
    /// нагрузка КЭ стержня относятся к оси стержня или плоскости пластины: импортные КонЭ расходятся с узлами сетки
    /// источника на миллиметры (у «Музея» ось колонны наклонена на 2,6 мм на 13,5 м).</summary>
    public const double SourceSnapM = 0.01;

    /// <summary>
    /// Точка АЖТ во фрагменте: на КЭ (с привязкой, <see cref="SnapToFragment"/>) или узел фрагмента, уже созданный
    /// под закрепление или пружину (голова сваи без КЭ, спека 4а) — как есть; иначе null.
    /// </summary>
    static PlanarVector3? Attach(Ctx c, PlanarVector3 p) =>
        SnapToFragment(c, p) ?? (c.Nodes.Find(p) is not null ? p : null);

    /// <summary>
    /// Точка на оси стержня фрагмента или в области его пластины (кромки включительно): ближе допуска — как есть,
    /// ближе <see cref="SourceSnapM"/> — проекция на ближайшие ось или плоскость; иначе null.
    /// </summary>
    static PlanarVector3? SnapToFragment(Ctx c, PlanarVector3 p)
    {
        PlanarVector3? best = null;
        double bestD = SourceSnapM;
        foreach (var x in c.Draft.Members)
        {
            PlanarVector3 q;
            if (x.Region is { } region)
            {
                double w = PlanarBoundaryFrameConverter.ToLocalPoint(region.Frame, p).Z;
                q = p - region.Frame.LocalZ * w;
                if (!RegionContains(region, q, c.Tol)) continue;
            }
            else if (x.Member.ElemType == "beam" && NodeTags(x.Member) is { Length: >= 2 } ids)
            {
                // По тегу: при сохранении сетки родителя теги узлов не идут подряд (FragmentNodeTable).
                if (c.Nodes.ByTag(ids[0].ToString(CultureInfo.InvariantCulture)) is not { } na
                    || c.Nodes.ByTag(ids[^1].ToString(CultureInfo.InvariantCulture)) is not { } nb) continue;
                PlanarVector3 a = new(na.X, na.Y, na.Z), d = new PlanarVector3(nb.X, nb.Y, nb.Z) - a;
                double l2 = d.Dot(d);
                q = At(a, d, l2 == 0 ? 0 : Math.Clamp((p - a).Dot(d) / l2, 0, 1));
            }
            else continue;
            double dist = (p - q).Length;
            if (dist <= c.Tol) return p;
            if (dist <= bestD) (best, bestD) = (q, dist);
        }
        return best;
    }

    /// <summary>Оба конца стержня фрагмента на отметке плиты <paramref name="z"/> (ребро в её плоскости).</summary>
    static bool InSlabPlane(Ctx c, FemMember bar, double z) =>
        NodeTags(bar) is { Length: >= 2 } ids
        && c.Nodes.ByTag(ids[0].ToString(CultureInfo.InvariantCulture)) is { } a
        && c.Nodes.ByTag(ids[^1].ToString(CultureInfo.InvariantCulture)) is { } b
        && Math.Abs(a.Z - z) <= c.Tol && Math.Abs(b.Z - z) <= c.Tol;
}
