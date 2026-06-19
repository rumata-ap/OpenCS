using CSfea.Thermal;
using OpenCS.ViewModels;
using System.Windows;
using System.Windows.Media;

namespace OpenCS.Views.Helpers;

/// <summary>Растеризация поля температуры (matplotlib tricontourf / hot_r).</summary>
public static class FireThermalFieldBitmap
{
    public sealed class RasterResult
    {
        public byte[] Pixels { get; init; } = [];
        public int Width { get; init; }
        public int Height { get; init; }
        public double XMinMm { get; init; }
        public double YMaxMm { get; init; }
        public double XMaxMm { get; init; }
        public double YBottomMm { get; init; }
    }

    const int MaxEdgePx = 1600;
    const double MmPerM = 1000.0;

    public static RasterResult? BuildFromMesh(HeatMesh mesh, double[] nodalT, double vmin, double vmax)
    {
        if (mesh.Elements.Length == 0 || nodalT.Length != mesh.NNodes)
            return null;

        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;
        for (int i = 0; i < mesh.NNodes; i++)
        {
            double x = mesh.X[i] * MmPerM;
            double y = mesh.Y[i] * MmPerM;
            if (x < xMin) xMin = x;
            if (x > xMax) xMax = x;
            if (y < yMin) yMin = y;
            if (y > yMax) yMax = y;
        }

        return RasterBounds(xMin, xMax, yMin, yMax, (px, w, h, x0, yTop, mpp) =>
        {
            foreach (var el in mesh.Elements)
            {
                foreach (var (n0, n1, n2) in FireMeshTriangulation.CornerTriangles(el))
                {
                    RasterTriangleMm(
                        px, w, h, x0, yTop, mpp, vmin, vmax,
                        mesh.X[n0] * MmPerM, mesh.Y[n0] * MmPerM, nodalT[n0],
                        mesh.X[n1] * MmPerM, mesh.Y[n1] * MmPerM, nodalT[n1],
                        mesh.X[n2] * MmPerM, mesh.Y[n2] * MmPerM, nodalT[n2]);
                }
            }
        });
    }

    public static RasterResult? Build(
        IReadOnlyList<FireTriDraw> tris,
        double vmin,
        double vmax,
        bool smooth)
    {
        if (tris.Count == 0) return null;

        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;
        foreach (var t in tris)
        {
            foreach (var p in t.VerticesMm)
            {
                if (p.X < xMin) xMin = p.X;
                if (p.X > xMax) xMax = p.X;
                if (p.Y < yMin) yMin = p.Y;
                if (p.Y > yMax) yMax = p.Y;
            }
        }

        return RasterBounds(xMin, xMax, yMin, yMax, (px, w, h, x0, yTop, mpp) =>
        {
            foreach (var t in tris)
            {
                if (t.VerticesMm.Count < 3) continue;
                double t0 = t.NodeValues.Count > 0 ? t.NodeValues[0] : t.Value;
                double t1 = t.NodeValues.Count > 1 ? t.NodeValues[1] : t.Value;
                double t2 = t.NodeValues.Count > 2 ? t.NodeValues[2] : t.Value;
                if (!smooth)
                    t0 = t1 = t2 = t.Value;

                var p0 = t.VerticesMm[0];
                var p1 = t.VerticesMm[1];
                var p2 = t.VerticesMm[2];
                RasterTriangleMm(px, w, h, x0, yTop, mpp, vmin, vmax,
                    p0.X, p0.Y, t0, p1.X, p1.Y, t1, p2.X, p2.Y, t2);
            }
        });
    }

    static RasterResult? RasterBounds(
        double xMin, double xMax, double yMin, double yMax,
        Action<byte[], int, int, double, double, double> draw)
    {
        double wMm = xMax - xMin;
        double hMm = yMax - yMin;
        if (wMm < 1e-6) wMm = 1;
        if (hMm < 1e-6) hMm = 1;

        double mpp = Math.Max(wMm, hMm) / MaxEdgePx;
        int w = Math.Max(1, (int)Math.Ceiling(wMm / mpp));
        int h = Math.Max(1, (int)Math.Ceiling(hMm / mpp));

        var pixels = new byte[w * h * 4];
        Array.Fill(pixels, (byte)255);
        draw(pixels, w, h, xMin, yMax, mpp);

        return new RasterResult
        {
            Pixels = pixels,
            Width = w,
            Height = h,
            XMinMm = xMin,
            YMaxMm = yMax,
            XMaxMm = xMin + w * mpp,
            YBottomMm = yMax - h * mpp
        };
    }

