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
}
