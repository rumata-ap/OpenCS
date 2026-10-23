using System.Text.Json;
using CScore.Planar;

namespace CScore.Fem.Fragment;

public static partial class StoreyFragmentBuilder
{
    /// <summary>
    /// Импортные АЖТ ЛИРЫ/SCAD у колонн → явный след стержней фрагмента в узле ведущего (выпуклая оболочка узлов тела
    /// в плоскости плиты; спека A: импортное АЖТ важнее следа по сечению). Авто-АЖТ родителя пересоздаются фрагментом;
    /// прочие — «не перенесено».
    /// </summary>
    static void TransferRigidBodies(Ctx c, StoreyFragmentParentSnapshot parent)
    {
        var meshNode = parent.MeshNodes.GroupBy(n => n.NodeTag).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var body in parent.RigidBodies)
        {
            if (!meshNode.TryGetValue(body.MasterNodeTag, out var master)) continue;
            var m = new PlanarVector3(master.X, master.Y, master.Z);
            if (!c.InBand(m.Z) || body.Origin == RigidJunctionTraceSet.Provenance) continue;
            string label = body.SourceElemTag ?? body.MasterNodeTag;
            if (!body.Origin.StartsWith("import:", StringComparison.Ordinal))
            {
                c.Warn("fragment_rigid_body_lost", $"АЖТ {label} задано вручную — во фрагмент не переносится.", label);
                continue;
            }
            var slab = c.Slabs.FirstOrDefault(sl => Math.Abs(sl.Z - m.Z) <= c.Tol && RegionContains(sl.Region, m, c.Tol));
            var node = slab is null ? null : c.Nodes.Find(m);
            var bars = node is null ? [] : c.Draft.Members
                .Where(x => x.Member.ElemType == "beam" && NodeTags(x.Member).Contains(int.Parse(node.NodeTag)))
                .ToList();
            if (bars.Count == 0)
            {
                c.Warn("fragment_rigid_body_lost", $"АЖТ {label}: ведущий узел не у стержня фрагмента в плите — не переносится.", label);
                continue;
            }
            var points = body.SlaveNodeTags.Append(body.MasterNodeTag)
                .Select(t => meshNode.GetValueOrDefault(t))
                .Where(n => n is not null && Math.Abs(n.Z - slab!.Z) <= c.Tol)
                .Select(n => new PlanarPoint2D(n!.X, n.Y));
            var hull = PlanarPolygonOps.ConvexHull(points);
            if (hull.Count < 3 || Math.Abs(PlanarPolygonOps.SignedArea(hull)) < c.Tol * c.Tol)
            {
                c.Warn("fragment_rigid_body_degenerate", $"АЖТ {label}: узлы тела в плите не образуют площадки — след по сечению.", label);
                continue;
            }
            string json = JsonSerializer.Serialize(hull.Select(p => new[] { p.U, p.V, slab!.Z }));
            foreach (var bar in bars) bar.Member.RigidJunctionTraceJson = json;
        }
    }
}
