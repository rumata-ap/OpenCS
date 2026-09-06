using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;

namespace OpenCS.Reporting;

/// <summary>Типизированный контракт результата определения момента трещинообразования.</summary>
public sealed class CrackingReportData
{
    /// <summary>Признак сходимости поиска момента трещинообразования.</summary>
    [JsonPropertyName("converged")] public bool Converged { get; set; }
    /// <summary>Продольная сила, кН.</summary>
    [JsonPropertyName("N")] public double N { get; set; }
    /// <summary>Компонента Mx момента трещинообразования, кН·м.</summary>
    [JsonPropertyName("Mx_crc")] public double MxCrc { get; set; }
    /// <summary>Компонента My момента трещинообразования, кН·м.</summary>
    [JsonPropertyName("My_crc")] public double MyCrc { get; set; }
    /// <summary>Модуль момента трещинообразования, кН·м.</summary>
    [JsonPropertyName("Mcrc")] public double Mcrc { get; set; }
    /// <summary>Максимальная растягивающая деформация бетона.</summary>
    [JsonPropertyName("eps_max_tension")] public double EpsMaxTension { get; set; }
    /// <summary>Предельная растягивающая деформация бетона.</summary>
    [JsonPropertyName("eps_tension_limit")] public double EpsTensionLimit { get; set; }
    /// <summary>Осевое перемещение плоскости, если она найдена.</summary>
    [JsonPropertyName("e0")] public double? E0 { get; set; }
    /// <summary>Кривизна ky, 1/м.</summary>
    [JsonPropertyName("ky")] public double? Ky { get; set; }
    /// <summary>Кривизна kz, 1/м.</summary>
    [JsonPropertyName("kz")] public double? Kz { get; set; }
    /// <summary>Признак сходимости плоскости деформаций.</summary>
    [JsonPropertyName("plane_converged")] public bool PlaneConverged { get; set; }
    /// <summary>Действия преднапряжения, если они были включены.</summary>
    [JsonPropertyName("prestress")] public PrestressActionsJsonModel? Prestress { get; set; }

    /// <summary>Разбирает фактический JSON результата cracking.</summary>
    /// <exception cref="JsonException">JSON пуст или имеет недопустимый формат.</exception>
    public static CrackingReportData Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new JsonException("Пустой результат cracking.");
        return JsonSerializer.Deserialize<CrackingReportData>(json)
            ?? throw new JsonException("Пустой результат cracking.");
    }
}
