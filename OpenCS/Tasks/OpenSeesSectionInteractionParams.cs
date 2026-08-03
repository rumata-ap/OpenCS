using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCS.Tasks;

/// <summary>Параметры задачи одноосной диаграммы N-M через OpenSees.</summary>
public sealed class OpenSeesSectionInteractionParams
{
    /// <summary>Упорядоченный список продольных сил в кН.</summary>
    [JsonPropertyName("axialForces")]
    public IReadOnlyList<double> AxialForcesKn { get; init; } = [0];

    /// <summary>Приращение кривизны каждой точки на одном шаге в 1/м.</summary>
    public double CurvatureStep { get; init; } = 0.0005;

    /// <summary>Максимальное число шагов каждой точки до принудительной остановки.</summary>
    public int MaxSteps { get; init; } = 200;

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
        (double curvatureStep, int maxSteps) = OpenSeesCurvatureParameterMigration.Resolve(
            json, result.CurvatureStep, result.MaxSteps);
        if (!double.IsFinite(curvatureStep) || curvatureStep <= 0)
            throw new ArgumentException("CurvatureStep must be positive and finite.", nameof(json));
        if (maxSteps <= 0)
            throw new ArgumentException("MaxSteps must be positive.", nameof(json));
        if (result.TimeoutSeconds <= 0)
            throw new ArgumentException("TimeoutSeconds must be positive.", nameof(json));

        string axis = result.Axis.Trim();
        if (!axis.Equals("Mx", StringComparison.OrdinalIgnoreCase) &&
            !axis.Equals("My", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Axis must be Mx or My.", nameof(json));

        return new OpenSeesSectionInteractionParams
        {
            AxialForcesKn = result.AxialForcesKn.ToArray(),
            CurvatureStep = curvatureStep,
            MaxSteps = maxSteps,
            Axis = axis.Equals("My", StringComparison.OrdinalIgnoreCase) ? "My" : "Mx",
            TimeoutSeconds = result.TimeoutSeconds,
            ExecutablePath = result.ExecutablePath
        };
    }
}
