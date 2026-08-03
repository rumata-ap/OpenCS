using System.Text.Json;

namespace OpenCS.Tasks;

/// <summary>Параметры задачи одноосного moment–curvature OpenSees.</summary>
public sealed class OpenSeesSectionParams
{
    /// <summary>Приращение кривизны на одном шаге в 1/м.</summary>
    public double CurvatureStep { get; init; } = 0.0005;

    /// <summary>Максимальное число шагов до принудительной остановки.</summary>
    public int MaxSteps { get; init; } = 200;

    /// <summary>Направление изгиба: Mx или My.</summary>
    public string Axis { get; init; } = "Mx";

    /// <summary>Таймаут внешнего процесса в секундах.</summary>
    public int TimeoutSeconds { get; init; } = 300;

    /// <summary>Необязательный явный путь к OpenSees executable.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Разбирает ParamsJson и проверяет диапазоны параметров.</summary>
    public static OpenSeesSectionParams Parse(string? json)
    {
        OpenSeesSectionParams result;
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
        {
            result = new OpenSeesSectionParams();
        }
        else
        {
            result = JsonSerializer.Deserialize<OpenSeesSectionParams>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new OpenSeesSectionParams();
        }

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

        return new OpenSeesSectionParams
        {
            CurvatureStep = curvatureStep,
            MaxSteps = maxSteps,
            Axis = axis.Equals("My", StringComparison.OrdinalIgnoreCase) ? "My" : "Mx",
            TimeoutSeconds = result.TimeoutSeconds,
            ExecutablePath = result.ExecutablePath
        };
    }
}
