namespace CScore.Fem;

/// <summary>Сохраняемая постановка расчёта FEM-схемы.</summary>
public sealed class FemAnalysis
{
    public int Id { get; set; }
    public int SchemaId { get; set; }
    public string Tag { get; set; } = "";
    public string Kind { get; set; } = "linear";
    public string LoadExpressionJson { get; set; } = "{}";
    public string ParamsJson { get; set; } = "{}";
    public string Status { get; set; } = "created";
    public int? ResultId { get; set; }
    public string Created { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>
    /// Пояснение к статусу для дерева (пакет CSfea: «7 из 128 не дошли до λ = 1»); null — нет. Не хранится — выводится
    /// из результата при загрузке и после расчёта.
    /// </summary>
    public string? StatusNote { get; set; }

    public FemLoadExpression GetLoadExpression() => FemLoadExpression.Parse(LoadExpressionJson);

    public void SetLoadExpression(FemLoadExpression expression) => LoadExpressionJson = expression.ToJson();

    /// <summary>Инвалидирует результат после изменения параметров постановки.</summary>
    public void InvalidateResult()
    {
        ResultId = null;
        Status = "created";
        StatusNote = null;
    }
}
