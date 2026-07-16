using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;

namespace OpenCS.Tasks;

/// <summary>
/// Параметры задачи пространственной диаграммы N–Mx–My через OpenSees.
/// </summary>
public sealed class OpenSeesSpatialInteractionParams
{
    /// <summary>Шаг полного оборота луча кривизн в градусах.</summary>
    [JsonPropertyName("angleStepDegrees")]
    public double AngleStepDegrees { get; init; } = 45;

    /// <summary>Максимальная длина каждого луча кривизны в 1/м.</summary>
    [JsonPropertyName("maxCurvature")]
    public double MaxCurvature { get; init; } = 0.01;

    /// <summary>Количество радиальных шагов каждого луча.</summary>
    [JsonPropertyName("increments")]
    public int Increments { get; init; } = 20;

    /// <summary>Таймаут каждого внешнего запуска в секундах.</summary>
    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; init; } = 300;

    /// <summary>Необязательный явный путь к OpenSees.</summary>
    [JsonPropertyName("executablePath")]
    public string? ExecutablePath { get; init; }

    /// <summary>
    /// Разбирает JSON параметров и проверяет диапазоны числовых настроек.
    /// </summary>
    public static OpenSeesSpatialInteractionParams Parse(string? json)
    {
        OpenSeesSpatialInteractionParams result = string.IsNullOrWhiteSpace(json) || json.Trim() == "{}"
            ? new OpenSeesSpatialInteractionParams()
            : JsonSerializer.Deserialize<OpenSeesSpatialInteractionParams>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new OpenSeesSpatialInteractionParams();

        if (!double.IsFinite(result.AngleStepDegrees) || result.AngleStepDegrees <= 0)
            throw new ArgumentException("AngleStepDegrees must be positive and finite.", nameof(json));

        double angleCount = 360.0 / result.AngleStepDegrees;
        double roundedAngleCount = Math.Round(angleCount);
        if (roundedAngleCount < 1 ||
            Math.Abs(angleCount - roundedAngleCount) > 1e-9 * Math.Max(1, Math.Abs(angleCount)))
            throw new ArgumentException("AngleStepDegrees must divide 360 degrees.", nameof(json));

        if (!double.IsFinite(result.MaxCurvature) || result.MaxCurvature <= 0)
            throw new ArgumentException("MaxCurvature must be positive and finite.", nameof(json));
        if (result.Increments <= 0)
            throw new ArgumentException("Increments must be positive.", nameof(json));
        if (result.TimeoutSeconds <= 0)
            throw new ArgumentException("TimeoutSeconds must be positive.", nameof(json));

        return new OpenSeesSpatialInteractionParams
        {
            AngleStepDegrees = result.AngleStepDegrees,
            MaxCurvature = result.MaxCurvature,
            Increments = result.Increments,
            TimeoutSeconds = result.TimeoutSeconds,
            ExecutablePath = result.ExecutablePath
        };
    }

    /// <summary>
    /// Извлекает первые уникальные значения N из строк выбранного барного ForceSet.
    /// </summary>
    public static IReadOnlyList<double> ExtractAxialForcesKn(ForceSet forceSet)
    {
        ArgumentNullException.ThrowIfNull(forceSet);

        List<double> values = [];
        HashSet<double> seen = [];
        foreach (LoadItem item in forceSet.Items)
        {
            if (!double.IsFinite(item.N))
                throw new ArgumentException("ForceSet must contain finite axial forces.", nameof(forceSet));
            if (seen.Add(item.N))
                values.Add(item.N);
        }

        if (values.Count == 0)
            throw new ArgumentException("ForceSet must contain at least one axial force.", nameof(forceSet));

        return values;
    }
}
