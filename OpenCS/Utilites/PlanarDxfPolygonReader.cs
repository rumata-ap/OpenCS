using CScore.Planar;
using netDxf;

namespace OpenCS.Utilites;

/// <summary>Причина отклонения кандидата-полигона при DXF-импорте геометрии/зон армирования.</summary>
public enum PlanarDxfRejectReason { None, NotClosed, DegenerateArea, SelfIntersecting }

/// <summary>Полигон-кандидат, извлечённый из закрытой LWPOLYLINE/POLYLINE. Координаты — открытый
/// цикл (без дублирующей замыкающей вершины), уже отмасштабированы под выбранные единицы.</summary>
public sealed record PlanarDxfPolygonCandidate(string Layer, double[] X, double[] Y, PlanarDxfRejectReason RejectReason)
{
    public bool IsAccepted => RejectReason == PlanarDxfRejectReason.None;

    public string StatusText => RejectReason switch
    {
        PlanarDxfRejectReason.None => Loc.S("PlanarDxfAccepted"),
        PlanarDxfRejectReason.NotClosed => Loc.S("PlanarDxfRejectNotClosed"),
        PlanarDxfRejectReason.DegenerateArea => Loc.S("PlanarDxfRejectDegenerateArea"),
        PlanarDxfRejectReason.SelfIntersecting => Loc.S("PlanarDxfRejectSelfIntersecting"),
        _ => "",
    };
}

/// <summary>Читает закрытые LWPOLYLINE/POLYLINE (netDxf: DxfDocument.Entities.Polylines2D) и
/// валидирует их для использования как Hull/Hole региона или полигона зоны армирования.
/// Отдельные Arc/Line-сущности и bulge-дуги внутри полилинии не обрабатываются (вне объёма v1,
/// как и в существующем FromDxfVM). Вся геометрическая валидация переиспользует
/// CScore.Planar.PlanarRegionTopologyValidator — новой геометрии здесь не пишется.</summary>
public static class PlanarDxfPolygonReader
{
    public static IReadOnlyList<PlanarDxfPolygonCandidate> Read(DxfDocument dxf, double scale)
    {
        var result = new List<PlanarDxfPolygonCandidate>();

        foreach (var pline in dxf.Entities.Polylines2D)
        {
            var verts = pline.Vertexes;
            var xs = verts.Select(v => v.Position.X * scale).ToArray();
            var ys = verts.Select(v => v.Position.Y * scale).ToArray();

            bool closed = pline.IsClosed ||
                (verts.Count >= 2 && verts.First().Position.Equals(verts.Last().Position, 1e-4));
            if (!closed)
            {
                result.Add(new PlanarDxfPolygonCandidate(pline.Layer.Name, xs, ys, PlanarDxfRejectReason.NotClosed));
                continue;
            }

            var (ox, oy) = PlanarRegionTopologyValidator.ToOpenLoop(xs, ys);

            if (Math.Abs(PlanarRegionTopologyValidator.SignedArea(ox, oy)) < PlanarRegionTopologyValidator.MinSignedArea)
            {
                result.Add(new PlanarDxfPolygonCandidate(pline.Layer.Name, ox, oy, PlanarDxfRejectReason.DegenerateArea));
                continue;
            }

            if (PlanarRegionTopologyValidator.HasSelfIntersection(ox, oy))
            {
                result.Add(new PlanarDxfPolygonCandidate(pline.Layer.Name, ox, oy, PlanarDxfRejectReason.SelfIntersecting));
                continue;
            }

            result.Add(new PlanarDxfPolygonCandidate(pline.Layer.Name, ox, oy, PlanarDxfRejectReason.None));
        }

        return result;
    }
}
