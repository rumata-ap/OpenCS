using System.Windows;
using System.Windows.Media;

namespace OpenCS.Views.Helpers;

/// <summary>Отрисовка сглаженной T3-карты температуры (барицентрическая подсетка).</summary>
public static class FireThermalMeshRenderer
{
    const int Subdivisions = 6;

    public static void DrawSmoothTriangle(
        DrawingContext dc,
        ReadOnlySpan<Point> verts,
        ReadOnlySpan<double> nodalValues,
        double vmin,
        double vmax,
        Func<Point, Point> toScreen,
        Pen edgePen)
    {
        if (verts.Length < 3 || nodalValues.Length < 3) return;

        var p0 = verts[0];
        var p1 = verts[1];
        var p2 = verts[2];
        double t0 = nodalValues[0];
        double t1 = nodalValues[1];
        double t2 = nodalValues[2];

        int n = Subdivisions;
        var gridP = new Point[(n + 1) * (n + 1)];
        var gridC = new Color[(n + 1) * (n + 1)];

        int Idx(int i, int j) => i * (n + 1) + j;

        for (int i = 0; i <= n; i++)
        {
            double l1 = i / (double)n;
            for (int j = 0; j <= n - i; j++)
            {
                double l2 = j / (double)n;
                double l3 = 1.0 - l1 - l2;
                gridP[Idx(i, j)] = new Point(
                    l1 * p0.X + l2 * p1.X + l3 * p2.X,
                    l1 * p0.Y + l2 * p1.Y + l3 * p2.Y);
                double tv = l1 * t0 + l2 * t1 + l3 * t2;
                gridC[Idx(i, j)] = ColormapHelper.GetThermalColor(tv, vmin, vmax);
            }
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n - i; j++)
            {
                DrawMicroTri(dc, toScreen, edgePen,
                    gridP[Idx(i, j)], gridC[Idx(i, j)],
                    gridP[Idx(i + 1, j)], gridC[Idx(i + 1, j)],
                    gridP[Idx(i, j + 1)], gridC[Idx(i, j + 1)]);

                if (j < n - i - 1)
                {
                    DrawMicroTri(dc, toScreen, edgePen,
                        gridP[Idx(i + 1, j)], gridC[Idx(i + 1, j)],
                        gridP[Idx(i + 1, j + 1)], gridC[Idx(i + 1, j + 1)],
                        gridP[Idx(i, j + 1)], gridC[Idx(i, j + 1)]);
                }
            }
        }
    }

    static void DrawMicroTri(
        DrawingContext dc, Func<Point, Point> toScreen, Pen edgePen,
        Point p0, Color c0, Point p1, Color c1, Point p2, Color c2)
    {
        double cx = (p0.X + p1.X + p2.X) / 3.0;
        double cy = (p0.Y + p1.Y + p2.Y) / 3.0;
        var cc = ColormapHelper.LerpColor(
            ColormapHelper.LerpColor(c0, c1, 0.5),
            c2,
            1.0 / 3.0);
        var brush = new SolidColorBrush(cc);
        var g = new StreamGeometry();
        using var ctx = g.Open();
        ctx.BeginFigure(toScreen(p0), true, true);
        ctx.LineTo(toScreen(p1), true, false);
        ctx.LineTo(toScreen(p2), true, false);
        dc.DrawGeometry(brush, edgePen, g);
    }
}
