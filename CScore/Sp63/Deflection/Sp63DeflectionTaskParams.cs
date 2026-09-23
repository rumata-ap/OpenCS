using System.Text.Json;
using System.Text.Json.Serialization;
using CScore.Sp63.CrackWidth;
using CScore.Sp63.Normal;

namespace CScore.Sp63.Deflection;

/// <summary>JSON-контракт параметров отдельной формульной задачи прогиба.</summary>
public sealed class Sp63DeflectionTaskParams
{
    /// <summary>Тип сечения; поддерживается rectangular.</summary>
    public string ShapeKind { get; set; } = "rectangular";
    /// <summary>Ось изгиба Mx или My.</summary>
    public string Axis { get; set; } = "Mx";
    /// <summary>Обязательный идентификатор одной из трёх схем.</summary>
    public string? Scheme { get; set; }
    /// <summary>Пролёт элемента l, м.</summary>
    public double SpanM { get; set; }
    /// <summary>Предельно допустимый прогиб fult, мм.</summary>
    public double DeflectionLimitMm { get; set; }
    /// <summary>Влажность: above_75, 40_75 или below_40.</summary>
    public string Humidity { get; set; } = "40_75";
    /// <summary>Режим длительных усилий: total_only, share или manual.</summary>
    public string ForcesMode { get; set; } = "total_only";
    /// <summary>Доля длительной части полной нагрузки в режиме share.</summary>
    public double LongTermShare { get; set; } = 1.0;
    /// <summary>Признак ручного задания полной нагрузки.</summary>
    public bool UseManualForces { get; set; }
    /// <summary>Полные усилия, если используется ручная нагрузка.</summary>
    public double? N { get; set; }
    /// <summary>Полный момент Mx, кН·м.</summary>
    public double? Mx { get; set; }
    /// <summary>Полный момент My, кН·м.</summary>
    public double? My { get; set; }
    /// <summary>Ручная длительная продольная сила, кН.</summary>
    public double? NLongManual { get; set; }
    /// <summary>Ручной длительный момент Mx, кН·м.</summary>
    public double? MxLongManual { get; set; }
    /// <summary>Ручной длительный момент My, кН·м.</summary>
    public double? MyLongManual { get; set; }

    /// <summary>Ошибка синтаксиса входного JSON, если она была обнаружена при разборе.</summary>
    [JsonIgnore]
    public string? ParseError { get; private set; }

    /// <summary>Разбирает JSON, не подменяя отсутствующую схему значением по умолчанию.</summary>
    public static Sp63DeflectionTaskParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Sp63DeflectionTaskParams { ParseError = "invalid_json" };
        try
        {
            return JsonSerializer.Deserialize<Sp63DeflectionTaskParams>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new Sp63DeflectionTaskParams { ParseError = "invalid_json" };
        }
        catch (JsonException)
        {
            return new Sp63DeflectionTaskParams { ParseError = "invalid_json" };
        }
    }

    /// <summary>Сериализует контракт с именами свойств в camelCase.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    /// <summary>Проверяет JSON-контракт и создаёт типизированные параметры.</summary>
    public bool TryToOptions(out Sp63DeflectionOptions options, out string errorCode)
    {
        options = null!;
        errorCode = ParseError ?? "";
        if (ParseError is not null) return false;
        if (!string.Equals(ShapeKind, "rectangular", StringComparison.OrdinalIgnoreCase))
            return Invalid("invalid_shape_kind", out errorCode);
        if (!Enum.TryParse<Sp63NormalAxis>(Axis, true, out var axis) || !Enum.IsDefined(axis))
            return Invalid("invalid_axis", out errorCode);
        if (!Sp63DeflectionScheme.TryParse(Scheme, out var scheme))
            return Invalid("invalid_scheme", out errorCode);
        if (!double.IsFinite(SpanM) || SpanM <= 0)
            return Invalid("invalid_span", out errorCode);
        if (!double.IsFinite(DeflectionLimitMm) || DeflectionLimitMm <= 0)
            return Invalid("invalid_deflection_limit", out errorCode);
        if (!Sp63CrackWidthTaskParams.TryParseHumidity(Humidity, out var humidity))
            return Invalid("invalid_humidity", out errorCode);
        if (!TryParseForcesMode(ForcesMode, out var forcesMode))
            return Invalid("invalid_forces_mode", out errorCode);
        if (!double.IsFinite(LongTermShare) || LongTermShare is < 0 or > 1)
            return Invalid("invalid_long_term_share", out errorCode);

        if (UseManualForces && (!Finite(N) || !Finite(Mx) || !Finite(My)))
            return Invalid("missing_manual_load", out errorCode);
        if (forcesMode == Sp63DeflectionForcesMode.Manual &&
            (!Finite(NLongManual) || !Finite(MxLongManual) || !Finite(MyLongManual)))
            return Invalid("missing_long_manual_load", out errorCode);

        options = new Sp63DeflectionOptions(Sp63NormalShapeKind.Rectangular, axis, scheme,
            SpanM, DeflectionLimitMm, humidity, forcesMode, LongTermShare,
            N, Mx, My, NLongManual, MxLongManual, MyLongManual);
        return true;
    }

    static bool TryParseForcesMode(string? value, out Sp63DeflectionForcesMode mode)
    {
        mode = value switch
        {
            "total_only" => Sp63DeflectionForcesMode.TotalOnly,
            "share" => Sp63DeflectionForcesMode.Share,
            "manual" => Sp63DeflectionForcesMode.Manual,
            _ => (Sp63DeflectionForcesMode)(-1)
        };
        return Enum.IsDefined(mode);
    }

    static bool Finite(double? value) => value is { } number && double.IsFinite(number);
    static bool Invalid(string code, out string errorCode) { errorCode = code; return false; }
}
