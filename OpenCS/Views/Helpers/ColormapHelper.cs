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

        public static Color GetDiscreteColor(double val, double min, double max, bool isRebar, int bands = 8)
        {
            double t = Normalize(val, min, max);
            // Квантование до bands шагов
            t = (Math.Floor(t * bands) + 0.5) / bands;
            t = Math.Clamp(t, 0.0, 1.0);
            return isRebar ? RebarColor(t) : MainColor(t);
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
