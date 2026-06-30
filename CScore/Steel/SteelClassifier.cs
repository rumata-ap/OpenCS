using System;
using System.Collections.Generic;
using System.Linq;

namespace CScore;

/// <summary>
/// Классификация элементов сечения по СП 16.13330.2017 (раздел 7.3, таблица 2).
/// </summary>
public static class SteelClassifier
{
    public enum ElementClass { A = 0, B = 1, C = 2 }

    public class ElementClassification
    {
        public string ElementType { get; set; } = "";
        public ElementClass Class { get; set; }
        public double WidthToThickness { get; set; }
        public double Limit { get; set; }
        public double EffectiveArea { get; set; }
        public double Alpha { get; set; } = 1.0;
        public double Beta { get; set; } = 1.0;
    }

    public class Result
    {
        public List<ElementClassification> Elements { get; set; } = [];

        /// <summary>Худший класс сечения (максимум).</summary>
        public ElementClass SectionClass =>
            Elements.Count > 0 ? Elements.Max(e => e.Class) : ElementClass.A;

        public double EffectiveArea => Elements.Sum(e => e.EffectiveArea);
        public double AlphaX { get; set; } = 1.0;
        public double BetaX { get; set; } = 1.0;
        public double AlphaY { get; set; } = 1.0;
        public double BetaY { get; set; } = 1.0;
    }

    /// <summary>
    /// Относительная деформация текучести: ε̄ = √(235/fy).
    /// fy — расчётное сопротивление в МПа.
    /// </summary>
    public static double EpsilonBar(double fyMpa) => Math.Sqrt(235.0 / fyMpa);

    /// <summary>
    /// Классификация внутреннего элемента под сжатием (стенка двутавра).
    /// Таблица 2, СП 16.13330.2017.
    /// Пределы: A=33ε̄, B=38ε̄, C=42ε̄.
    /// </summary>
    public static ElementClassification ClassifyWeb(
        double d, double tw, double fyMpa)
    {
        var epsBar = EpsilonBar(fyMpa);
        double ratio = d / tw;
        double limitA = 33 * epsBar;
        double limitB = 38 * epsBar;
        double limitC = 42 * epsBar;

        var cls = ratio <= limitA ? ElementClass.A :
                  ratio <= limitB ? ElementClass.B :
                  ratio <= limitC ? ElementClass.C : ElementClass.C;

        return new ElementClassification
        {
            ElementType = "Стенка (внутренний элемент)",
            Class = cls,
            WidthToThickness = ratio,
            Limit = limitC,
            EffectiveArea = cls == ElementClass.C ? d * tw * 0.5 : d * tw
        };
    }

    /// <summary>
    /// Классификация выступающего элемента под сжатием (полка двутавра).
    /// Таблица 2, СП 16.13330.2017.
    /// Пределы: A=9ε̄, B=10ε̄, C=14ε̄.
    /// </summary>
    public static ElementClassification ClassifyFlange(
        double bf, double tf, double fyMpa)
    {
        var epsBar = EpsilonBar(fyMpa);
        double ratio = bf / tf;
        double limitA = 9 * epsBar;
        double limitB = 10 * epsBar;
        double limitC = 14 * epsBar;

        var cls = ratio <= limitA ? ElementClass.A :
                  ratio <= limitB ? ElementClass.B :
                  ratio <= limitC ? ElementClass.C : ElementClass.C;

        return new ElementClassification
        {
            ElementType = "Полка (выступающий элемент)",
            Class = cls,
            WidthToThickness = ratio,
            Limit = limitC,
            EffectiveArea = cls == ElementClass.C ? bf * tf * 0.5 : bf * tf
        };
    }

    /// <summary>
    /// Классификация выступающего элемента под растяжением.
    /// Таблица 2, СП 16.13330.2017 (примечание 2).
    /// </summary>
    public static ElementClassification ClassifyFlangeTension(
        double bf, double tf, double fyMpa)
    {
        var epsBar = EpsilonBar(fyMpa);
        double ratio = bf / tf;
        double limitA = 9 * epsBar;
        double limitB = 10 * epsBar;
        double limitC = 14 * epsBar;

        var cls = ratio <= limitA ? ElementClass.A :
                  ratio <= limitB ? ElementClass.B :
                  ratio <= limitC ? ElementClass.C : ElementClass.C;

        return new ElementClassification
        {
            ElementType = "Полка (растяжение)",
            Class = cls,
            WidthToThickness = ratio,
            Limit = limitC,
            EffectiveArea = bf * tf
        };
    }

