namespace CScore.Sp63.Normal;

/// <summary>Контекст элемента для учёта случайного эксцентриситета и η.</summary>
/// <param name="ElementLengthOrRestraintDistance">Длина элемента или расстояние между закреплениями, м.</param>
/// <param name="StructuralScheme">Схема статической определимости.</param>
/// <param name="EffectiveLengthL0">Расчётная длина для устойчивости, м.</param>
/// <param name="StabilityMode">Режим проверки устойчивости.</param>
/// <param name="Psi">Относительная длительная составляющая момента ψ.</param>
/// <param name="SlendernessThreshold">Порог гибкости l0/i (п. 8.1.2).</param>
/// <param name="ElementKind">Тип элемента для пп. 10.3.5 и 10.3.8.</param>
/// <param name="ExposureCondition">Условия эксплуатации для таблицы 10.1.</param>
/// <param name="IsPrecast">Сборный элемент: защитный слой по таблице 10.1 уменьшается на 5 мм.</param>
public sealed record Sp63MemberContext(
    double? ElementLengthOrRestraintDistance,
    Sp63StructuralScheme StructuralScheme,
    double? EffectiveLengthL0,
    Sp63NormalStabilityMode StabilityMode,
    double Psi,
    double SlendernessThreshold = 14.0,
    Sp63ElementKind ElementKind = Sp63ElementKind.Unspecified,
    Sp63ExposureCondition ExposureCondition = Sp63ExposureCondition.Unspecified,
    bool IsPrecast = false)
{
    /// <summary>Вычисляет случайный эксцентриситет по п. 8.1.7, м.</summary>
    public static double AccidentalEccentricity(double length, double h) =>
        Math.Max(Math.Max(length / 600.0, h / 30.0), 0.010);

    /// <summary>Объединяет статический и случайный эксцентриситет.</summary>
    public static double EffectiveEccentricity(
        double e0Static, double ea, Sp63StructuralScheme scheme) =>
        scheme == Sp63StructuralScheme.StaticallyDeterminate
            ? Math.Abs(e0Static) + ea
            : Math.Max(Math.Abs(e0Static), ea);
}
