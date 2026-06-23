using System.Text.Json;

namespace CScore.Fem;

/// <summary>
/// Параметры нормативной проверки конструктивного элемента (расчётные длины, коэффициенты).
/// Поля совпадают с OpenCS.Tasks.SteelCheckParams по именам, что позволяет передавать
/// ToJson() напрямую как ParamsJson для задачи "steel_check".
/// </summary>
public record FemDesignParams
{
    public double DesignLengthX { get; init; } = 3.0;
    public double DesignLengthY { get; init; } = 3.0;
    public double MuX           { get; init; } = 1.0;
    public double MuY           { get; init; } = 1.0;
    public double BetaM         { get; init; } = 1.0;
    public double GammaM        { get; init; } = 1.025;

    public string ToJson() => JsonSerializer.Serialize(this);

    public static FemDesignParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        return JsonSerializer.Deserialize<FemDesignParams>(json) ?? new();
    }
}
