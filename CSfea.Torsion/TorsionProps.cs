namespace CSfea.Torsion;

/// <summary>
/// Результат решения задачи кручения Сен-Венана. Единицы нейтральны
/// (соответствуют единицам входного контура): It — единицы длины⁴,
/// координаты и τ/(GΘ) — единицы длины².
/// </summary>
public sealed class TorsionProps
{
    /// <summary>Постоянная (геометрическая) кручения, единицы длины⁴.</summary>
    public double It { get; init; }

    /// <summary>Координата X центра кручения (МГЭ всегда; МКЭ — NaN).</summary>
    public double ShearCenterX { get; init; } = double.NaN;

    /// <summary>Координата Y центра кручения (МГЭ всегда; МКЭ — NaN).</summary>
    public double ShearCenterY { get; init; } = double.NaN;

    /// <summary>Максимальное безразмерное касательное напряжение max|τ/(GΘ)|, единицы длины².</summary>
    public double TauUnitMax { get; init; }

    /// <summary>Координаты X узлов поля (МГЭ — центры элементов; МКЭ — узлы сетки).</summary>
    public double[]? NodeX { get; init; }

    /// <summary>Координаты Y узлов поля.</summary>
    public double[]? NodeY { get; init; }

    /// <summary>Поле безразмерного касательного напряжения τ/(GΘ).</summary>
    public double[]? TauUnitField { get; init; }

    /// <summary>Поле потенциала: МГЭ — депланация ω на границе; МКЭ — функция Прандтля φ в узлах.</summary>
    public double[]? PotentialField { get; init; }

    /// <summary>Флаг вырожденности СЛАУ (МГЭ).</summary>
    public bool Singular { get; init; }

    /// <summary>Число элементов/узлов дискретизации.</summary>
    public int NElements { get; init; }

    /// <summary>Фактическое максимальное касательное напряжение τ_max = G·Θ·TauUnitMax.</summary>
    public double TauMax(double shearModulusG, double twistRateTheta) => shearModulusG * twistRateTheta * TauUnitMax;
}
