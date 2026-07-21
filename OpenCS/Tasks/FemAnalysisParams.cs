using System.Text.Json;
using CScore;

namespace OpenCS.Tasks;

/// <summary>Параметры запуска FEM-расчёта (линейного и нелинейного), хранимые в FemAnalysis.ParamsJson.
/// Поля CalcType/LoadSteps/Tolerance/MaxIterations/GeomTransfKind/IntegrationPoints используются
/// только при Kind="nonlinear".</summary>
public sealed class FemAnalysisParams
{
    public string? ExecutablePath { get; set; }
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Тип расчёта для выбора диаграмм материалов fiber-сечений (нелинейный расчёт).</summary>
    public CalcType? CalcType { get; set; }
    /// <summary>Число шагов нагрузки (LoadControl).</summary>
    public int LoadSteps { get; set; } = 10;
    /// <summary>Допуск критерия сходимости NormUnbalance.</summary>
    public double Tolerance { get; set; } = 1e-6;
    /// <summary>Максимальное число итераций Ньютона на шаг.</summary>
    public int MaxIterations { get; set; } = 50;
    /// <summary>Формулировка geomTransf: "Linear" | "PDelta" | "Corotational".</summary>
    public string GeomTransfKind { get; set; } = "Linear";
    /// <summary>Число точек интегрирования forceBeamColumn.</summary>
    public int IntegrationPoints { get; set; } = 5;

    public string ToJson() => JsonSerializer.Serialize(this);
    public static FemAnalysisParams Parse(string? json) =>
        string.IsNullOrWhiteSpace(json) ? new() : JsonSerializer.Deserialize<FemAnalysisParams>(json) ?? new();
}
