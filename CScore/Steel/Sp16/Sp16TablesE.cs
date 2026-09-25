namespace CScore.Sp16;

/// <summary>Коэффициенты cx, cy, n табл. Е.1 для канонических осей сечения.</summary>
public sealed record PlasticCoefficients(double Cx, double Cy, double NOnlyMx, double NOnlyMy, double NBoth, int TableType, string Note);

public static partial class Sp16Tables
{
    static readonly double[] E1Ratios = [0.25, 0.5, 1.0, 2.0];

    /// <summary>
    /// Табл. Е.1 (прил. Е): наибольшие значения cx, cy и n для сечения в канонических осях.
    /// n зависит от наличия момента относительно «второй» оси схемы табл. Е.1: при My ≠ 0 (в осях
    /// таблицы) n = 1,5, кроме типов 5а (n = 2) и 5б (n = 3). Для тавра и швеллера, у которых n в
    /// таблице зависит от ориентации (а/б), принимается меньшее (в запас) n = 1,0.
    /// Промежуточные Af/Aw — линейная интерполяция (прим. 1), вне диапазона — крайние значения.
    /// null — сечение в табл. Е.1 не приведено.
    /// </summary>
    public static PlasticCoefficients? TableE1(Sp16Section s)
    {
        switch (s.Kind)
        {
            case SteelProfileKind.IBeam:
            {
                double afMax = Math.Max(s.AfTop, s.AfBottom), afMin = Math.Min(s.AfTop, s.AfBottom);
                double r = afMin / afMax;
                if (r >= 0.95)
                {
                    double cx = Interp(s.AfTop / s.Aw, E1Ratios, [1.19, 1.12, 1.07, 1.04]);
                    return new(cx, 1.47, 1.5, 1.5, 1.5, 1, "тип 1");
                }
                if (Math.Abs(r - 0.5) <= 0.1)
                {
                    double cx = Interp(afMax / s.Aw, [0.5, 1.0, 2.0], [1.40, 1.28, 1.18]);
                    return new(cx, 1.47, 2.0, 1.5, 1.5, 2, "тип 2 (меньший пояс ≈ 0,5 большего)");
                }
                return null;
            }
            case SteelProfileKind.Box:
            {
                double ratio = s.AfTop / s.Aw;
                double cx = Interp(ratio, E1Ratios, [1.19, 1.12, 1.07, 1.04]);
                double cy = Interp(ratio, E1Ratios, [1.07, 1.12, 1.19, 1.26]);
                return new(cx, cy, 1.5, 1.5, 1.5, 3, "тип 3");
            }
            case SteelProfileKind.Rect:
                return new(1.47, 1.47, 2.0, 2.0, 2.0, 5, "тип 5а");
            case SteelProfileKind.Pipe:
                return new(1.26, 1.26, 1.5, 1.5, 1.5, 7, "тип 7");
            case SteelProfileKind.Tee:
                // Тип 8: изгиб в плоскости стенки — cx = 1,60 (n а) 3,0, б) 1,0 → 1,0 в запас); из плоскости — cy = 1,47, n = 1,5.
                return new(1.60, 1.47, 1.0, 1.5, 1.5, 8, "тип 8; n при изгибе в плоскости стенки принят 1,0 (меньшее из а) 3,0 и б) 1,0)");
            case SteelProfileKind.Channel:
            {
                // Тип 9 (схема повёрнута): изгиб относительно канонической x (в плоскости стенки) соответствует cy
                // табл. Е.1 при Af/Aw = (площадь стенки)/(площадь двух полок); изгиб из плоскости стенки — cx = 1,60.
                double web = s.Profile.H * s.Tw, flanges = 2 * (s.Profile.Bf1 - s.Tw) * s.Profile.Tf1;
                double cyTable = Interp(web / flanges, [0.5, 1.0, 2.0], [1.07, 1.12, 1.19]);
                return new(cyTable, 1.60, 1.5, 1.0, 1.5, 9, "тип 9 (оси схемы повёрнуты); n при изгибе из плоскости стенки принят 1,0 (меньшее из а) 3,0 и б) 1,0)");
            }
            default:
                return null;
        }
    }

    /// <summary>Прим. 2 табл. Е.1: c ≤ 1,15γf.</summary>
    public static double CapPlastic(double c, double gammaF) => Math.Min(c, 1.15 * gammaF);
}
