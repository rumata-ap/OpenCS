using CScore;
using netDxf;
using OpenCS.Utilites;

namespace OpenCS.Services.Cad;

/// <summary>Итог импорта геометрии в пул: добавлено, пропущено (не замкнуто, самопересечение, нулевой радиус) и
/// уже было в пуле (повторный импорт того же файла).</summary>
public sealed record GeometryImportResult(int Added, int Skipped, int Duplicates);

/// <summary>
/// Импорт пула геометрии сечений — контуров (замкнутые полилинии DXF) и кругов (окружности DXF, CSV «Tag;X;Y;Radius»).
/// Общая служба меню «Импорт» и консоли (<c>import contours</c>, <c>import circles</c>). Повторный импорт не создаёт
/// дубликатов: контур или круг того же набора геометрии с теми же координатами (±1 мкм) пропускается.
/// </summary>
public static class DxfGeometryImport
{
    /// <summary>Допуск совпадения координат при поиске дубликатов, м.</summary>
    public const double DuplicateTolerance = 1e-6;

    /// <summary>Замкнутые 2D-полилинии DXF → контуры набора <paramref name="geometrySet"/>.</summary>
    /// <param name="metersPerUnit">Метров в единице чертежа.</param>
    public static GeometryImportResult Contours(DatabaseService db, DxfDocument dxf, double metersPerUnit, string geometrySet)
    {
        int added = 0, skipped = 0, duplicates = 0;
        foreach (var pline in dxf.Entities.Polylines2D)
        {
            var verts = pline.Vertexes;
            if (verts.Count < 2) continue;

            bool firstEqualsLast =
                Math.Abs(verts[0].Position.X - verts[^1].Position.X) < 1e-4 &&
                Math.Abs(verts[0].Position.Y - verts[^1].Position.Y) < 1e-4;
            if (!pline.IsClosed && !firstEqualsLast) { skipped++; continue; }

            var pts = new List<StressPoint>();
            int j = 1;
            foreach (var v in verts)
                pts.Add(new StressPoint(v.Position.X * metersPerUnit, v.Position.Y * metersPerUnit) { Num = j++ });
            // Флаг замкнутости без совпадающих вершин — добавляем замыкающую точку.
            if (pline.IsClosed && !firstEqualsLast)
                pts.Add(new StressPoint(verts[0].Position.X * metersPerUnit, verts[0].Position.Y * metersPerUnit) { Num = j });
            if (pts.Count < 4) { skipped++; continue; }

            var contour = new Contour(pts, pline.Layer.Name) { GeometrySet = geometrySet };
            if (string.IsNullOrWhiteSpace(contour.Tag)) contour.Tag = Loc.S("Contour");
            if (db.Contours.Any(c => SameContour(c, contour))) { duplicates++; continue; }
            if (ContourAuthoring.Save(db, contour).Count > 0) { skipped++; continue; } // самопересечение, нулевая площадь
            added++;
        }
        return new(added, skipped, duplicates);
    }

    /// <summary>Окружности DXF → круги набора <paramref name="geometrySet"/> (обозначение — слой).</summary>
    public static GeometryImportResult Circles(DatabaseService db, DxfDocument dxf, double metersPerUnit, string geometrySet) =>
        Circles(db, dxf.Entities.Circles.Select(c => new CircleP(c.Center.X * metersPerUnit, c.Center.Y * metersPerUnit,
            c.Radius * metersPerUnit) { Tag = c.Layer.Name, GeometrySet = geometrySet }), skippedBefore: 0);

    /// <summary>Готовые круги (CSV) → пул; <paramref name="skippedBefore"/> — отброшенные разбором строки.</summary>
    public static GeometryImportResult Circles(DatabaseService db, IEnumerable<CircleP> circles, int skippedBefore)
    {
        int added = 0, skipped = skippedBefore, duplicates = 0;
        foreach (var cp in circles)
        {
            if (db.Circles.Any(c => SameCircle(c, cp))) { duplicates++; continue; }
            if (ContourAuthoring.SaveCircle(db, cp).Count == 0) added++; else skipped++;
        }
        return new(added, skipped, duplicates);
    }

    static bool SameCircle(CircleP a, CircleP b) =>
        a.GeometrySet == b.GeometrySet && Near(a.X, b.X) && Near(a.Y, b.Y) && Near(a.Radius, b.Radius);

    static bool SameContour(Contour a, Contour b)
    {
        if (a.GeometrySet != b.GeometrySet || a.Points.Count == 0) return false;
        var pa = OpenRing(a.Points);
        var pb = OpenRing(b.Points);
        if (pa.Count != pb.Count) return false;
        for (int i = 0; i < pa.Count; i++)
            if (!Near(pa[i].X, pb[i].X) || !Near(pa[i].Y, pb[i].Y)) return false;
        return true;
    }

    /// <summary>Вершины без замыкающей точки (у сохранённого и нового контура она может отличаться).</summary>
    static List<StressPoint> OpenRing(IList<StressPoint> pts)
    {
        var list = pts.ToList();
        if (list.Count > 1 && Near(list[0].X, list[^1].X) && Near(list[0].Y, list[^1].Y)) list.RemoveAt(list.Count - 1);
        return list;
    }

    static bool Near(double a, double b) => Math.Abs(a - b) <= DuplicateTolerance;
}
