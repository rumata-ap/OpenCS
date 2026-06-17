using System;
using System.Windows.Media;

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
        static readonly Color s_red   = Color.FromRgb(220,  20,  20);
        static readonly Color s_blue  = Color.FromRgb( 20,  60, 220);
        static readonly Color s_white = Colors.White;

        static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));

        // 0 → белый; отрицательные → красный (осн.) / синий (арм.);
        // положительные → синий (осн.) / красный (арм.)
        static Color DivergingColor(double val, double min, double max, bool isRebar)
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

        public static Color GetColor(double val, double min, double max, bool isRebar)
            => DivergingColor(val, min, max, isRebar);

        public static SolidColorBrush GetBrush(double val, double min, double max, bool isRebar)
            => new(GetColor(val, min, max, isRebar));

        public static Color GetDiscreteColor(double val, double min, double max, bool isRebar, int bands = 8)
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

        /// <summary>Холодный синий → жёлтый → красный (для поля температуры).</summary>
        public static Color ThermalColor(double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);
            if (t <= 0.5)
            {
                double u = t * 2.0;
                return Color.FromRgb(0, (byte)(u * 200), (byte)(180 + u * 75));
            }
            double v = (t - 0.5) * 2.0;
            return Color.FromRgb((byte)(v * 255), (byte)((1 - v) * 220), 0);
        }

        public static Color GetThermalDiscreteColor(double val, double min, double max, int bands = 8)
        {
            double t = Normalize(val, min, max);
            t = (Math.Floor(t * bands) + 0.5) / bands;
            return ThermalColor(Math.Clamp(t, 0.0, 1.0));
        }
    }
}
