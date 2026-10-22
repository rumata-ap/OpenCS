using CScore.Planar;

namespace CScore.Fem;

/// <summary>
/// Контур сечения стержня для следа АЖТ: выпуклая оболочка внешних контуров бетонных областей в осях стержня
/// (Y — ось y сечения, Z — ось x, как у 3D-глифа сечения), отсчитанная от геометрического ц. т. этих областей —
/// ось стержня в расчёте проходит через ц. т. сечения.
/// </summary>
public static class RigidJunctionSectionOutline
{
    /// <summary>Вершины оболочки (Y, Z), м, против часовой стрелки; null — у сечения нет бетонных контуров.</summary>
    public static IReadOnlyList<(double Y, double Z)>? From(CrossSection section)
    {
        var areas = section.Areas
            .Where(a => a.Category == AreaCategory.Region && a.HostAreaId == null && a.Hull is { X.Count: >= 3 })
            .ToList();
        if (areas.Count == 0) return null;

        // Точки в осях стержня: U = Y стержня (y сечения), V = Z стержня (x сечения).
        static PlanarPoint2D[] Open(Contour c)
        {
            int n = c.X.Count;
            if (n > 1 && c.X[0] == c.X[n - 1] && c.Y[0] == c.Y[n - 1]) n--;
            return Enumerable.Range(0, n).Select(i => new PlanarPoint2D(c.Y[i], c.X[i])).ToArray();
        }

        double area = 0, su = 0, sv = 0;
        void Add(PlanarPoint2D[] p, double sign)
        {
            double a = Math.Abs(PlanarPolygonOps.SignedArea(p));
            if (a <= 0) return;
            var (cu, cv) = Centroid(p);
            area += sign * a; su += sign * a * cu; sv += sign * a * cv;
        }

        var points = new List<PlanarPoint2D>();
        foreach (var a in areas)
        {
            var hull = Open(a.Hull!);
            points.AddRange(hull);
            Add(hull, 1);
            foreach (var hole in a.Holes) Add(Open(hole), -1);
        }
        if (area <= 0) return null;

        double u0 = su / area, v0 = sv / area;
        return PlanarPolygonOps.ConvexHull(points)
            .Select(q => (q.U - u0, q.V - v0))
            .ToArray();
    }

    static (double U, double V) Centroid(IReadOnlyList<PlanarPoint2D> p)
    {
        double a = 0, cu = 0, cv = 0;
        for (int i = 0; i < p.Count; i++)
        {
            var s = p[i];
            var t = p[(i + 1) % p.Count];
            double cross = s.U * t.V - t.U * s.V;
            a += cross; cu += (s.U + t.U) * cross; cv += (s.V + t.V) * cross;
        }
        return (cu / (3 * a), cv / (3 * a));
    }
}
