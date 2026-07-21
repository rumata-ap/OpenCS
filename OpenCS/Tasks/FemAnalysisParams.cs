using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;

namespace OpenCS.Tasks;

/// <summary>Параметры запуска FEM-расчёта (линейного и нелинейного), хранимые в FemAnalysis.ParamsJson.
/// Поля CalcType/LoadFactorStep/MaxLoadFactor/RefinementDivisions/Tolerance/MaxIterations/GeomTransfKind/IntegrationPoints используются
/// только при Kind="nonlinear".</summary>
public sealed class FemAnalysisParams
{
    public string? ExecutablePath { get; set; }
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Тип расчёта для выбора диаграмм материалов fiber-сечений (нелинейный расчёт).</summary>
    public CalcType? CalcType { get; set; }
    /// <summary>Шаг коэффициента пропорциональной нагрузки λ.</summary>
    public double LoadFactorStep { get; set; } = 0.1;
    /// <summary>Максимальный коэффициент пропорциональной нагрузки λ.</summary>
    public double MaxLoadFactor { get; set; } = 10.0;
    /// <summary>Количество частей для уточнения последнего неудачного шага.</summary>
    public int RefinementDivisions { get; set; } = 10;
    /// <summary>Старое число шагов; читается только из legacy JSON и не записывается в новый JSON.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LoadSteps { get; set; }
    /// <summary>Допуск критерия сходимости.</summary>
    public double Tolerance { get; set; } = 1e-6;
    /// <summary>Максимальное число итераций Ньютона на шаг.</summary>
    public int MaxIterations { get; set; } = 50;
    /// <summary>Формулировка geomTransf: "Linear" | "PDelta" | "Corotational".</summary>
    public string GeomTransfKind { get; set; } = "Linear";
    /// <summary>Критерий сходимости Ньютона: "EnergyIncr" (по умолчанию, самый устойчивый) |
    /// "NormUnbalance" | "NormDispIncr".</summary>
    public string ConvergenceTest { get; set; } = "EnergyIncr";
    /// <summary>Число точек интегрирования forceBeamColumn.</summary>
    public int IntegrationPoints { get; set; } = 5;

    public string ToJson() => JsonSerializer.Serialize(this);
    public static FemAnalysisParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        var result = JsonSerializer.Deserialize<FemAnalysisParams>(json) ?? new();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("LoadFactorStep", out _) && result.LoadSteps is > 0)
            result.LoadFactorStep = 1.0 / result.LoadSteps.Value;
        result.LoadSteps = null;
        return result;
    }
}
