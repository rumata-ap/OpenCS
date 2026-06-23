namespace CScore.Fem;

/// <summary>Конечный элемент сетки.</summary>
public class FemElement
{
    public int     Id          { get; set; }
    public int     SchemaId    { get; set; }
    public string  ElemTag     { get; set; } = "";
    /// <summary>Тип элемента: "beam" | "shell" | "truss"</summary>
    public string  ElemType    { get; set; } = "beam";
    /// <summary>JSON-массив ID узлов: [n1, n2] для балки, [n1,n2,n3,n4] для оболочки.</summary>
    public string  NodeIdsJson { get; set; } = "[]";
    public string? SectionTag  { get; set; }
    public string? MaterialTag { get; set; }
}
