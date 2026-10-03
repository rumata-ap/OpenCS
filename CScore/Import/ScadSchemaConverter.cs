using System.Text.Json;
using CScore.Fem;

namespace CScore.Import;

/// <summary>Конвертирует сырые данные SCAD (txt-экспорт или SCADAPIX.dll) в доменные объекты FEM-схемы OpenCS.</summary>
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
            Origin   = FemMember.MeshSourceImported,
        }).ToArray();

    /// <summary>
    /// Создаёт элементы КЭ-сетки (стержни и оболочки вперемешку) напрямую из данных SCAD.
    /// Угол оси выдачи усилий (<see cref="ScadSchemaData.PlateAxisAngles"/>) — только у оболочек.
    /// </summary>
    public static FemElement[] ToFemMeshElements(ScadSchemaData data, int schemaId)
    {
        var stiffById = data.Stiffnesses.ToDictionary(s => s.Id);
        return data.Elements.Select(e =>
        {
            stiffById.TryGetValue(e.StiffnessId, out var stiff);
            string type = ElemType(e);
            return new FemElement
            {
                SchemaId     = schemaId,
                ElemTag      = e.Id.ToString(),
                ElemType     = type,
                NodeIdsJson  = JsonSerializer.Serialize(e.NodeIds),
                SectionTag   = stiff?.Name,
                ThicknessM   = stiff?.ThicknessM,
                StiffnessNum = e.StiffnessId > 0 ? e.StiffnessId : null,
                LocalAxisAngleDeg = type == "shell" && data.PlateAxisAngles.TryGetValue(e.Id, out double a)
                    ? a : null,
                Origin       = FemMember.MeshSourceImported,
            };
        }).ToArray();
    }

    /// <summary>Тип КЭ OpenCS: по коду типа SCAD, для неизвестного типа — по числу узлов.</summary>
    static string ElemType(ScadElementRecord e) =>
        ScadElementKinds.Classify(e.TypeCode, e.NodeIds.Length) switch
        {
            ScadElementKind.Beam  => "beam",
            ScadElementKind.Shell => "shell",
            _                     => e.NodeIds.Length == 2 ? "beam" : "shell",
        };

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

    /// <summary>
    /// Группы по блокам SCAD: Tag = «Блок: имя» (безымянный — «Блок N» по порядку). Пересекаются
    /// с остальными группами, как кБ ЛИРЫ.
    /// </summary>
    public static FemMemberGroup[] ToFemMemberGroupsByBlocks(ScadSchemaData data, int schemaId) =>
        OverlappingGroups(data, schemaId, data.Blocks.Select((b, i) =>
            (string.IsNullOrWhiteSpace(b.Name) ? $"Блок {i + 1}" : $"Блок: {b.Name.Trim()}", b.ElementIds)));

    /// <summary>Группы по ЖБ-группам SCAD: Tag = «ЖБ: имя» (безымянная — «ЖБ: номер»).</summary>
    public static FemMemberGroup[] ToFemMemberGroupsByConcreteGroups(ScadSchemaData data, int schemaId) =>
        OverlappingGroups(data, schemaId, data.ConcreteGroups.Select(g =>
            ($"ЖБ: {(string.IsNullOrWhiteSpace(g.Name) ? g.Num.ToString() : g.Name.Trim())}", g.ElementIds)));

    /// <summary>Группы КЭ «Сталь: &lt;имя&gt;» по стальным группам SCAD (КЭ может входить и в другие группы).</summary>
    public static FemMemberGroup[] ToFemMemberGroupsBySteelGroups(ScadSchemaData data, int schemaId) =>
        OverlappingGroups(data, schemaId, data.SteelGroups.Select(g =>
            ($"Сталь: {(string.IsNullOrWhiteSpace(g.Name) ? g.Num.ToString() : g.Name.Trim())}", g.ElementIds)));

    static FemMemberGroup[] OverlappingGroups(ScadSchemaData data, int schemaId,
        IEnumerable<(string Tag, int[] ElementIds)> source)
    {
        var elementById = data.Elements.ToDictionary(e => e.Id);
        var groups = new List<FemMemberGroup>();
        foreach (var (tag, elementIds) in source)
        {
            var ids = elementIds.Where(elementById.ContainsKey).Distinct().ToArray();
            if (ids.Length == 0) continue;
            var types = ids.Select(id => ElemType(elementById[id])).Distinct().ToArray();
            groups.Add(new FemMemberGroup
            {
                SchemaId       = schemaId,
                Tag            = tag,
                MemberType     = types.Length == 1 ? types[0] : null, // смешанная — по составу КЭ
                MemberTagsJson = JsonSerializer.Serialize(ids),
            });
        }
        return groups.ToArray();
    }

    /// <summary>
    /// Жёсткости SCAD для fem_schema_stiffnesses: KindCode = <see cref="ScadStiffnessParams.ScadKindCode"/>,
    /// Params — исходная строка SCAD, SectionUnitM — единица сечений проекта. Записи без исходной
    /// строки пропускаются.
    /// </summary>
    public static LiraStiffnessRecord[] ToSchemaStiffnesses(ScadSchemaData data) =>
        data.Stiffnesses
            .Where(s => s.Text != null)
            .Select(s => new LiraStiffnessRecord(s.Id, ScadStiffnessParams.ScadKindCode, s.Name ?? "",
                s.Text!, data.SectionUnitM))
            .ToArray();
}
