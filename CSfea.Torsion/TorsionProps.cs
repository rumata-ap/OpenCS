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

    /// <summary>Координата X центра кручения по подходу Трефтца (только МКЭ; МГЭ — NaN).</summary>
    public double ShearCenterTrefftzX { get; init; } = double.NaN;

    /// <summary>Координата Y центра кручения по подходу Трефтца (только МКЭ; МГЭ — NaN).</summary>
    public double ShearCenterTrefftzY { get; init; } = double.NaN;

    /// <summary>Максимальное безразмерное касательное напряжение max|τ/(GΘ)|, единицы длины².</summary>
    public double TauUnitMax { get; init; }

    /// <summary>Координаты X узлов поля (МГЭ — центры элементов; МКЭ — узлы сетки).</summary>
    public double[]? NodeX { get; init; }

    /// <summary>Координаты Y узлов поля.</summary>
    public double[]? NodeY { get; init; }

    /// <summary>Поле безразмерного касательного напряжения τ/(GΘ) (магнитуда).</summary>
    public double[]? TauUnitField { get; init; }

    /// <summary>Компонента X безразмерного τ/(GΘ) от кручения (только МКЭ; нужна для векторного
    /// суммирования с τ от Vx/Vy при вычислении комбинированных напряжений).</summary>
    public double[]? TauUnitFieldX { get; init; }

    /// <summary>Компонента Y безразмерного τ/(GΘ) от кручения (только МКЭ).</summary>
    public double[]? TauUnitFieldY { get; init; }

    /// <summary>Поле потенциала: МГЭ — депланация ω на границе; МКЭ — функция Прандтля φ в узлах.</summary>
    public double[]? PotentialField { get; init; }

    /// <summary>Поле функции депланации ω в узлах сетки (только МКЭ; определено с точностью до аддитивной константы).</summary>
    public double[]? WarpingField { get; init; }

    /// <summary>Секториальная жёсткость (константа депланации) γ=Iω, единицы длины⁶ (только МКЭ; МГЭ — NaN).</summary>
    public double WarpingConstant { get; init; } = double.NaN;

    /// <summary>Δs — сдвиговый коэффициент сечения (только МКЭ; МГЭ — NaN), см. <see cref="TorsionShearStressPostprocessor.Combine"/>.</summary>
    public double ShearDeltaS { get; init; } = double.NaN;

    /// <summary>Единичное поле τx от Vx (без множителя E·Vx/Δs), в узлах сетки (только МКЭ).</summary>
    public double[]? ShearVxUnitFieldX { get; init; }

    /// <summary>Единичное поле τy от Vx (без множителя E·Vx/Δs), в узлах сетки (только МКЭ).</summary>
    public double[]? ShearVxUnitFieldY { get; init; }

    /// <summary>Единичное поле τx от Vy (без множителя E·Vy/Δs), в узлах сетки (только МКЭ).</summary>
    public double[]? ShearVyUnitFieldX { get; init; }

    /// <summary>Единичное поле τy от Vy (без множителя E·Vy/Δs), в узлах сетки (только МКЭ).</summary>
    public double[]? ShearVyUnitFieldY { get; init; }

    /// <summary>Флаг вырожденности СЛАУ (МГЭ).</summary>
    public bool Singular { get; init; }

    /// <summary>Треугольники МКЭ: [i0, i1, i2] на элемент (null для МГЭ).</summary>
    public int[][]? Triangles { get; init; }

    /// <summary>Координаты X вершин граничной дискретизации (МГЭ).</summary>
    public double[]? BoundaryX { get; init; }

    /// <summary>Координаты Y вершин граничной дискретизации (МГЭ).</summary>
    public double[]? BoundaryY { get; init; }

    /// <summary>Индекс следующей вершины по контуру (МГЭ).</summary>
    public int[]? BoundaryJ1 { get; init; }

    /// <summary>Число элементов/узлов дискретизации.</summary>
    public int NElements { get; init; }

    /// <summary>Фактическое максимальное касательное напряжение τ_max = G·Θ·TauUnitMax.</summary>
    public double TauMax(double shearModulusG, double twistRateTheta) => shearModulusG * twistRateTheta * TauUnitMax;
}
