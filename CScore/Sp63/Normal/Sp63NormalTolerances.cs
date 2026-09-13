namespace CScore.Sp63.Normal;

/// <summary>Общие численные допуски формульных проверок СП 63.</summary>
internal static class Sp63NormalTolerances
{
    /// <summary>Допуск сравнения продольных усилий, кН.</summary>
    public const double Force = 1e-9;

    /// <summary>Допуск сравнения моментов, кН·м.</summary>
    public const double Moment = 1e-9;

    /// <summary>Допуск сравнения площадей арматуры, м².</summary>
    public const double RebarArea = 1e-9;
}
