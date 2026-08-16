using System.Text.Json;

namespace CScore;

/// <summary>
/// Параметры задачи «Полная кривизна», хранящиеся в CalcTask.ParamsJson.
/// </summary>
public sealed class TotalCurvatureTaskParams
{
    /// <summary>Ручная нормальная сила полной нагрузки, кН.</summary>
    public double? N { get; set; }

    /// <summary>Ручной момент Mx полной нагрузки, кН·м.</summary>
    public double? Mx { get; set; }

    /// <summary>Ручной момент My полной нагрузки, кН·м.</summary>
    public double? My { get; set; }

    /// <summary>Режим длительной части: total_only, share или manual.</summary>
    public string ForcesMode { get; set; } = "total_only";

    /// <summary>Доля длительной нагрузки от полной в режиме share.</summary>
    public double LongShare { get; set; } = 0.7;

    /// <summary>Ручной длительный момент Mx, кН·м, в режиме manual.</summary>
    public double? MxLongManual { get; set; }

    /// <summary>Ручной длительный момент My, кН·м, в режиме manual.</summary>
    public double? MyLongManual { get; set; }

    /// <summary>Разобрать JSON-параметры задачи.</summary>
    public static TotalCurvatureTaskParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new TotalCurvatureTaskParams();

        try
        {
            return JsonSerializer.Deserialize<TotalCurvatureTaskParams>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new TotalCurvatureTaskParams();
        }
        catch
        {
            return new TotalCurvatureTaskParams();
        }
    }

    /// <summary>Сериализовать параметры задачи.</summary>
    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Создать строку полной нагрузки для общего механизма исполнителя задач.</summary>
    public LoadItem ToLoadItem() => new()
    {
        N = N ?? 0.0,
        Mx = Mx ?? 0.0,
        My = My ?? 0.0
    };
}
