using System.Text.Json;
using CScore.Fem;

namespace CScore.Import;

/// <summary>Конвертирует сырые данные ЛираСАПР в доменные объекты FEM-схемы OpenCS.</summary>
public static class LiraSchemaConverter
{
    /// <summary>
    /// Создаёт массив FemNode из данных ЛираСАПР.
    /// schemaId должен быть уже сохранён в БД.
    /// </summary>
    public static FemNode[] ToFemNodes(LiraSchemaData data, int schemaId)
        => data.Nodes
            .Select(n => new FemNode
            {
                SchemaId = schemaId,
                NodeTag  = n.Id.ToString(),
                X        = n.X,
                Y        = n.Y,
                Z        = n.Z,
                DofMask  = n.DofMask,
            })
            .ToArray();

    /// <summary>
    /// Создаёт массив конструктивных FemMember из стержневых КЭ (2 узла).
    /// schemaId должен быть уже сохранён в БД.
    /// </summary>
    public static FemMember[] ToFemBarMembers(LiraSchemaData data, int schemaId)
        => data.Elements
            .Where(e => e.NodeIds.Length == 2)
            .Select(e =>
            {
                var stiff = data.BarStiffnesses.FirstOrDefault(s => s.Id == e.StiffnessId);
                var tag   = stiff?.Name ?? (e.StiffnessId > 0 ? e.StiffnessId.ToString() : null);
                return new FemMember
                {
                    SchemaId    = schemaId,
                    ElemTag     = e.Id.ToString(),
                    ElemType    = "beam",
                    NodeIdsJson = JsonSerializer.Serialize(e.NodeIds),
                    SectionTag  = tag,
                };
            })
            .ToArray();

    /// <summary>
    /// Создаёт массив конструктивных FemMember из пластинчатых/оболочечных КЭ (3 или 4 узла).
    /// </summary>
    public static FemMember[] ToFemShellMembers(LiraSchemaData data, int schemaId)
        => data.Elements
            .Where(e => e.NodeIds.Length == 3 || e.NodeIds.Length == 4)
            .Select(e =>
            {
                var stiff = data.PlateStiffnesses.FirstOrDefault(s => s.Id == e.StiffnessId);
                var tag   = stiff?.Name ?? (e.StiffnessId > 0 ? e.StiffnessId.ToString() : null);
                return new FemMember
                {
                    SchemaId    = schemaId,
                    ElemTag     = e.Id.ToString(),
                    ElemType    = "shell",
                    NodeIdsJson = JsonSerializer.Serialize(e.NodeIds),
                    SectionTag  = tag,
                };
            })
            .ToArray();

    /// <summary>
    /// Создаёт узлы КЭ-сетки напрямую из данных ЛираСАПР — модель Лиры уже является готовой
    /// сеткой, поэтому импорт минует конструктивный слой (FemNode/FemMember) и не требует
    /// последующей дискретизации.
    /// </summary>
    public static FemMeshNode[] ToFemMeshNodes(LiraSchemaData data, int schemaId)
        => data.Nodes
            .Select(n => new FemMeshNode
            {
                SchemaId = schemaId,
                NodeTag  = n.Id.ToString(),
                X        = n.X,
                Y        = n.Y,
                Z        = n.Z,
            })
            .ToArray();

    /// <summary>Создаёт 2-узловые стержневые элементы КЭ-сетки напрямую из данных ЛираСАПР.</summary>
    public static FemElement[] ToFemMeshBarElements(LiraSchemaData data, int schemaId)
        => data.Elements
            .Where(e => e.NodeIds.Length == 2)
            .Select(e =>
            {
                var stiff = data.BarStiffnesses.FirstOrDefault(s => s.Id == e.StiffnessId);
                var tag   = stiff?.Name ?? (e.StiffnessId > 0 ? e.StiffnessId.ToString() : null);
                return new FemElement
                {
                    SchemaId    = schemaId,
                    ElemTag     = e.Id.ToString(),
                    ElemType    = "beam",
                    NodeIdsJson = JsonSerializer.Serialize(e.NodeIds),
                    SectionTag  = tag,
                };
            })
            .ToArray();

