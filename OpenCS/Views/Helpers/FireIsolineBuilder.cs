using CSfea.Thermal;
using System.Windows;

namespace OpenCS.Views.Helpers;

/// <summary>Изолинии температуры на T3/T6-сетке (маршинг по рёбрам элементов).</summary>
public static class FireIsolineBuilder
{
    /// <summary>Отрезок изолинии в координатах мм (как на канве).</summary>
    public readonly record struct Segment(Point A, Point B, double LevelCelsius);

    public static IReadOnlyList<Segment> Build(HeatMesh mesh, double[] nodalT, double stepCelsius)
    {
        if (stepCelsius <= 1e-9 || mesh.Elements.Length == 0 || nodalT.Length != mesh.NNodes)
            return [];

        double tMin = nodalT.Min();
        double tMax = nodalT.Max();
        if (tMax - tMin < 1e-9)
            return [];

        var segments = new List<Segment>();
        for (double level = Math.Ceiling(tMin / stepCelsius) * stepCelsius;
             level <= tMax + 1e-6;
             level += stepCelsius)
        {
            foreach (var el in mesh.Elements)
            {
                foreach (var (n0, n1, n2) in FireMeshTriangulation.CornerTriangles(el))
                    TryAddTriangleSegments(mesh, nodalT, n0, n1, n2, level, segments);
            }
        }

        return segments;
    }

    static void TryAddTriangleSegments(
        HeatMesh mesh, double[] t, int n0, int n1, int n2, double level,
        List<Segment> segments)
    {
        var hits = new List<Point>(3);
        TryEdgeCrossing(mesh, t, n0, n1, level, hits);
        TryEdgeCrossing(mesh, t, n1, n2, level, hits);
        TryEdgeCrossing(mesh, t, n2, n0, level, hits);

        if (hits.Count == 2)
            segments.Add(new Segment(hits[0], hits[1], level));
    }

    static void TryEdgeCrossing(HeatMesh mesh, double[] t, int a, int b, double level, List<Point> hits)
    {
        double ta = t[a];
        double tb = t[b];
        if (Math.Abs(ta - tb) < 1e-12)
        {
            if (Math.Abs(ta - level) < 1e-6)
            {
                hits.Add(Mm(mesh.X[a], mesh.Y[a]));
                hits.Add(Mm(mesh.X[b], mesh.Y[b]));
            }
            return;
        }

        if ((ta - level) * (tb - level) > 0)
            return;

        double u = (level - ta) / (tb - ta);
        u = Math.Clamp(u, 0.0, 1.0);
        double x = mesh.X[a] + u * (mesh.X[b] - mesh.X[a]);
        double y = mesh.Y[a] + u * (mesh.Y[b] - mesh.Y[a]);
        hits.Add(Mm(x, y));
    }

    static Point Mm(double xM, double yM) => new(xM * 1000.0, yM * 1000.0);
}
