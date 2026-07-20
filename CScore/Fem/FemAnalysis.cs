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

    public FemLoadExpression GetLoadExpression() => FemLoadExpression.Parse(LoadExpressionJson);

    public void SetLoadExpression(FemLoadExpression expression) => LoadExpressionJson = expression.ToJson();

    /// <summary>Инвалидирует результат после изменения параметров постановки.</summary>
    public void InvalidateResult()
    {
        ResultId = null;
        Status = "created";
    }
}
