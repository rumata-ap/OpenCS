using System.Text.Json;
using CScore.Planar;

namespace CScore.Fem.Fragment;

public static partial class StoreyFragmentBuilder
{
    /// <summary>
    /// Импортные АЖТ ЛИРЫ/SCAD у колонн → явный след стержней фрагмента в узле ведущего (выпуклая оболочка узлов тела
    /// в плоскости плиты; спека A: импортное АЖТ важнее следа по сечению). Прочие импортные — «по точкам»: узлы схемы
    /// фрагмента в точках ведущего и ведомых (спека <c>2026-10-29-storey-fragment-rigid-points-design.md</c>), тела на
    /// узлах сетки строит сборка сетки. Авто-АЖТ родителя пересоздаются фрагментом; ручные — «не перенесено».
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
        var origins = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var body in parent.RigidBodies)
        {
            if (!meshNode.TryGetValue(body.MasterNodeTag, out var master)) continue;
            var m = new PlanarVector3(master.X, master.Y, master.Z);
            if (!c.InBand(m.Z) || body.Origin == RigidJunctionTraceSet.Provenance) continue;
            string label = body.SourceElemTag ?? body.MasterNodeTag;
            if (!body.Origin.StartsWith("import:", StringComparison.Ordinal))
            {
                manual.Add(label);
                continue;
            }
            var slab = c.Slabs.FirstOrDefault(sl => Math.Abs(sl.Z - m.Z) <= c.Tol && RegionContains(sl.Region, m, c.Tol));
            var node = slab is null ? null : c.Nodes.Find(m);
            var bars = node is null ? [] : c.Draft.Members
                .Where(x => x.Member.ElemType == "beam" && NodeTags(x.Member).Contains(int.Parse(node.NodeTag)))
                .ToList();
            if (bars.Count > 0)
            {
                var hull = PlanarPolygonOps.ConvexHull(body.SlaveNodeTags.Append(body.MasterNodeTag)
                    .Select(t => meshNode.GetValueOrDefault(t))
                    .Where(n => n is not null && Math.Abs(n.Z - slab!.Z) <= c.Tol)
                    .Select(n => new PlanarPoint2D(n!.X, n.Y)));
                if (hull.Count < 3 || Math.Abs(PlanarPolygonOps.SignedArea(hull)) < c.Tol * c.Tol)
                {
                    degenerate.Add(label);
                    continue;
                }
                string json = JsonSerializer.Serialize(hull.Select(p => new[] { p.U, p.V, slab!.Z }));
                foreach (var bar in bars) bar.Member.RigidJunctionTraceJson = json;
                continue;
            }

            // По точкам: ведущий и ведомые — на стержне или в пластине фрагмента (с привязкой, см. SnapToFragment).
            if (SnapToFragment(c, m) is not { } ms)
            {
                masterOutside.Add(label);
                continue;
            }
            m = ms;
            var slaves = new List<PlanarVector3>();
            int lost = 0;
            foreach (var tag in body.SlaveNodeTags)
            {
                if (meshNode.GetValueOrDefault(tag) is { } n && c.InBand(n.Z) && SnapToFragment(c, new(n.X, n.Y, n.Z)) is { } p)
                    slaves.Add(p);
                else lost++;
            }
            if (slaves.Count == 0)
            {
                slavesOutside.Add(label);
                continue;
            }
            if (lost > 0) partial.Add(label);
            var masterTag = c.Nodes.Get(m).NodeTag;
            var copy = new FemRigidBody
            {
                MasterNodeTag = masterTag, Mask = body.Mask, Kind = body.Kind, Origin = body.Origin, SourceElemTag = body.SourceElemTag,
            };
            copy.SetSlaveNodeTags(slaves.Select(p => c.Nodes.Get(p).NodeTag).Where(t => t != masterTag));
            c.Draft.NodeRigidBodies.Add(copy);
            points.Add(label);
            origins.Add(body.Origin);
        }

        void Row(string code, List<string> labels, string text)
        {
            if (labels.Count == 0) return;
            labels.Sort(FemNodeRigidBodies.TagComparer);
            c.Warn(code, string.Format(text, labels.Count, FemNodeRigidBodies.Short(labels)), [.. labels]);
        }
        string source = string.Join(", ", origins.Select(o => o switch { "import:scad" => "SCAD", "import:lira" => "ЛИРЫ", _ => o }));
        Row("fragment_rigid_body_points", points,
            $"АЖТ {source} перенесены по узлам схемы: {{0}} ({{1}}) — тела на узлах сетки появятся после построения сетки.");
        Row("fragment_rigid_body_slaves_dropped", partial, "АЖТ с ведомыми узлами вне КонЭ фрагмента — эти узлы отброшены: {0} ({1}).");
        Row("fragment_rigid_body_lost", masterOutside, "АЖТ не перенесены — ведущий узел вне КонЭ фрагмента: {0} ({1}).");
        Row("fragment_rigid_body_lost", slavesOutside, "АЖТ не перенесены — все ведомые узлы вне КонЭ фрагмента: {0} ({1}).");
        Row("fragment_rigid_body_lost", manual, "АЖТ заданы вручную — во фрагмент не переносятся: {0} ({1}).");
        Row("fragment_rigid_body_degenerate", degenerate, "АЖТ у колонн, узлы тела в плите не образуют площадки — след по сечению: {0} ({1}).");
    }

    /// <summary>Наибольшее расхождение сетки источника с КонЭ фрагмента, м, при котором точка АЖТ (проекцией) и
    /// нагрузка КЭ стержня относятся к оси стержня или плоскости пластины: импортные КонЭ расходятся с узлами сетки
    /// источника на миллиметры (у «Музея» ось колонны наклонена на 2,6 мм на 13,5 м).</summary>
    public const double SourceSnapM = 0.01;

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
                // Теги узлов фрагмента — номера по порядку создания (FragmentNodeTable).
                var na = c.Draft.Nodes[ids[0] - 1];
                var nb = c.Draft.Nodes[ids[^1] - 1];
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
}
