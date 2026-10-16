using System.Text.Json;
using System.Text.Json.Serialization;

namespace CScore.Fem;

/// <summary>Режим выбора загружений для постановки FEM-анализа.</summary>
public enum FemLoadExpressionMode
{
    Single,
    Sum,
    All,
    Sp20
}

/// <summary>
/// Слагаемое пользовательской формулы загружения. Решатели читают только <see cref="Coefficient"/>; роль и режим —
/// для пересчёта «авто»-коэффициента по СП 20 (<see cref="Combinations.FemCombinationCoefficients"/>). Без
/// <see cref="Mode"/> (старые формулы) слагаемое — заданное число.
/// </summary>
public sealed class FemLoadTerm
{
    /// <summary>Режим «авто»: коэффициент вычисляется по роли, загружению и типу комбинации.</summary>
    public const string AutoMode = "auto";

    /// <summary>Режим «задано»: коэффициент введён пользователем.</summary>
    public const string ManualMode = "manual";

    public int LoadCaseId { get; init; }
    public double Coefficient { get; init; }

    /// <summary>Роль в сочетании СП 20 (<see cref="FemLoadTermRoles"/>); null — не задана.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role { get; init; }

    /// <summary><see cref="AutoMode"/> | <see cref="ManualMode"/>; null — заданное число.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mode { get; init; }

    /// <summary>Коэффициент вычисляется по роли.</summary>
    [JsonIgnore]
    public bool IsAuto => Mode == AutoMode && Role != null;
}

/// <summary>Роли слагаемых в JSON формулы — строковые имена <see cref="CScore.Combinations.Sp20TermRole"/>.</summary>
public static class FemLoadTermRoles
{
    public const string Permanent = "permanent";
    public const string PermanentReduced = "permanent_reduced";
    public const string Leading = "leading";
    public const string Second = "second";
    public const string Other = "other";
    public const string Accidental = "accidental";

    /// <summary>Имя роли для JSON.</summary>
    public static string ToKey(CScore.Combinations.Sp20TermRole role) => role switch
    {
        CScore.Combinations.Sp20TermRole.Permanent => Permanent,
        CScore.Combinations.Sp20TermRole.PermanentReduced => PermanentReduced,
        CScore.Combinations.Sp20TermRole.Leading => Leading,
        CScore.Combinations.Sp20TermRole.Second => Second,
        CScore.Combinations.Sp20TermRole.Other => Other,
        _ => Accidental
    };

    /// <summary>Роль по имени из JSON; неизвестное имя — null.</summary>
    public static CScore.Combinations.Sp20TermRole? Parse(string? key) => key switch
    {
        Permanent => CScore.Combinations.Sp20TermRole.Permanent,
        PermanentReduced => CScore.Combinations.Sp20TermRole.PermanentReduced,
        Leading => CScore.Combinations.Sp20TermRole.Leading,
        Second => CScore.Combinations.Sp20TermRole.Second,
        Other => CScore.Combinations.Sp20TermRole.Other,
        Accidental => CScore.Combinations.Sp20TermRole.Accidental,
        _ => null
    };
}

/// <summary>Сериализуемое выражение выбора одного или нескольких загружений.</summary>
public sealed class FemLoadExpression
{
    public FemLoadExpressionMode Mode { get; init; } = FemLoadExpressionMode.Single;
    public List<int> LoadCaseIds { get; init; } = [];
    public List<FemLoadTerm> Terms { get; init; } = [];
    public string? CombinationType { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static FemLoadExpression Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new();

        return JsonSerializer.Deserialize<FemLoadExpression>(json) ?? new();
    }
}
