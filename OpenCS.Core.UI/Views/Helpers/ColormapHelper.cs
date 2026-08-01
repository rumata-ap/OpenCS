using OpenCS.Utilites;

namespace OpenCS.Views.Helpers
{
    /// <summary>
    /// Утилиты цветовых карт для графиков напряжений/деформаций.
    /// Дивергирующая карта с белым в нуле:
    /// Основной материал: сжатие (−) → красный, растяжение (+) → синий.
    /// Арматура: сжатие (−) → синий, растяжение (+) → красный.
    /// </summary>
    public static class ColormapHelper
    {
        static readonly Argb s_red   = Argb.FromRgb(220,  20,  20);
        static readonly Argb s_blue  = Argb.FromRgb( 20,  60, 220);
        static readonly Argb s_white = Argb.FromRgb(255, 255, 255);

        static Argb Lerp(Argb a, Argb b, double t) => Argb.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));

        public static Argb LerpColor(Argb a, Argb b, double t) => Lerp(a, b, t);

        // 0 → белый; отрицательные → красный (осн.) / синий (арм.);
        // положительные → синий (осн.) / красный (арм.)
        static Argb DivergingColor(double val, double min, double max, bool isRebar)
        {
            if (val <= 0.0)
            {
                double t = (min < -1e-10) ? Math.Clamp(val / min, 0.0, 1.0) : 0.0;
                return isRebar ? Lerp(s_white, s_blue, t) : Lerp(s_white, s_red, t);
            }
            else
            {
                double t = (max > 1e-10) ? Math.Clamp(val / max, 0.0, 1.0) : 0.0;
                return isRebar ? Lerp(s_white, s_red, t) : Lerp(s_white, s_blue, t);
            }
        }

        public static Argb GetColor(double val, double min, double max, bool isRebar)
            => DivergingColor(val, min, max, isRebar);

        public static string GetColorHex(double val, double min, double max, bool isRebar)
            => GetColor(val, min, max, isRebar).ToHex();

        public static Argb GetDiscreteColor(double val, double min, double max, bool isRebar, int bands = 8)
        {
            double range = max - min;
            if (range < 1e-10) return DivergingColor(val, min, max, isRebar);
            double step = range / bands;
            double center = min + (Math.Floor((val - min) / step) + 0.5) * step;
            return DivergingColor(Math.Clamp(center, min, max), min, max, isRebar);
        }

        public static double Normalize(double val, double min, double max)
        {
            if (Math.Abs(max - min) < 1e-10) return 0.5;
            return Math.Clamp((val - min) / (max - min), 0.0, 1.0);
        }

        /// <summary>Карта температуры как matplotlib <c>hot_r</c> (GreenSectionPy): холод — светлый, жар — тёмный.</summary>
        public static Argb GetThermalColor(double val, double min, double max)
        {
            double range = max - min;
            double margin = range > 1e-9 ? range * 0.01 : 1.0;
            return HotReversed(Normalize(val, min - margin, max + margin));
        }

        public static Argb GetThermalDiscreteColor(double val, double min, double max, int bands = 8)
        {
            double range = max - min;
            double margin = range > 1e-9 ? range * 0.01 : 1.0;
            double lo = min - margin;
            double hi = max + margin;
            double t = Normalize(val, lo, hi);
            t = (Math.Floor(t * bands) + 0.5) / bands;
            return HotReversed(Math.Clamp(t, 0.0, 1.0));
        }

        /// <summary><c>hot_r</c>: t=0 → белый (min T), t=1 → чёрный (max T).</summary>
        public static Argb HotReversed(double t)
            => HotMatplotlib(1.0 - Math.Clamp(t, 0.0, 1.0));

        /// <summary>Стандартный matplotlib <c>hot</c>: t=0 → чёрный, t=1 → белый.</summary>
        public static Argb HotMatplotlib(double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);
            double r, g, b;
            if (t < 1.0 / 3.0)
            {
                r = 3.0 * t;
                g = 0.0;
                b = 0.0;
            }
            else if (t < 2.0 / 3.0)
            {
                r = 1.0;
                g = 3.0 * (t - 1.0 / 3.0);
                b = 0.0;
            }
            else
            {
                r = 1.0;
                g = 1.0;
                b = 3.0 * (t - 2.0 / 3.0);
            }

            return Argb.FromRgb(
                (byte)Math.Round(r * 255.0),
                (byte)Math.Round(g * 255.0),
                (byte)Math.Round(b * 255.0));
        }
    }
}
