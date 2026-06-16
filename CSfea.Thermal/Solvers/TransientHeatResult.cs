namespace CSfea.Thermal.Solvers;

/// <summary>
/// Результат нестационарного теплового расчёта.
/// </summary>
public sealed class TransientHeatResult
{
    /// <summary>Времена снапшотов, с.</summary>
    public double[] Times_s { get; init; } = [];

    /// <summary>Снапшоты температур, индексирование [снапшот][узел].</summary>
    public double[][] Snapshots { get; init; } = [];

    /// <summary>Лог сходимости Пикар-итераций по шагам времени.</summary>
    public IReadOnlyList<PicardRecord> ConvergenceLog { get; init; } = [];
}
