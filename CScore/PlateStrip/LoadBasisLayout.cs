namespace CScore.PlateStrip;

/// <summary>Раскладка базиса нагрузки по станциям: сколько коэффициентов, в каком порядке и как
/// они превращаются в StripLoad. Единственная точка правды этих соглашений — и сборка оператора
/// отклика, и выдача восстановленной нагрузки идут через неё, поэтому разойтись они не могут.
///
/// Порядок коэффициентов: сначала все qx, затем все qy, затем все qz.
/// PiecewiseConstant — n−1 функций (по одной на интервал между станциями),
/// PiecewiseLinear — n hat-функций станций (крайние — полу-hat).</summary>
public sealed class LoadBasisLayout
{
    public LoadBasis Basis { get; }
    public IReadOnlyList<double> StationFractions { get; }
    public int FunctionsPerComponent { get; }

    public int CoefficientCount => 3 * FunctionsPerComponent;
    public int IntervalCount => StationFractions.Count - 1;

    /// <summary>Смещение блока компоненты в векторе коэффициентов: 0 — qx, 1 — qy, 2 — qz.</summary>
    public int ComponentOffset(int component) => component * FunctionsPerComponent;

    public LoadBasisLayout(LoadBasis basis, IReadOnlyList<double> stationFractions)
    {
        ArgumentNullException.ThrowIfNull(stationFractions);
        if (stationFractions.Count < 2)
            throw new ArgumentException("Нужно не менее двух станций.", nameof(stationFractions));

        Basis = basis;
        StationFractions = stationFractions;
        FunctionsPerComponent = basis switch
        {
            LoadBasis.PiecewiseConstant => stationFractions.Count - 1,
            LoadBasis.PiecewiseLinear => stationFractions.Count,
            _ => throw new ArgumentOutOfRangeException(nameof(basis), "Неизвестный базис нагрузки.")
        };
    }

    /// <summary>Положения коэффициентов вдоль полосы (доли пролёта): станции для hat-базиса и
    /// середины интервалов для кусочно-постоянного. Нужны оператору сглаживания.</summary>
    public double[] CoefficientPositions()
    {
        var result = new double[FunctionsPerComponent];
        if (Basis == LoadBasis.PiecewiseLinear)
        {
            for (int i = 0; i < FunctionsPerComponent; i++)
                result[i] = StationFractions[i];
        }
        else
        {
            for (int i = 0; i < FunctionsPerComponent; i++)
                result[i] = 0.5 * (StationFractions[i] + StationFractions[i + 1]);
        }
        return result;
    }

    /// <summary>Превращает коэффициенты в набор StripLoad по интервалам между станциями.
    /// Интервалы с тождественно нулевой интенсивностью не выпускаются.</summary>
    public IReadOnlyList<StripLoad> ToLoads(IReadOnlyList<double> coefficients, string sourceTag)
    {
        ArgumentNullException.ThrowIfNull(coefficients);
        if (coefficients.Count != CoefficientCount)
            throw new ArgumentException(
                "Число коэффициентов должно соответствовать базису.", nameof(coefficients));

        var loads = new List<StripLoad>(IntervalCount);
        for (int interval = 0; interval < IntervalCount; interval++)
        {
            var (start, end) = IntervalIntensities(coefficients, interval);

            bool isZero = start.Qx == 0.0 && start.Qy == 0.0 && start.Qz == 0.0 &&
                          end.Qx == 0.0 && end.Qy == 0.0 && end.Qz == 0.0;
            if (isZero)
                continue;

            bool uniform = Basis == LoadBasis.PiecewiseConstant;
            loads.Add(new StripLoad
            {
                Kind = uniform ? StripLoadKind.DistributedUniform : StripLoadKind.DistributedLinear,
                SourceTag = $"{sourceTag}#{interval}",
                StationStartFraction = StationFractions[interval],
                StationEndFraction = StationFractions[interval + 1],
                QxKnM = start.Qx,
                QyKnM = start.Qy,
                QzKnM = start.Qz,
                QxEndKnM = uniform ? 0.0 : end.Qx,
                QyEndKnM = uniform ? 0.0 : end.Qy,
                QzEndKnM = uniform ? 0.0 : end.Qz
            });
        }
        return loads;
    }

    /// <summary>Набор нагрузок для единичного значения одного коэффициента — столбец оператора
    /// отклика собирается ровно тем же преобразованием, что и итоговая нагрузка.</summary>
    public IReadOnlyList<StripLoad> UnitLoads(int coefficientIndex)
    {
        if (coefficientIndex < 0 || coefficientIndex >= CoefficientCount)
            throw new ArgumentOutOfRangeException(nameof(coefficientIndex));

        var coefficients = new double[CoefficientCount];
        coefficients[coefficientIndex] = 1.0;
        return ToLoads(coefficients, "basis");
    }

    /// <summary>Значение базисной функции на границах интервала для всех трёх компонент.</summary>
    ((double Qx, double Qy, double Qz) Start, (double Qx, double Qy, double Qz) End)
        IntervalIntensities(IReadOnlyList<double> coefficients, int interval)
    {
        if (Basis == LoadBasis.PiecewiseConstant)
        {
            var value = (
                Qx: coefficients[ComponentOffset(0) + interval],
                Qy: coefficients[ComponentOffset(1) + interval],
                Qz: coefficients[ComponentOffset(2) + interval]);
            return (value, value);
        }

        var start = (
            Qx: coefficients[ComponentOffset(0) + interval],
            Qy: coefficients[ComponentOffset(1) + interval],
            Qz: coefficients[ComponentOffset(2) + interval]);
        var end = (
            Qx: coefficients[ComponentOffset(0) + interval + 1],
            Qy: coefficients[ComponentOffset(1) + interval + 1],
            Qz: coefficients[ComponentOffset(2) + interval + 1]);
        return (start, end);
    }
}
