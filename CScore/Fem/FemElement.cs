using System.Text.Json;

namespace CScore.Fem;

/// <summary>Конечный элемент сетки, созданный при дискретизации конструктивного элемента.</summary>
public class FemElement
{
    /// <summary>Идентификатор элемента в базе данных.</summary>
    public int Id { get; set; }

    /// <summary>Идентификатор FEM-схемы.</summary>
    public int SchemaId { get; set; }

    /// <summary>Тег элемента в пределах FEM-схемы.</summary>
    public string ElemTag { get; set; } = "";

    /// <summary>JSON-массив идентификаторов узлов элемента.</summary>
    public string NodeIdsJson { get; set; } = "[]";

    /// <summary>Тег исходного конструктивного элемента до дискретизации.</summary>
    public string? SourceMemberTag { get; set; }

    /// <summary>Идентификатор назначенного сечения.</summary>
    public int? CrossSectionId { get; set; }

    /// <summary>Стратегия определения крутильной жёсткости.</summary>
    public string GjStrategy { get; set; } = "manual";

    /// <summary>Ручное значение GJ, Н·м².</summary>
    public double? GjManualValue { get; set; }

    /// <summary>Идентификатор задачи определения крутильной жёсткости.</summary>
    public int? GjTorsionTaskId { get; set; }

    /// <summary>Идентификатор первого узла элемента.</summary>
    public int? Node1 => ReadNodeId(0);

    /// <summary>Идентификатор второго узла элемента.</summary>
    public int? Node2 => ReadNodeId(1);

    int? ReadNodeId(int index)
    {
        var nodeIds = JsonSerializer.Deserialize<int[]>(NodeIdsJson) ?? [];
        return index < nodeIds.Length ? nodeIds[index] : null;
    }
}
