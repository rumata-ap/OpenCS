using CSfea.Thermal.Solvers;

namespace CScore.Fire;

/// <summary>
/// Результат огневого нестационарного теплового расчёта сечения.
/// </summary>
public sealed class FireThermalResult
{
    /// <summary>Информация о построенной тепловой сетке и привязках.</summary>
    public required FireMeshBuildResult MeshInfo { get; init; }

    /// <summary>Времена снапшотов в минутах.</summary>
    public double[] TimesMin { get; init; } = [];

    /// <summary>Снапшоты температуры по узлам: [снапшот][узел], °C.</summary>
    public double[][] Snapshots { get; init; } = [];

    /// <summary>Максимальная температура арматурных точек по времени, °C.</summary>
    public Dictionary<int, double> RebarMaxTemperatures { get; init; } = [];

    /// <summary>История температуры арматурных точек: key=id, value=[снапшот], °C.</summary>
    public Dictionary<int, double[]> RebarTemperatureHistory { get; init; } = [];

    /// <summary>Индексы узлов холодной стороны (рёбра ambient).</summary>
    public List<int> ColdFaceNodeIds { get; init; } = [];

    /// <summary>Температура узлов холодной стороны: [снапшот][узел cold-face], °C.</summary>
    public double[][]? ColdFaceTemperatureHistory { get; init; }

    /// <summary>Начальная температура холодной стороны, °C.</summary>
    public double ColdFaceInitialTemperature { get; init; } = 20.0;

    /// <summary>Лог сходимости Пикар-итераций решателя.</summary>
    public List<PicardRecord> ConvergenceLog { get; init; } = [];

    /// <summary>Тип заполнителя бетона (silicate, carbonate, lightweight).</summary>
    public string AggregateType { get; init; } = "silicate";

    /// <summary>Массовая доля влаги бетона.</summary>
    public double MoistureFraction { get; init; } = 0.025;

    /// <summary>Имя огневой кривой (iso834, hydrocarbon, slow).</summary>
    public string FireCurve { get; init; } = "iso834";

    /// <summary>Длительность огневого расчёта, мин.</summary>
    public double FireDurationMin { get; init; }
}
