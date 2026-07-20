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

    /// <summary>Тип: "beam" | "shell". По умолчанию "beam" — как единственный тип, который
    /// поддерживался до появления импорта пластин в сетку.</summary>
    public string ElemType { get; set; } = "beam";

    /// <summary>JSON-массив идентификаторов узлов элемента.</summary>
    public string NodeIdsJson { get; set; } = "[]";

    /// <summary>Тег исходного конструктивного элемента до дискретизации.</summary>
    public string? SourceMemberTag { get; set; }

    /// <summary>Тег сечения/жёсткости из источника импорта (SCAD/Lira). Null для стержней,
    /// дискретизированных из конструктивной модели редактора (у них CrossSectionId).</summary>
    public string? SectionTag  { get; set; }
    /// <summary>Тег материала из источника импорта. Null, если не распознан.</summary>
    public string? MaterialTag { get; set; }
    /// <summary>Толщина оболочки, м (для ElemType="shell", из источника импорта).</summary>
    public double? ThicknessM  { get; set; }

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

    /// <summary>Идентификатор третьего узла элемента (для 3/4-узловых оболочек). Null для стержней.</summary>
    public int? Node3 => ReadNodeId(2);

    /// <summary>Идентификатор четвёртого узла элемента (для 4-узловых оболочек). Null для 3-узловых и стержней.</summary>
    public int? Node4 => ReadNodeId(3);

    int? ReadNodeId(int index)
    {
        var nodeIds = JsonSerializer.Deserialize<int[]>(NodeIdsJson) ?? [];
        return index < nodeIds.Length ? nodeIds[index] : null;
    }
}
