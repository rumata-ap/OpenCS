namespace CScore.Sp63.Normal;

/// <summary>Контекст элемента для учёта случайного эксцентриситета и η.</summary>
/// <param name="ElementLengthOrRestraintDistance">Длина элемента или расстояние между закреплениями, м.</param>
/// <param name="StructuralScheme">Схема статической определимости.</param>
/// <param name="EffectiveLengthL0">Расчётная длина для устойчивости, м.</param>
/// <param name="StabilityMode">Режим проверки устойчивости.</param>
/// <param name="Psi">Относительная длительная составляющая момента ψ.</param>
/// <param name="SlendernessThreshold">Порог гибкости l0/h.</param>
public sealed record Sp63MemberContext(
    double? ElementLengthOrRestraintDistance,
    Sp63StructuralScheme StructuralScheme,
    double? EffectiveLengthL0,
    Sp63NormalStabilityMode StabilityMode,
    double Psi,
    double SlendernessThreshold = 14.0)
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
