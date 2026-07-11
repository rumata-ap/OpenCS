using System;
using System.Collections.Generic;

namespace CScore;

/// <summary>Стиль кривой эпюры разреза: цвет только по знаку значения.</summary>
public static class SectionCutDiagramStyle
{
    public const double SignEpsilon = 1e-12;

    public static bool CurveIsPositive(double v) => v > SignEpsilon;

    public static (byte R, byte G, byte B) CurveStrokeRgb(double v) =>
        CurveIsPositive(v) ? ((byte)0x00, (byte)0x44, (byte)0xCC) : ((byte)0xCC, (byte)0x00, (byte)0x00);

    /// <summary>
    /// Разбивает полилинию на куски одного знака.
    /// При смене знака между i-1 и i: предыдущий кусок EndExclusive=i+1, следующий Start=i (общая вершина).
    /// </summary>
    public static IReadOnlyList<(int Start, int EndExclusive, bool Positive)> SplitBySign(IReadOnlyList<double> values)
    {
        var result = new List<(int, int, bool)>();
        if (values == null || values.Count == 0) return result;

        int start = 0;
        bool pos = CurveIsPositive(values[0]);
        for (int i = 1; i < values.Count; i++)
        {
            bool p = CurveIsPositive(values[i]);
            if (p != pos)
            {
                result.Add((start, i + 1, pos));
                start = i;
                pos = p;
            }
        }
        result.Add((start, values.Count, pos));
        return result;
    }

    /// <summary>
    /// Кривые для заливки/штриховки по знаку: на смене знака вставляется точка на нуле (линейная интерполяция по s).
    /// <paramref name="s"/> и <paramref name="values"/> — параллельные массивы одной длины.
    /// </summary>
    public static IReadOnlyList<(IReadOnlyList<(double S, double V)> Curve, bool Positive)> BuildSignedFillCurves(
        IReadOnlyList<double> s, IReadOnlyList<double> values)
    {
        var result = new List<(IReadOnlyList<(double S, double V)>, bool)>();
        if (s == null || values == null || s.Count != values.Count || values.Count == 0)
            return result;

        foreach (var part in SplitBySign(values))
        {
            var curve = new List<(double S, double V)>();

            // После смены знака SplitBySign начинает кусок с общей вершины —
            // добавляем ноль между предыдущей и текущей точкой.
            if (part.Start > 0)
            {
                int i = part.Start;
                double t = ZeroCrossingT(values[i - 1], values[i]);
                curve.Add((s[i - 1] + t * (s[i] - s[i - 1]), 0));
            }

            for (int i = part.Start; i < part.EndExclusive; i++)
            {
                bool p = CurveIsPositive(values[i]);
                if (p == part.Positive)
                {
                    curve.Add((s[i], values[i]));
                    continue;
                }

                // Хвостовая вершина противоположного знака — заменяем нулём и обрываем.
                if (curve.Count > 0 && i > 0)
                {
                    double t = ZeroCrossingT(values[i - 1], values[i]);
                    curve.Add((s[i - 1] + t * (s[i] - s[i - 1]), 0));
                }
                break;
            }

            if (curve.Count >= 1)
                result.Add((curve, part.Positive));
        }

        return result;
    }

    /// <summary>Параметр t∈[0,1] на отрезке v0→v1, где значение пересекает ноль.</summary>
    public static double ZeroCrossingT(double v0, double v1)
    {
        double d = v1 - v0;
        if (Math.Abs(d) < 1e-30) return 0.5;
        return Math.Clamp(-v0 / d, 0.0, 1.0);
    }
}
