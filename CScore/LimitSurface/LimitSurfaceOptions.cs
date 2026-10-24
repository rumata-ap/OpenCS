namespace CScore.LimitSurface;

/// <summary>Параметры построения сетки поверхности (значения по умолчанию — из замеров спеки).</summary>
public sealed class LimitSurfaceOptions
{
    /// <summary>Стартовое число меридианов nφ0.</summary>
    public int InitialMeridians { get; init; } = 24;
    /// <summary>Число интервалов по θ на меридиане до вставки изломов и адаптации.</summary>
    public int ThetaIntervals { get; init; } = 24;
    /// <summary>Допуск адаптации в нормированных усилиях (N/ΔN полюсов, M/M_max); 0 — без адаптации.</summary>
    public double RefineTol { get; init; } = 0.002;
    /// <summary>Предельное число меридианов.</summary>
    public int MaxMeridians { get; init; } = 576;
    /// <summary>Глубина рекурсии адаптации по θ.</summary>
    public int MaxThetaDepth { get; init; } = 6;
    /// <summary>Считать меридианы параллельно.</summary>
    public bool Parallel { get; init; } = true;
    /// <summary>Применять (8.53) и (8.54); выключение — только для сверки с решателями.</summary>
    internal bool ApplySingleSignRule { get; init; } = true;
}
