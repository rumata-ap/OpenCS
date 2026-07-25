using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;

namespace OpenCS.Tasks;

/// <summary>Параметры запуска FEM-расчёта (линейного и нелинейного), хранимые в FemAnalysis.ParamsJson.
/// Поле CalcType используется только при Kind="nonlinear". Solver-настройки OpenSees
/// (исполняемый файл, таймаут, сходимость, источник/модель материалов и т.п.) — глобальные,
/// см. <see cref="OpenCS.Utilites.CalcSettings"/> (вкладка «OpenSees» в диалоге настроек), а не
/// хранятся в каждой постановке.</summary>
public sealed class FemAnalysisParams
{
    /// <summary>Тип расчёта для выбора диаграмм материалов fiber-сечений (нелинейный расчёт).</summary>
    public CalcType? CalcType { get; set; }
    /// <summary>Шаг коэффициента пропорциональной нагрузки λ.</summary>
    public double LoadFactorStep { get; set; } = 0.1;
    /// <summary>Максимальный коэффициент пропорциональной нагрузки λ.</summary>
    public double MaxLoadFactor { get; set; } = 10.0;
    /// <summary>Старое число шагов; читается только из legacy JSON и не записывается в новый JSON.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LoadSteps { get; set; }

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
