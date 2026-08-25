namespace CScore.Sp63Shear;

/// <summary>Точка кривой несущей способности по длине проекции наклонного сечения.</summary>
/// <param name="C">Длина проекции, м.</param>
/// <param name="Qb">Поперечная сила, воспринимаемая бетоном, кН.</param>
/// <param name="Qsw">Поперечная сила, воспринимаемая хомутами, кН.</param>
/// <param name="QSum">Суммарная несущая способность, кН.</param>
/// <param name="Q">Действующая поперечная сила, кН.</param>
public readonly record struct ProjectionPoint(
    double C, double Qb, double Qsw, double QSum, double Q);

/// <summary>
/// Итог расчёта одной стоянки вдоль элемента. Критические величины проверок по поперечной
/// силе и по моменту хранятся раздельно: они находятся при разных проекциях C.
/// </summary>
/// <param name="S">Координата стоянки, м.</param>
/// <param name="N">Продольная сила, кН.</param>
/// <param name="PhiN">Коэффициент φn в стоянке.</param>
/// <param name="TensionOnPositiveSide">Растянута грань с положительной координатой.</param>
/// <param name="Q">Действующая поперечная сила при критической проекции, кН.</param>
/// <param name="CriticalC">Критическая проекция по поперечной силе, м; NaN — проверка не велась.</param>
/// <param name="Qb">Поперечная сила бетона при критической проекции, кН.</param>
/// <param name="Qsw">Поперечная сила хомутов при критической проекции, кН.</param>
/// <param name="Eta">Коэффициент использования по поперечной силе; NaN — проверка не велась.</param>
/// <param name="MomentApplied">Момент в точке 0 при критической проекции по моменту, кН·м.</param>
/// <param name="CriticalCMoment">Критическая проекция по моменту, м; NaN — проверка не велась.</param>
/// <param name="Ms">Момент продольной арматуры, кН·м.</param>
/// <param name="Msw">Момент хомутов при критической проекции по моменту, кН·м.</param>
/// <param name="EtaM">Коэффициент использования по моменту; NaN — проверка не выполнялась.</param>
public sealed record StationResult(
    double S, double N, double PhiN, bool TensionOnPositiveSide,
    double Q, double CriticalC, double Qb, double Qsw, double Eta,
    double MomentApplied, double CriticalCMoment, double Ms, double Msw, double EtaM);

/// <summary>Результат проверок наклонных сечений в одной плоскости сдвига.</summary>
public sealed class ShearInclinedResult
{
    /// <summary>Плоскость сдвига.</summary>
    public required ShearPlane Plane { get; init; }

    /// <summary>Проверки по пунктам нормы.</summary>
    public required IReadOnlyList<CheckDetail> Details { get; init; }

    /// <summary>Результаты по всем рассмотренным стоянкам.</summary>
    public required IReadOnlyList<StationResult> Stations { get; init; }

    /// <summary>Оговорки и предупреждения для отчёта.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>
    /// Наибольший коэффициент использования среди всех проверок, включая упрощённые
    /// (8.60) и (8.63′). Упрощённые условия дают нижнюю оценку несущей способности и
    /// нередко оказываются жёстче точных, поэтому итоговый вердикт по ним — в запас.
    /// </summary>
    public double Utilization => Details.Count == 0 ? 0.0 : Details.Max(d => d.Ratio);

    /// <summary>
    /// Коэффициент использования только по точным проверкам (8.55), (8.56) и (8.63) —
    /// без упрощённых условий. Показывается в отчёте рядом с <see cref="Utilization"/>,
    /// чтобы было видно, вердикт определён точным расчётом или его нижней оценкой.
    /// </summary>
    public double UtilizationExact
    {
        get
        {
            var exact = Details.Where(d => d.Formula is "8.55" or "8.56" or "8.63").ToList();
            return exact.Count == 0 ? 0.0 : exact.Max(d => d.Ratio);
        }
    }
}
