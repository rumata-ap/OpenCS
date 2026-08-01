using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCS.Tasks;

/// <summary>Параметры задачи одноосной диаграммы N-M через OpenSees.</summary>
public sealed class OpenSeesSectionInteractionParams
{
    /// <summary>Упорядоченный список продольных сил в кН.</summary>
    [JsonPropertyName("axialForces")]
    public IReadOnlyList<double> AxialForcesKn { get; init; } = [0];

    /// <summary>Максимальная кривизна каждой точки в 1/м.</summary>
    public double MaxCurvature { get; init; } = 0.01;

    /// <summary>Количество шагов кривизны.</summary>
    public int Increments { get; init; } = 20;

    /// <summary>Направление изгиба: Mx или My.</summary>
    public string Axis { get; init; } = "Mx";

    /// <summary>Таймаут каждого внешнего процесса в секундах.</summary>
    public int TimeoutSeconds { get; init; } = 300;

    /// <summary>Необязательный явный путь к OpenSees executable.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Разбирает ParamsJson и проверяет диапазоны параметров.</summary>
    public static OpenSeesSectionInteractionParams Parse(string? json)
    {
        OpenSeesSectionInteractionParams result = string.IsNullOrWhiteSpace(json) || json.Trim() == "{}"
            ? new OpenSeesSectionInteractionParams()
            : JsonSerializer.Deserialize<OpenSeesSectionInteractionParams>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new OpenSeesSectionInteractionParams();

        if (result.AxialForcesKn is null || result.AxialForcesKn.Count == 0 ||
            result.AxialForcesKn.Any(force => !double.IsFinite(force)))
            throw new ArgumentException("AxialForces must contain finite values.", nameof(json));
        if (result.AxialForcesKn.Count != result.AxialForcesKn.Distinct().Count())
            throw new ArgumentException("AxialForces must not contain duplicates.", nameof(json));
        if (!double.IsFinite(result.MaxCurvature) || result.MaxCurvature <= 0)
            throw new ArgumentException("MaxCurvature must be positive and finite.", nameof(json));
        if (result.Increments <= 0)
            throw new ArgumentException("Increments must be positive.", nameof(json));
        if (result.TimeoutSeconds <= 0)
            throw new ArgumentException("TimeoutSeconds must be positive.", nameof(json));

        string axis = result.Axis.Trim();
        if (!axis.Equals("Mx", StringComparison.OrdinalIgnoreCase) &&
            !axis.Equals("My", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Axis must be Mx or My.", nameof(json));

        return new OpenSeesSectionInteractionParams
        {
            AxialForcesKn = result.AxialForcesKn.ToArray(),
            MaxCurvature = result.MaxCurvature,
            Increments = result.Increments,
            Axis = axis.Equals("My", StringComparison.OrdinalIgnoreCase) ? "My" : "Mx",
            TimeoutSeconds = result.TimeoutSeconds,
            ExecutablePath = result.ExecutablePath
        };
    }
}
