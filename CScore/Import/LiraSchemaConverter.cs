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
                return new FemElement
                {
                    SchemaId    = schemaId,
                    ElemTag     = e.Id.ToString(),
                    ElemType    = "beam",
                    NodeIdsJson = JsonSerializer.Serialize(e.NodeIds),
                    SectionTag  = stiff?.Name,
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
}
