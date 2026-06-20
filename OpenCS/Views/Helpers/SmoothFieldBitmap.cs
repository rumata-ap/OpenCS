using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenCS.Views.Helpers;

public static class SmoothFieldBitmap
{
    const int MaxEdgePx = 1600;

    public readonly record struct TriVal(Point A, Point B, Point C, double Va, double Vb, double Vc);

    public sealed class RasterPixels
    {
        public byte[] Pixels { get; init; } = [];
        public int Width { get; init; }
        public int Height { get; init; }
        public double XMinMm { get; init; }
        public double YMinMm { get; init; }
        public double XMaxMm { get; init; }
        public double YMaxMm { get; init; }

        public RasterResult Freeze()
        {
            var bs = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, Pixels, Width * 4);
            bs.Freeze();
            return new RasterResult
            {
                Bitmap = bs,
                XMinMm = XMinMm,
                YMinMm = YMinMm,
                XMaxMm = XMaxMm,
                YMaxMm = YMaxMm
            };
        }
    }

    public sealed class RasterResult
    {
        public BitmapSource Bitmap { get; init; } = null!;
        public double XMinMm { get; init; }
        public double YMinMm { get; init; }
        public double XMaxMm { get; init; }
        public double YMaxMm { get; init; }
    }

    public static RasterPixels? Build(
        List<TriVal> tris,
        double vmin,
        double vmax,
        Func<double, double, double, Color> colorFn)
    {
        if (tris.Count == 0) return null;

        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;
        for (int ti = 0; ti < tris.Count; ti++)
        {
            var t = tris[ti];
            Expand(t.A, ref xMin, ref xMax, ref yMin, ref yMax);
            Expand(t.B, ref xMin, ref xMax, ref yMin, ref yMax);
            Expand(t.C, ref xMin, ref xMax, ref yMin, ref yMax);
        }

        double wMm = xMax - xMin;
        double hMm = yMax - yMin;
        if (wMm < 1e-6) wMm = 1;
        if (hMm < 1e-6) hMm = 1;

        double mpp = Math.Max(wMm, hMm) / MaxEdgePx;
        int w = Math.Max(1, (int)Math.Ceiling(wMm / mpp));
        int h = Math.Max(1, (int)Math.Ceiling(hMm / mpp));

        var pixels = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            pixels[i * 4] = 255;
            pixels[i * 4 + 1] = 255;
            pixels[i * 4 + 2] = 255;
            pixels[i * 4 + 3] = 255;
        }

        for (int ti = 0; ti < tris.Count; ti++)
        {
            var t = tris[ti];
            RasterTriangle(pixels, w, h,
                xMin, yMax, mpp, vmin, vmax, colorFn,
                t.A.X, t.A.Y, t.Va,
                t.B.X, t.B.Y, t.Vb,
                t.C.X, t.C.Y, t.Vc);
        }

        return new RasterPixels
        {
            Pixels = pixels,
            Width = w,
            Height = h,
            XMinMm = xMin,
            YMinMm = yMin,
            XMaxMm = xMax,
            YMaxMm = yMax
        };
    }

    static void Expand(Point p, ref double xMin, ref double xMax, ref double yMin, ref double yMax)
    {
        if (p.X < xMin) xMin = p.X; if (p.X > xMax) xMax = p.X;
        if (p.Y < yMin) yMin = p.Y; if (p.Y > yMax) yMax = p.Y;
    }

    static void RasterTriangle(
        byte[] px, int bw, int bh,
        double xMin, double yMax, double mpp,
        double vmin, double vmax,
        Func<double, double, double, Color> colorFn,
        double x0, double y0, double t0,
        double x1, double y1, double t1,
        double x2, double y2, double t2)
    {
        double area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
        if (Math.Abs(area) < 1e-18) return;
        if (area < 0)
        {
            (x1, x2) = (x2, x1);
            (y1, y2) = (y2, y1);
            (t1, t2) = (t2, t1);
            area = -area;
        }

        double invArea = 1.0 / area;

        int ix0 = (int)Math.Floor((x0 - xMin) / mpp);
        int ix1 = (int)Math.Floor((x1 - xMin) / mpp);
        int ix2 = (int)Math.Floor((x2 - xMin) / mpp);
        int iy0 = (int)Math.Floor((yMax - y0) / mpp);
        int iy1 = (int)Math.Floor((yMax - y1) / mpp);
        int iy2 = (int)Math.Floor((yMax - y2) / mpp);

        int iMin = Math.Clamp(Math.Min(ix0, Math.Min(ix1, ix2)), 0, bw - 1);
        int iMax = Math.Clamp(Math.Max(ix0, Math.Max(ix1, ix2)), 0, bw - 1);
        int jMin = Math.Clamp(Math.Min(iy0, Math.Min(iy1, iy2)), 0, bh - 1);
        int jMax = Math.Clamp(Math.Max(iy0, Math.Max(iy1, iy2)), 0, bh - 1);

        double dx1 = x2 - x1;
        double dy1 = y2 - y1;
        double dx2 = x0 - x2;
        double dy2 = y0 - y2;

        for (int j = jMin; j <= jMax; j++)
        {
            double my = yMax - (j + 0.5) * mpp;
            int rowBase = j * bw * 4;
            for (int i = iMin; i <= iMax; i++)
            {
                double mx = xMin + (i + 0.5) * mpp;

                double w0 = (dx1 * (my - y1) - dy1 * (mx - x1)) / area;
                double w1 = (dx2 * (my - y2) - dy2 * (mx - x2)) / area;
                const double eps = -1e-9;
                if (w0 < eps || w1 < eps) continue;
                double w2 = 1.0 - w0 - w1;
                if (w2 < eps) continue;

                double tv = w0 * t0 + w1 * t1 + w2 * t2;
                var c = colorFn(tv, vmin, vmax);
                int o = rowBase + i * 4;
                px[o] = c.B;
                px[o + 1] = c.G;
                px[o + 2] = c.R;
                px[o + 3] = 255;
            }
        }
    }
}