    static void RasterTriangleMm(
        byte[] px, int bw, int bh,
        double xMin, double yMax, double mpp,
        double vmin, double vmax,
        double x0, double y0, double t0,
        double x1, double y1, double t1,
        double x2, double y2, double t2)
    {
        var p0 = new Point(x0, y0);
        var p1 = new Point(x1, y1);
        var p2 = new Point(x2, y2);

        double area = SignedArea(p0, p1, p2);
        if (Math.Abs(area) < 1e-18) return;
        if (area < 0)
        {
            (p1, p2) = (p2, p1);
            (t1, t2) = (t2, t1);
            area = -area;
        }

        int ix0 = (int)Math.Floor((p0.X - xMin) / mpp);
        int ix1 = (int)Math.Floor((p1.X - xMin) / mpp);
        int ix2 = (int)Math.Floor((p2.X - xMin) / mpp);
        int iy0 = (int)Math.Floor((yMax - p0.Y) / mpp);
        int iy1 = (int)Math.Floor((yMax - p1.Y) / mpp);
        int iy2 = (int)Math.Floor((yMax - p2.Y) / mpp);

        int iMin = Math.Clamp(Math.Min(ix0, Math.Min(ix1, ix2)), 0, bw - 1);
        int iMax = Math.Clamp(Math.Max(ix0, Math.Max(ix1, ix2)), 0, bw - 1);
        int jMin = Math.Clamp(Math.Min(iy0, Math.Min(iy1, iy2)), 0, bh - 1);
        int jMax = Math.Clamp(Math.Max(iy0, Math.Max(iy1, iy2)), 0, bh - 1);

        for (int j = jMin; j <= jMax; j++)
        {
            double my = yMax - (j + 0.5) * mpp;
            for (int i = iMin; i <= iMax; i++)
            {
                double mx = xMin + (i + 0.5) * mpp;
                if (!InsideTri(mx, my, p0, p1, p2, area)) continue;

                double tv = BarycentricT(mx, my, p0, p1, p2, area, t0, t1, t2);
                SetPixel(px, bw, i, j, ColormapHelper.GetThermalColor(tv, vmin, vmax));
            }
        }
    }

    static double SignedArea(Point a, Point b, Point c)
        => (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);

    static bool InsideTri(double x, double y, Point p0, Point p1, Point p2, double area)
    {
        double w0 = ((p1.X - p2.X) * (y - p2.Y) - (p1.Y - p2.Y) * (x - p2.X)) / area;
        double w1 = ((p2.X - p0.X) * (y - p0.Y) - (p2.Y - p0.Y) * (x - p0.X)) / area;
        double w2 = 1.0 - w0 - w1;
        const double eps = -1e-9;
        return w0 >= eps && w1 >= eps && w2 >= eps;
    }

    static double BarycentricT(
        double x, double y, Point p0, Point p1, Point p2, double area,
        double t0, double t1, double t2)
    {
        double w0 = ((p1.X - p2.X) * (y - p2.Y) - (p1.Y - p2.Y) * (x - p2.X)) / area;
        double w1 = ((p2.X - p0.X) * (y - p0.Y) - (p2.Y - p0.Y) * (x - p0.X)) / area;
        double w2 = 1.0 - w0 - w1;
        return w0 * t0 + w1 * t1 + w2 * t2;
    }

    static void SetPixel(byte[] px, int w, int x, int y, Color c)
    {
        int o = (y * w + x) * 4;
        px[o] = c.B;
        px[o + 1] = c.G;
        px[o + 2] = c.R;
        px[o + 3] = 255;
    }
}
