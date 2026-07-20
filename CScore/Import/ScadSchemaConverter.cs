using System.Text.Json;
using CScore.Fem;

namespace CScore.Import;

/// <summary>Конвертирует сырые данные SCAD (текстовый формат) в доменные объекты FEM-схемы OpenCS.</summary>
public static class ScadSchemaConverter
{
    public static FemNode[] ToFemNodes(ScadSchemaData data, int schemaId) =>
        data.Nodes.Select(n => new FemNode
        {
            SchemaId = schemaId,
            NodeTag  = n.Id.ToString(),
            X = n.X, Y = n.Y, Z = n.Z,
        }).ToArray();

    public static FemMember[] ToFemMembers(ScadSchemaData data, int schemaId)
    {
        var stiffNames = data.Stiffnesses.ToDictionary(s => s.Id, s => s.Name);
        var stiffThk   = data.Stiffnesses.ToDictionary(s => s.Id, s => s.ThicknessM);
        return data.Elements.Select(e =>
        {
            stiffNames.TryGetValue(e.StiffnessId, out var name);
            stiffThk.TryGetValue(e.StiffnessId, out var thk);
            return new FemMember
            {
                SchemaId    = schemaId,
                ElemTag     = e.Id.ToString(),
                ElemType    = e.NodeIds.Length == 2 ? "beam" : "shell",
                NodeIdsJson = JsonSerializer.Serialize(e.NodeIds),
                SectionTag  = name,
                ThicknessM  = thk,
            };
        }).ToArray();
    }

    /// <summary>
    /// Создаёт узлы КЭ-сетки напрямую из данных SCAD — модель SCAD уже является готовой сеткой,
    /// поэтому импорт минует конструктивный слой (FemNode/FemMember) и не требует последующей
    /// дискретизации.
    /// </summary>
    public static FemMeshNode[] ToFemMeshNodes(ScadSchemaData data, int schemaId) =>
        data.Nodes.Select(n => new FemMeshNode
        {
            SchemaId = schemaId,
            NodeTag  = n.Id.ToString(),
            X = n.X, Y = n.Y, Z = n.Z,
        }).ToArray();

    /// <summary>Создаёт элементы КЭ-сетки (стержни и оболочки вперемешку, по числу узлов) напрямую из данных SCAD.</summary>
    public static FemElement[] ToFemMeshElements(ScadSchemaData data, int schemaId)
    {
        var stiffNames = data.Stiffnesses.ToDictionary(s => s.Id, s => s.Name);
        var stiffThk   = data.Stiffnesses.ToDictionary(s => s.Id, s => s.ThicknessM);
        return data.Elements.Select(e =>
        {
            stiffNames.TryGetValue(e.StiffnessId, out var name);
            stiffThk.TryGetValue(e.StiffnessId, out var thk);
            return new FemElement
            {
                SchemaId    = schemaId,
                ElemTag     = e.Id.ToString(),
                ElemType    = e.NodeIds.Length == 2 ? "beam" : "shell",
                NodeIdsJson = JsonSerializer.Serialize(e.NodeIds),
                SectionTag  = name,
                ThicknessM  = thk,
            };
        }).ToArray();
    }

    /// <summary>
    /// Строит FemMemberGroup: элементы, входящие хотя бы в одну именованную группу SCAD,
    /// группируются по имени группы (Tag = имя группы). Остальные элементы группируются
    /// по номеру жёсткости — так же, как для схем ЛираСАПР (см. LiraSchemaConverter).
    /// </summary>
    public static FemMemberGroup[] ToFemMemberGroups(ScadSchemaData data, int schemaId)
    {
        var elementById = data.Elements.ToDictionary(e => e.Id);
        var stiffNames  = data.Stiffnesses.ToDictionary(s => s.Id, s => s.Name);
        var assigned    = new HashSet<int>();
        var groups      = new List<FemMemberGroup>();

        foreach (var group in data.Groups)
        {
            var ids = group.ElementIds
                .Where(id => elementById.ContainsKey(id) && assigned.Add(id))
                .ToArray();
            if (ids.Length == 0) continue;

            bool allBars = ids.All(id => elementById[id].NodeIds.Length == 2);
            groups.Add(new FemMemberGroup
            {
                SchemaId       = schemaId,
                Tag            = group.Name,
                MemberType     = allBars ? "beam" : "shell",
                MemberTagsJson = JsonSerializer.Serialize(ids),
            });
        }

        var remaining = data.Elements.Where(e => !assigned.Contains(e.Id));
        foreach (var g in remaining.GroupBy(e => e.StiffnessId).OrderBy(g => g.Key))
        {
            string tag = stiffNames.TryGetValue(g.Key, out var name) && !string.IsNullOrEmpty(name)
                ? name
                : $"Жёсткость {g.Key}";
            var ids = g.Select(e => e.Id).ToArray();
            bool allBars = g.All(e => e.NodeIds.Length == 2);

            groups.Add(new FemMemberGroup
            {
                SchemaId       = schemaId,
                Tag            = tag,
                MemberType     = allBars ? "beam" : "shell",
                MemberTagsJson = JsonSerializer.Serialize(ids),
            });
        }

        return groups.ToArray();
    }
}
