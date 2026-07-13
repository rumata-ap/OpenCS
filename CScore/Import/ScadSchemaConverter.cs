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

    public static FemElement[] ToFemElements(ScadSchemaData data, int schemaId)
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
    /// Строит FemMember: элементы, входящие хотя бы в одну именованную группу SCAD,
    /// группируются по имени группы (Tag = имя группы). Остальные элементы группируются
    /// по номеру жёсткости — так же, как для схем ЛираСАПР (см. LiraSchemaConverter).
    /// </summary>
    public static FemMember[] ToFemMembers(ScadSchemaData data, int schemaId)
    {
        var elementById = data.Elements.ToDictionary(e => e.Id);
        var stiffNames  = data.Stiffnesses.ToDictionary(s => s.Id, s => s.Name);
        var assigned    = new HashSet<int>();
        var members     = new List<FemMember>();

        foreach (var group in data.Groups)
        {
            var ids = group.ElementIds
                .Where(id => elementById.ContainsKey(id) && assigned.Add(id))
                .ToArray();
            if (ids.Length == 0) continue;

            bool allBars = ids.All(id => elementById[id].NodeIds.Length == 2);
            members.Add(new FemMember
            {
                SchemaId    = schemaId,
                Tag         = group.Name,
                MemberType  = allBars ? "beam" : "shell",
                ElemIdsJson = JsonSerializer.Serialize(ids),
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

            members.Add(new FemMember
            {
                SchemaId    = schemaId,
                Tag         = tag,
                MemberType  = allBars ? "beam" : "shell",
                ElemIdsJson = JsonSerializer.Serialize(ids),
            });
        }

        return members.ToArray();
    }
}
