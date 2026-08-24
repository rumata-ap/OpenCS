namespace CScore.Sp63Shear;

/// <summary>Расчётные данные одной плоскости сдвига для проверок наклонных сечений.</summary>
/// <param name="B">Расчётная ширина, м.</param>
/// <param name="H0">Рабочая высота, м.</param>
/// <param name="Rb">Сопротивление бетона сжатию, кПа.</param>
/// <param name="Rbt">Сопротивление бетона растяжению, кПа.</param>
/// <param name="Qsw">Погонное усилие в хомутах, кН/м.</param>
/// <param name="Sw">Шаг хомутов, м.</param>
/// <param name="Ns">Усилие Σ(Rs,i·As,i) в растянутой продольной арматуре, кН.</param>
/// <param name="Kind">Тип элемента для режима φn.</param>
/// <param name="AnchorageFactor">Коэффициент включения продольной арматуры k.</param>
/// <param name="StationStep">Шаг стоянок, м; 0 — авто.</param>
/// <param name="ProjectionStep">Шаг перебора проекции C, м; 0 — авто.</param>
/// <param name="MomentZoneLength">Длина приопорной зоны для проверки (8.63), м; 0 — 2·h0.</param>
/// <param name="BarCutoffs">Координаты обрывов продольной арматуры, м.</param>
/// <param name="CheckMoment">Выполнять проверки по 8.1.35.</param>
/// <param name="PhiNOverride">Ручное значение φn; null — расчёт по 8.1.34.</param>
/// <param name="FixedB">Ручная ширина, если задана пользователем.</param>
/// <param name="FixedH0">Ручная рабочая высота, если задана пользователем.</param>
/// <param name="FixedNs">Ручное усилие в продольной арматуре, если задано пользователем.</param>
public sealed record ShearInclinedInput(
    double B, double H0, double Rb, double Rbt, double Qsw, double Sw, double Ns,
    ElementKind Kind, double AnchorageFactor,
    double StationStep, double ProjectionStep, double MomentZoneLength,
    IReadOnlyList<double> BarCutoffs, bool CheckMoment, double? PhiNOverride,
    double? FixedB = null, double? FixedH0 = null, double? FixedNs = null)
{
    /// <summary>Фактический шаг перебора проекции C, м.</summary>
    public double ProjectionStepOrAuto() => ProjectionStep > 0.0 ? ProjectionStep : H0 / 100.0;

    /// <summary>Фактический шаг стоянок, м.</summary>
    public double StationStepOrAuto(double length) =>
        StationStep > 0.0
            ? StationStep
            : Math.Min(H0 / 2.0, length > 0.0 ? length / 20.0 : H0 / 2.0);

    /// <summary>
    /// Подставляет геометрию стоянки, сохраняя ручные переопределения пользователя.
    /// Нужен потому, что при смене знака момента меняются растянутая грань, h0, Ns и b.
    /// </summary>
    public ShearInclinedInput WithGeometry(InclinedSectionGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return this with
        {
            B = FixedB ?? geometry.B,
            H0 = FixedH0 ?? geometry.H0,
            Ns = FixedNs ?? geometry.Ns
        };
    }
}
