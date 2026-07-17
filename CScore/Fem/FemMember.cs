using System.Text.Json;

namespace CScore.Fem;

/// <summary>Конструктивный элемент — стержень между двумя FemNode. Создаётся кликом в 3D-редакторе.
/// Сечение и GJ-стратегия — его собственные поля (раньше жили на группе FemMemberGroup).
/// После явной команды «Дискретизировать» превращается в один или несколько расчётных КЭ.</summary>
public class FemMember
{
    public int     Id          { get; set; }
    public int     SchemaId    { get; set; }
    public string  ElemTag     { get; set; } = "";
    /// <summary>Тип: "beam" | "shell" | "truss"</summary>
    public string  ElemType    { get; set; } = "beam";
    /// <summary>JSON-массив тегов узлов: [n1, n2] для стержня, [n1,n2,n3,n4] для оболочки.</summary>
    public string  NodeIdsJson { get; set; } = "[]";
    public string? SectionTag  { get; set; }
    public string? MaterialTag { get; set; }
    /// <summary>Толщина оболочки, м (из SCAD GE/GEI). Null для стержней / если неизвестна.</summary>
    public double? ThicknessM  { get; set; }

    /// <summary>FK → cross_sections.id. Собственное сечение элемента (не группы).</summary>
    public int?    CrossSectionId   { get; set; }
    /// <summary>Стратегия крутильной жёсткости: "manual" | "saint_venant".</summary>
    public string  GjStrategy       { get; set; } = "manual";
    /// <summary>Ручное значение GJ, Н·м². Используется при GjStrategy="manual".</summary>
    public double? GjManualValue    { get; set; }
    /// <summary>FK → calc_tasks.id (Kind "torsion_bem"/"torsion_fem"). Используется при GjStrategy="saint_venant".</summary>
    public int?    GjTorsionTaskId  { get; set; }
    /// <summary>Целевая длина элемента сетки при дискретизации, м. Null означает значение по умолчанию.</summary>
    public double? TargetMeshLengthM { get; set; }

    int[]? _nodeIds;
    int[] NodeIds => _nodeIds ??= JsonSerializer.Deserialize<int[]>(NodeIdsJson) ?? [];

    public int? Node1 => NodeIds.Length > 0 ? NodeIds[0] : null;
    public int? Node2 => NodeIds.Length > 1 ? NodeIds[1] : null;
    public int? Node3 => NodeIds.Length > 2 ? NodeIds[2] : null;
    public int? Node4 => NodeIds.Length > 3 ? NodeIds[3] : null;
}