    /// <summary>
    /// Классификация круглого полого сечения.
    /// Таблица 2, СП 16.13330.2017 (примечание 4).
    /// Пределы: A=50ε̄², B=70ε̄², C=90ε̄².
    /// </summary>
    public static ElementClassification ClassifyCircularHollow(
        double D, double t, double fyMpa)
    {
        var epsBar = EpsilonBar(fyMpa);
        double ratio = D / t;
        double limitA = 50 * epsBar * epsBar;
        double limitB = 70 * epsBar * epsBar;
        double limitC = 90 * epsBar * epsBar;

        var cls = ratio <= limitA ? ElementClass.A :
                  ratio <= limitB ? ElementClass.B :
                  ratio <= limitC ? ElementClass.C : ElementClass.C;

        return new ElementClassification
        {
            ElementType = "Круглое полое сечение",
            Class = cls,
            WidthToThickness = ratio,
            Limit = limitC,
            EffectiveArea = cls == ElementClass.C ? 0.0 : Math.PI * D * t
        };
    }

    /// <summary>
    /// Классификация прямоугольного полого сечения (RHS).
    /// Таблица 2, СП 16.13330.2017.
    /// Пределы: A=33ε̄, B=38ε̄, C=42ε̄.
    /// </summary>
    public static ElementClassification ClassifyRectangularHollow(
        double b, double t, double fyMpa)
    {
        var epsBar = EpsilonBar(fyMpa);
        double ratio = b / t;
        double limitA = 33 * epsBar;
        double limitB = 38 * epsBar;
        double limitC = 42 * epsBar;

        var cls = ratio <= limitA ? ElementClass.A :
                  ratio <= limitB ? ElementClass.B :
                  ratio <= limitC ? ElementClass.C : ElementClass.C;

        return new ElementClassification
        {
            ElementType = "Прямоугольное полое сечение",
            Class = cls,
            WidthToThickness = ratio,
            Limit = limitC,
            EffectiveArea = cls == ElementClass.C ? 0.0 : b * t
        };
    }

    /// <summary>
    /// Классификация сечения (таблица 2).
    /// Анализирует контур: выделяет стенку и полки, вычисляет b/t и d/tw.
    /// </summary>
    public static Result Classify(SteelSection section)
    {
        var result = new Result();
        var fyMpa = (section.Steel.C?.Ry ?? 235e6) / 1e6; // Па → МПа

        var pts = section.OuterContour;
        if (pts.Count < 4)
        {
            // Круглое сечение — класс A
            result.Elements.Add(new ElementClassification
            {
                ElementType = "Сечение",
                Class = ElementClass.A,
                EffectiveArea = section.Area
            });
            return result;
        }

        // Экстремумы контура
        double yMin = double.MaxValue, yMax = double.MinValue;
        double xMin = double.MaxValue, xMax = double.MinValue;
        foreach (var (x, y) in pts)
        {
            if (y < yMin) yMin = y;
            if (y > yMax) yMax = y;
            if (x < xMin) xMin = x;
            if (x > xMax) xMax = x;
        }

        double h = yMax - yMin;
        double b = xMax - xMin;
        if (h < 1e-10 || b < 1e-10)
        {
            result.Elements.Add(new ElementClassification
            {
                ElementType = "Сечение",
                Class = ElementClass.A,
                EffectiveArea = section.Area
            });
            return result;
        }

        // Определяем толщину стенки: находим минимальную ширину по X в средней зоне
        double midY = (yMin + yMax) / 2;
        double bandH = h * 0.25; // средняя зона 25%
        double xMinMid = double.MaxValue, xMaxMid = double.MinValue;
        foreach (var (x, y) in pts)
        {
            if (Math.Abs(y - midY) < bandH)
            {
                if (x < xMinMid) xMinMid = x;
                if (x > xMaxMid) xMaxMid = x;
            }
        }
        double tw = xMaxMid > xMinMid + 1e-10 ? xMaxMid - xMinMid : b * 0.1;

        // Определяем толщину полки: находим минимальную высоту по Y в крайних зонах
        double bandEdge = h * 0.15; // крайние зоны 15%
        double yMinTop = double.MaxValue, yMaxTop = double.MinValue;
        double yMinBot = double.MaxValue, yMaxBot = double.MinValue;
        foreach (var (x, y) in pts)
        {
            if (Math.Abs(y - yMax) < bandEdge)
            {
                if (y < yMinTop) yMinTop = y;
                if (y > yMaxTop) yMaxTop = y;
            }
            if (Math.Abs(y - yMin) < bandEdge)
            {
                if (y < yMinBot) yMinBot = y;
                if (y > yMaxBot) yMaxBot = y;
            }
        }
        double tfTop = yMaxTop > yMinTop + 1e-10 ? yMaxTop - yMinTop : h * 0.1;
        double tfBot = yMaxBot > yMinBot + 1e-10 ? yMaxBot - yMinBot : h * 0.1;
        double tf = Math.Max(tfTop, tfBot);

        // Высота стенки (между полками)
        double d = h - tfTop - tfBot;
        if (d < 1e-10) d = h - 2 * tf;

        // Ширина полки (выступающая часть)
        double bf = (b - tw) / 2; // односторонняя ширина выступа полки

        // Классификация стенки и полки через отдельные методы
        result.Elements.Add(ClassifyWeb(d, tw, fyMpa));
        result.Elements.Add(ClassifyFlange(bf, tf, fyMpa));

        return result;
    }
}
