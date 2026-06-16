namespace CScore.Fire;

/// <summary>Стандартные огневые кривые — температура среды как функция времени.</summary>
/// <remarks>
/// Все функции принимают <paramref name="tSeconds"/> в секундах,
/// возвращают температуру среды в °C и начинаются с T(0) = 20°C.
/// </remarks>
public static class FireCurves
{
    /// <summary>Стандартная огневая кривая ISO 834 / ГОСТ 30247.0.</summary>
    /// <remarks>T(t) = 20 + 345 · log10(8 t_min + 1), где t_min = t / 60.</remarks>
    public static double Iso834(double tSeconds)
    {
        if (tSeconds <= 0.0)
            return 20.0;
        double tMin = tSeconds / 60.0;
        return 20.0 + 345.0 * Math.Log10(8.0 * tMin + 1.0);
    }

    /// <summary>Углеводородная кривая (EN 1991-1-2, приложение для нефтегаза).</summary>
    /// <remarks>
    /// T(t) = 20 + 1080 · (1 − 0.325·exp(−0.167·t_min) − 0.675·exp(−2.5·t_min)).
    /// </remarks>
    public static double Hydrocarbon(double tSeconds)
    {
        if (tSeconds <= 0.0)
            return 20.0;
        double tMin = tSeconds / 60.0;
        return 20.0 + 1080.0 * (
            1.0 - 0.325 * Math.Exp(-0.167 * tMin) - 0.675 * Math.Exp(-2.5 * tMin));
    }

    /// <summary>Кривая медленного нагрева (EN 1991-1-2, аналог склада/жилья).</summary>
    /// <remarks>
    /// T(t) = 20 + 154 · (1 − exp(−0.5·t_min)) · log10(8 t_min + 1).
    /// Упрощённый профиль — заметно ниже ISO 834.
    /// </remarks>
    public static double SlowHeat(double tSeconds)
    {
        if (tSeconds <= 0.0)
            return 20.0;
        double tMin = tSeconds / 60.0;
        return 20.0 + 154.0 * (1.0 - Math.Exp(-0.5 * tMin)) * Math.Log10(8.0 * tMin + 1.0);
    }

    /// <summary>Получить функцию-кривую по строковому имени.</summary>
    /// <param name="name">Имя кривой: <c>iso834</c>, <c>hydrocarbon</c> или <c>slow</c>.</param>
    /// <returns>Функция T(t) в °C при аргументе t в секундах.</returns>
    /// <exception cref="ArgumentException">Неизвестное имя кривой.</exception>
    public static Func<double, double> Get(string name)
    {
        return name switch
        {
            "iso834" => Iso834,
            "hydrocarbon" => Hydrocarbon,
            "slow" => SlowHeat,
            _ => throw new ArgumentException(
                $"Неизвестная огневая кривая: '{name}'. Допустимые: iso834, hydrocarbon, slow.",
                nameof(name)),
        };
    }
}
