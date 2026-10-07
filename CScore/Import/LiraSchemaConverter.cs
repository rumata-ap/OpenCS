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
                Origin   = FemMember.MeshSourceImported,
            })
            .ToArray();

    /// <summary>Происхождение ГУ, перенесённых из ЛИРЫ.</summary>
    public static readonly string BoundaryOrigin = FemLoadOrigin.Import("lira");

    /// <summary>
    /// Закрепления узлов сетки из масок узлов ЛИРЫ (биты 0–5; бит 6 — депланация — не переносится).
    /// </summary>
    public static FemMeshNodeSupport[] ToFemMeshNodeSupports(LiraSchemaData data)
        => data.Nodes
            .Where(n => (n.DofMask & FemBoundaryDofs.All) != 0)
            .Select(n => new FemMeshNodeSupport
            {
                NodeTag = n.Id.ToString(),
                Mask    = n.DofMask & FemBoundaryDofs.All,
                Origin  = BoundaryOrigin,
            })
            .ToArray();

    /// <summary>
    /// ГУ КЭ из ЛИРЫ по тегу: C1 упругого основания пластин (таблица 5). Только пластины, переносимые в сетку.
    /// </summary>
    public static Dictionary<string, FemElementBoundaryProps> ToFemElementBoundaryProps(LiraSchemaData data)
    {
        var shells = data.Elements.Where(e => e.NodeIds.Length is 3 or 4).Select(e => e.Id).ToHashSet();
        return data.PlateFoundationC1
            .Where(kv => kv.Value > 0 && shells.Contains(kv.Key))
            .ToDictionary(kv => kv.Key.ToString(), kv => new FemElementBoundaryProps(null, null, kv.Value), StringComparer.Ordinal);
    }

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
                    StiffnessNum = e.StiffnessId > 0 ? e.StiffnessId : null,
                    ReinforcementTypeIds = ReinforcementKey(data, e.Id),
                    Origin      = FemMember.MeshSourceImported,
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
                    StiffnessNum = e.StiffnessId > 0 ? e.StiffnessId : null,
                    ThicknessM  = stiff?.H_mm is > 0 and var h ? h / 1000.0 : null,
                    ReinforcementTypeIds = ReinforcementKey(data, e.Id),
                    LocalAxisAngleDeg = data.PlateAxisAngles.TryGetValue(e.Id, out double angle) ? angle : null,
                    Origin      = FemMember.MeshSourceImported,
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
                return ImportedMeshGroup(schemaId, tag, "beam", ids);
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
                return ImportedMeshGroup(schemaId, tag, "shell", ids);
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
                // Номер блока ЛИРА («Блок N» в карточке КЭ) — без него группы одного типа неразличимы
                // (все безымянные блоки импортируются как «Блок»).
                var tag = LiraBlockTags.Format(b.Id, b.Type, b.Floor, b.Mark);
                return ImportedMeshGroup(schemaId, tag, null, b.ElementIds);
            })
            .ToArray();
    }

    /// <summary>
    /// Создаёт FemMemberGroup пластин для каждой пары «набор ТЗА × жёсткость пластины»:
    /// у КЭ группы одинаковые армирование и толщина, поэтому группу можно проверять одним сечением.
    /// Tag = "ТЗА {номера} · {жёсткость}". КЭ без ТЗА в группы не попадают.
    /// </summary>
    public static FemMemberGroup[] ToFemMemberGroupsByReinforcementTypes(LiraSchemaData data, int schemaId)
    {
        if (data.ElementReinforcementTypes.Count == 0) return [];
        var stiffNames = data.PlateStiffnesses.ToDictionary(s => s.Id, s => s.Name);

        return data.Elements
            .Where(e => e.NodeIds.Length == 3 || e.NodeIds.Length == 4)
            .Select(e => (Elem: e, Key: ReinforcementKey(data, e.Id)))
            .Where(x => x.Key != null)
            .GroupBy(x => (x.Key!, x.Elem.StiffnessId))
            .OrderBy(g => g.Key.StiffnessId)
            .ThenBy(g => g.Key.Item1, Comparer<string>.Create(CompareKeys))
            .Select(g =>
            {
                var stiff = stiffNames.TryGetValue(g.Key.StiffnessId, out var name) ? name : $"Жёсткость пластины {g.Key.StiffnessId}";
                return ImportedMeshGroup(schemaId, $"ТЗА {g.Key.Item1} · {stiff}", "shell", g.Select(x => x.Elem.Id).ToArray());
            })
            .ToArray();
    }

    /// <summary>Номера ТЗА КЭ без повторов по возрастанию, через пробел; null — ТЗА нет.</summary>
    public static string? ReinforcementKey(LiraSchemaData data, int elemId)
        => data.ElementReinforcementTypes.TryGetValue(elemId, out var ids) && ids.Length > 0
            ? string.Join(" ", ids.Distinct().Order())
            : null;

    /// <summary>Сравнение ключей «1 2 4» как последовательностей чисел.</summary>
    static int CompareKeys(string a, string b)
    {
        var x = a.Split(' ').Select(int.Parse).ToArray();
        var y = b.Split(' ').Select(int.Parse).ToArray();
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
            if (x[i] != y[i]) return x[i].CompareTo(y[i]);
        return x.Length.CompareTo(y.Length);
    }

    /// <summary>Группа КЭ импорта lira: состав — номера КЭ сетки.</summary>
    static FemMemberGroup ImportedMeshGroup(int schemaId, string tag, string? memberType, IEnumerable<int> ids) =>
        FemGroupComposition.NewMeshGroup(schemaId,
            ids.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            tag, memberType, FemMemberGroup.ImportOrigin("lira"));
}
