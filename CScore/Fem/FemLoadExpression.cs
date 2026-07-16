using System.Text.Json;

namespace CScore.Fem;

/// <summary>Режим выбора загружений для постановки FEM-анализа.</summary>
public enum FemLoadExpressionMode
{
    Single,
    Sum,
    All,
    Sp20
}

/// <summary>Слагаемое пользовательской формулы загружения.</summary>
public sealed class FemLoadTerm
{
    public int LoadCaseId { get; init; }
    public double Coefficient { get; init; }
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
