namespace CScore.Fem;

/// <summary>Именованное определение одиночного загружения или сочетания нагрузок FEM-схемы.</summary>
public sealed class FemLoadDefinition
{
    public int Id { get; set; }
    public int SchemaId { get; set; }
    public string Tag { get; set; } = "";
    public string? Description { get; set; }
    public string ExpressionJson { get; set; } = "{}";
    public string SourceKind { get; set; } = "manual";
    public string? CombinationType { get; set; }

    /// <summary>Возвращает состав определения в сериализуемом общем контракте FEM.</summary>
    public FemLoadExpression GetExpression() => FemLoadExpression.Parse(ExpressionJson);

    /// <summary>Заменяет состав определения сериализуемым выражением.</summary>
    public void SetExpression(FemLoadExpression expression) => ExpressionJson = expression.ToJson();
}
