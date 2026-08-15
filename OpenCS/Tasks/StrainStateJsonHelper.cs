namespace OpenCS.Tasks;

/// <summary>Подготовка чисел результата strain-state к стандартному JSON.</summary>
public static class StrainStateJsonHelper
{
    /// <summary>Округляет конечное число; NaN и бесконечность возвращает как null.</summary>
    public static double? FiniteRounded(double value, int digits)
        => double.IsFinite(value) ? Math.Round(value, digits) : null;

    /// <summary>Округляет и оставляет только конечные значения истории.</summary>
    public static double[] FiniteRoundedArray(IEnumerable<double> values, int digits)
        => values
            .Where(double.IsFinite)
            .Select(value => Math.Round(value, digits))
            .ToArray();
}
