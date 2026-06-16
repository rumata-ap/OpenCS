using System;
using System.Windows.Media;

namespace OpenCS.Views.Helpers
{
    /// <summary>
    /// Утилиты цветовых карт для графиков напряжений/деформаций.
    /// Основной материал: синий→белый→красный.
    /// Арматура: красный→белый→синий (инверсия).
    /// </summary>
    public static class ColormapHelper
    {
        // Синий→белый→красный
        public static Color MainColor(double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);
            if (t <= 0.5)
            {
                double u = t * 2.0;
                return Color.FromRgb((byte)(u * 255), (byte)(u * 255), 255);
            }
            else
            {
                double u = (t - 0.5) * 2.0;
                return Color.FromRgb(255, (byte)((1 - u) * 255), (byte)((1 - u) * 255));
            }
        }

        // Красный→белый→синий (инверсия для арматуры)
        public static Color RebarColor(double t) => MainColor(1.0 - t);

        public static double Normalize(double val, double min, double max)
        {
            if (Math.Abs(max - min) < 1e-10) return 0.5;
            return Math.Clamp((val - min) / (max - min), 0.0, 1.0);
        }

        public static Color GetColor(double val, double min, double max, bool isRebar)
        {
            double t = Normalize(val, min, max);
            return isRebar ? RebarColor(t) : MainColor(t);
        }

        public static SolidColorBrush GetBrush(double val, double min, double max, bool isRebar)
            => new(GetColor(val, min, max, isRebar));
    }
}
