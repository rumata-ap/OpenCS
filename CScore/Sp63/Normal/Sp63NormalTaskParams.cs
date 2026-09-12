using System.Text.Json;
using CScore;

namespace CScore.Sp63.Normal;

/// <summary>JSON-контракт параметров задачи упрощённой проверки нормального сечения.</summary>
public sealed class Sp63NormalTaskParams
{
    /// <summary>Идентификатор формы: rectangular.</summary>
    public string ShapeKind { get; set; } = "rectangular";

    /// <summary>Идентификатор оси: Mx или My.</summary>
    public string Axis { get; set; } = "Mx";

    /// <summary>Идентификатор схемы: statically_determinate или statically_indeterminate.</summary>
    public string StructuralScheme { get; set; } = "statically_indeterminate";

    /// <summary>Длина элемента или расстояние между закреплениями, м.</summary>
    public double? ElementLengthOrRestraintDistance { get; set; }

    /// <summary>Расчётная длина для проверки устойчивости, м.</summary>
    public double? EffectiveLengthL0 { get; set; }

    /// <summary>Режим устойчивости: member или section_only_explicit.</summary>
    public string StabilityMode { get; set; } = "member";

    /// <summary>Относительная длительная составляющая момента ψ.</summary>
    public double Psi { get; set; }

    /// <summary>Порог гибкости l0/h.</summary>
    public double SlendernessThreshold { get; set; } = 14.0;

    /// <summary>Использовать ручные значения N, Mx и My.</summary>
    public bool UseManualForces { get; set; }

    /// <summary>Ручная продольная сила, кН.</summary>
    public double? N { get; set; }

    /// <summary>Ручной момент Mx, кН·м.</summary>
    public double? Mx { get; set; }

    /// <summary>Ручной момент My, кН·м.</summary>
    public double? My { get; set; }

    /// <summary>Разбирает JSON или возвращает безопасные параметры по умолчанию.</summary>
    public static Sp63NormalTaskParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Sp63NormalTaskParams();

        try
        {
            return JsonSerializer.Deserialize<Sp63NormalTaskParams>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new Sp63NormalTaskParams();
        }
        catch (JsonException)
        {
            return new Sp63NormalTaskParams { ShapeKind = "" };
        }
    }

    /// <summary>Сериализует параметры с именами свойств в camelCase.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    /// <summary>Преобразует строковые значения JSON в типизированные параметры.</summary>
    public bool TryToOptions(out Sp63NormalOptions options, out string errorCode)
    {
        options = null!;
        errorCode = "";

        if (!Enum.TryParse<Sp63NormalShapeKind>(ShapeKind, true, out var shapeKind) ||
            !string.Equals(ShapeKind, "rectangular", StringComparison.OrdinalIgnoreCase))
            return Invalid("invalid_shape_kind", out errorCode);

        if (!Enum.TryParse<Sp63NormalAxis>(Axis, true, out var axis))
            return Invalid("invalid_axis", out errorCode);

        Sp63StructuralScheme scheme;
        if (string.Equals(StructuralScheme, "statically_determinate",
                          StringComparison.OrdinalIgnoreCase))
            scheme = Sp63StructuralScheme.StaticallyDeterminate;
        else if (string.Equals(StructuralScheme, "statically_indeterminate",
                               StringComparison.OrdinalIgnoreCase))
            scheme = Sp63StructuralScheme.StaticallyIndeterminate;
        else
            return Invalid("invalid_structural_scheme", out errorCode);

        Sp63NormalStabilityMode stabilityMode;
        if (string.Equals(StabilityMode, "member", StringComparison.OrdinalIgnoreCase))
            stabilityMode = Sp63NormalStabilityMode.Member;
        else if (string.Equals(StabilityMode, "section_only_explicit",
                               StringComparison.OrdinalIgnoreCase))
            stabilityMode = Sp63NormalStabilityMode.SectionOnlyExplicit;
        else
            return Invalid("invalid_stability_mode", out errorCode);

        if (!IsOptionalPositive(ElementLengthOrRestraintDistance) ||
            !IsOptionalPositive(EffectiveLengthL0))
            return Invalid("invalid_length", out errorCode);
        if (!double.IsFinite(Psi) || !double.IsFinite(SlendernessThreshold) ||
            SlendernessThreshold <= 0)
            return Invalid("invalid_stability_parameters", out errorCode);

        options = new Sp63NormalOptions(shapeKind, axis, new Sp63MemberContext(
            ElementLengthOrRestraintDistance, scheme, EffectiveLengthL0,
            stabilityMode, Psi, SlendernessThreshold));
        return true;
    }

    /// <summary>Создаёт строку нагрузки из ручных полей задачи.</summary>
    public LoadItem ToLoadItem() => new()
    {
        N = N ?? 0.0,
        Mx = Mx ?? 0.0,
        My = My ?? 0.0
    };

    static bool IsOptionalPositive(double? value) =>
        value is null || (double.IsFinite(value.Value) && value.Value > 0);

    static bool Invalid(string code, out string errorCode)
    {
        errorCode = code;
        return false;
    }
}
