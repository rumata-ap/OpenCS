namespace CScore.Sp63.Deflection;

/// <summary>Одна из поддерживаемых расчётных схем для приближённого вычисления прогиба.</summary>
public enum Sp63DeflectionStaticScheme
{
    /// <summary>Шарнирно опёртая балка с равномерно распределённой нагрузкой.</summary>
    SimplySupportedUniform,
    /// <summary>Шарнирно опёртая балка с силой в середине пролёта.</summary>
    SimplySupportedMidpoint,
    /// <summary>Консоль с силой на свободном конце.</summary>
    CantileverTip
}

/// <summary>Преобразует идентификатор схемы в её коэффициент прогиба.</summary>
public static class Sp63DeflectionScheme
{
    /// <summary>Разбирает единственный допустимый идентификатор схемы.</summary>
    public static bool TryParse(string? value, out Sp63DeflectionStaticScheme scheme)
    {
        scheme = default;
        switch (value)
        {
            case "simply_supported_uniform": scheme = Sp63DeflectionStaticScheme.SimplySupportedUniform; return true;
            case "simply_supported_midpoint": scheme = Sp63DeflectionStaticScheme.SimplySupportedMidpoint; return true;
            case "cantilever_tip": scheme = Sp63DeflectionStaticScheme.CantileverTip; return true;
            default: return false;
        }
    }

    /// <summary>Коэффициент S в формуле f = S·l²·(1/r) для характерного сечения схемы.</summary>
    public static double Coefficient(Sp63DeflectionStaticScheme scheme) => scheme switch
    {
        Sp63DeflectionStaticScheme.SimplySupportedUniform => 5.0 / 48.0,
        Sp63DeflectionStaticScheme.SimplySupportedMidpoint => 1.0 / 12.0,
        Sp63DeflectionStaticScheme.CantileverTip => 1.0 / 3.0,
        _ => throw new ArgumentOutOfRangeException(nameof(scheme))
    };

    /// <summary>Возвращает устойчивый JSON-идентификатор схемы.</summary>
    public static string Id(Sp63DeflectionStaticScheme scheme) => scheme switch
    {
        Sp63DeflectionStaticScheme.SimplySupportedUniform => "simply_supported_uniform",
        Sp63DeflectionStaticScheme.SimplySupportedMidpoint => "simply_supported_midpoint",
        Sp63DeflectionStaticScheme.CantileverTip => "cantilever_tip",
        _ => throw new ArgumentOutOfRangeException(nameof(scheme))
    };
}
