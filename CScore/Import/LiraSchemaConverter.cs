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
    /// Создаёт массив FemElement из стержневых КЭ (2 узла).
    /// schemaId должен быть уже сохранён в БД.
    /// </summary>
    public static FemElement[] ToFemBarElements(LiraSchemaData data, int schemaId)
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

    /// <summary>
    /// Создаёт массив FemElement из пластинчатых/оболочечных КЭ (3 или 4 узла).
    /// </summary>
    public static FemElement[] ToFemShellElements(LiraSchemaData data, int schemaId)
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
                };
            })
            .ToArray();

    /// <summary>
    /// Создаёт FemMember для каждого уникального ID жёсткости стержней.
    /// Tag = имя жёсткости из CSV; ElemIdsJson = все элементы данной жёсткости.
    /// </summary>
    public static FemMember[] ToFemMembersByStiffness(LiraSchemaData data, int schemaId)
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
                return new FemMember
                {
                    SchemaId    = schemaId,
                    Tag         = tag,
                    MemberType  = "beam",
                    ElemIdsJson = JsonSerializer.Serialize(ids),
                };
            })
            .ToArray();
    }

    /// <summary>
    /// Создаёт FemMember для каждого уникального ID жёсткости пластин.
    /// </summary>
    public static FemMember[] ToFemMembersByPlateStiffness(LiraSchemaData data, int schemaId)
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
                return new FemMember
                {
                    SchemaId    = schemaId,
                    Tag         = tag,
                    MemberType  = "shell",
                    ElemIdsJson = JsonSerializer.Serialize(ids),
                };
            })
            .ToArray();
    }
}
