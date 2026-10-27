namespace CScore.Hysteresis;

/// <summary>
/// Состояние материала в волокне. Поля P0…P7 и Flags трактует модель, Aux — декоратор (<see cref="RuptureDamage"/>).
/// Eps, Sig — деформация и напряжение (кПа) зафиксированной точки.
/// </summary>
public readonly record struct HystState(double Eps, double Sig, double P0, double P1, double P2, double P3,
    double P4, double P5, double P6, double P7, double Aux, int Flags);

/// <summary>
/// Одноосная модель материала с состоянием. <see cref="Trial"/> — чистая функция: от зафиксированного состояния и
/// деформации даёт σ, касательный модуль и состояние для фиксации; путь от committed.Eps к eps считается монотонным.
/// </summary>
public interface IHysteresisModel
{
    /// <summary>Начальное (недеформированное) состояние.</summary>
    HystState Initial { get; }

    /// <summary>σ (кПа) и касательный модуль <paramref name="et"/> при деформации <paramref name="eps"/> от
    /// зафиксированного состояния; <paramref name="next"/> — состояние для фиксации, если шаг будет принят.</summary>
    double Trial(in HystState committed, double eps, bool ten, out double et, out HystState next);
}

/// <summary>Модель стали при гистерезисе.</summary>
public enum SteelHysteresisModel { MenegottoPinto, Bilinear }

/// <summary>Параметры гистерезиса задачи (бетон всегда Karsan–Jirsa над диаграммой области).</summary>
public sealed class HysteresisOptions
{
    /// <summary>Модель стали.</summary>
    public SteelHysteresisModel Steel { get; set; } = SteelHysteresisModel.MenegottoPinto;
    /// <summary>Начальный показатель кривизны перехода Menegotto–Pinto.</summary>
    public double R0 { get; set; } = 20;
    /// <summary>cR1: R = R0·(1 − cR1·ξ/(cR2 + ξ)).</summary>
    public double CR1 { get; set; } = 0.925;
    /// <summary>cR2: R = R0·(1 − cR1·ξ/(cR2 + ξ)).</summary>
    public double CR2 { get; set; } = 0.15;
}
