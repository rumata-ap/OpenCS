using System.Text.Json;

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

    int[]? _nodeIds;
    int[] NodeIds => _nodeIds ??= JsonSerializer.Deserialize<int[]>(NodeIdsJson) ?? [];

    public int? Node1 => NodeIds.Length > 0 ? NodeIds[0] : null;
    public int? Node2 => NodeIds.Length > 1 ? NodeIds[1] : null;
    public int? Node3 => NodeIds.Length > 2 ? NodeIds[2] : null;
    public int? Node4 => NodeIds.Length > 3 ? NodeIds[3] : null;
}
