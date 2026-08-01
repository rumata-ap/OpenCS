using System.Text.Json;

namespace OpenCS.Tasks;

/// <summary>Параметры задачи одноосного moment–curvature OpenSees.</summary>
public sealed class OpenSeesSectionParams
{
    /// <summary>Максимальная кривизна в 1/м.</summary>
    public double MaxCurvature { get; init; } = 0.01;

    /// <summary>Количество шагов кривизны.</summary>
    public int Increments { get; init; } = 20;

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

        return new OpenSeesSectionParams
        {
            MaxCurvature = result.MaxCurvature,
            Increments = result.Increments,
            Axis = axis.Equals("My", StringComparison.OrdinalIgnoreCase) ? "My" : "Mx",
            TimeoutSeconds = result.TimeoutSeconds,
            ExecutablePath = result.ExecutablePath
        };
    }
}