    /// <summary>Создаёт 3/4-узловые пластинчатые элементы КЭ-сетки напрямую из данных ЛираСАПР.</summary>
    public static FemElement[] ToFemMeshShellElements(LiraSchemaData data, int schemaId)
        => data.Elements
            .Where(e => e.NodeIds.Length == 3 || e.NodeIds.Length == 4)
            .Select(e =>
            {
                var stiff = data.PlateStiffnesses.FirstOrDefault(s => s.Id == e.StiffnessId);
                var tag   = stiff?.Name ?? (e.StiffnessId > 0 ? e.StiffnessId.ToString() : null);
                return new FemElement
                {
                    SchemaId    = schemaId,
                    ElemTag     = e.Id.ToString(),
                    ElemType    = "shell",
                    NodeIdsJson = JsonSerializer.Serialize(e.NodeIds),
                    SectionTag  = tag,
                    ThicknessM  = stiff?.H_mm is { } h ? h / 1000.0 : null,
                };
            })
            .ToArray();

    /// <summary>
    /// Создаёт FemMemberGroup для каждого уникального ID жёсткости стержней.
    /// Tag = имя жёсткости из CSV; MemberTagsJson = все элементы данной жёсткости.
    /// </summary>
    public static FemMemberGroup[] ToFemMemberGroupsByStiffness(LiraSchemaData data, int schemaId)
    {
        var barElements = data.Elements
            .Where(e => e.NodeIds.Length == 2)
            .ToList();
        var stiffNames = data.BarStiffnesses.ToDictionary(s => s.Id, s => s.Name);

        return barElements
            .GroupBy(e => e.StiffnessId)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var tag  = stiffNames.TryGetValue(g.Key, out var name) ? name : $"Жёсткость {g.Key}";
                var ids  = g.Select(e => e.Id).ToArray();
                return new FemMemberGroup
                {
                    SchemaId       = schemaId,
                    Tag            = tag,
                    MemberType     = "beam",
                    MemberTagsJson = JsonSerializer.Serialize(ids),
                };
            })
            .ToArray();
    }

    /// <summary>
    /// Создаёт FemMemberGroup для каждого уникального ID жёсткости пластин.
    /// </summary>
    public static FemMemberGroup[] ToFemMemberGroupsByPlateStiffness(LiraSchemaData data, int schemaId)
    {
        var shellElements = data.Elements
            .Where(e => e.NodeIds.Length == 3 || e.NodeIds.Length == 4)
            .ToList();
        if (shellElements.Count == 0) return [];

        var stiffNames = data.PlateStiffnesses.ToDictionary(s => s.Id, s => s.Name);

        return shellElements
            .GroupBy(e => e.StiffnessId)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var tag  = stiffNames.TryGetValue(g.Key, out var name) ? name : $"Жёсткость пластины {g.Key}";
                var ids  = g.Select(e => e.Id).ToArray();
                return new FemMemberGroup
                {
                    SchemaId       = schemaId,
                    Tag            = tag,
                    MemberType     = "shell",
                    MemberTagsJson = JsonSerializer.Serialize(ids),
                };
            })
            .ToArray();
    }

    /// <summary>
    /// Создаёт FemMemberGroup из конструктивных блоков ЛираСАПР (таблица 31).
    /// Tag = "{Тип} [{Этаж}]" (или просто Тип, если этаж пустой).
    /// </summary>
    public static FemMemberGroup[] ToFemMemberGroupsByConstructiveBlocks(LiraSchemaData data, int schemaId)
    {
        if (data.ConstructiveBlocks.Count == 0) return [];

        return data.ConstructiveBlocks
            .Select(b =>
            {
                var tag = string.IsNullOrWhiteSpace(b.Floor)
                    ? b.Type
                    : $"{b.Type} [{b.Floor}]";
                if (!string.IsNullOrWhiteSpace(b.Mark))
                    tag += $" {b.Mark}";
                return new FemMemberGroup
                {
                    SchemaId       = schemaId,
                    Tag            = tag,
                    MemberType     = null, // тип определяется по составу КЭ
                    MemberTagsJson = JsonSerializer.Serialize(b.ElementIds),
                };
            })
            .ToArray();
    }
}
