using System.Text.Json;
using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Planar;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Перекрытия схемы и её высотный диапазон (край полосы, когда соседнего этажа нет).</summary>
public sealed record StoreyFloorContext(IReadOnlyList<StoreyFloorLevel> Levels, double ZMin, double ZMax);

/// <summary>
/// Перекрытия схемы для диалога фрагмента этажа и отметка перекрытия по выделению в 3D (спека 2026-10-29). Только чтение.
/// </summary>
public static class StoreyFloorService
{
    /// <summary>
    /// Перекрытия из КонЭ-пластин и КЭ-оболочек сетки без КонЭ (импорт); диапазон z — по сетке, иначе по узлам схемы,
    /// иначе по перекрытиям. Читает всю сетку — вызывать в фоне.
    /// </summary>
    public static StoreyFloorContext Read(DatabaseService db, int schemaId)
    {
        ArgumentNullException.ThrowIfNull(db);
        var nodes = db.GetFemNodes(schemaId);
        var levels = StoreyFloorLevels.Find(db.GetFemMembers(schemaId), db.GetPlanarRegions(schemaId),
            db.GetFemMeshNodes(schemaId), db.GetFemMeshElements(schemaId));
        var zs = levels.Select(l => l.Z).ToList();
        (double Min, double Max) range = db.GetFemMeshZRange(schemaId)
            ?? (nodes.Count > 0 ? (nodes.Min(n => n.Z), nodes.Max(n => n.Z))
                : zs.Count > 0 ? (zs.Min(), zs.Max()) : (0, 0));
        return new(levels, range.Min, range.Max);
    }

    /// <summary>
    /// Отметка перекрытия по выделению: узлы схемы, вершины контуров КонЭ-пластин, узлы стержней-КонЭ, КЭ и узлы сетки.
    /// Разброс z больше допуска уровня или пустое выделение — null и ключ строки ошибки.
    /// </summary>
    public static double? SelectionZ(DatabaseService db, int schemaId, IReadOnlyCollection<string> nodeTags,
        IReadOnlyCollection<string> memberTags, IReadOnlyCollection<string> meshElemTags,
        IReadOnlyCollection<string> meshNodeTags, out string? errorKey)
    {
        ArgumentNullException.ThrowIfNull(db);
        var points = new List<PlanarVector3>();
        if (nodeTags.Count > 0 || memberTags.Count > 0)
        {
            var nodes = db.GetFemNodes(schemaId).GroupBy(n => n.NodeTag).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            foreach (var tag in nodeTags)
                if (nodes.TryGetValue(tag, out var n)) points.Add(new(n.X, n.Y, n.Z));
            if (memberTags.Count > 0)
            {
                var wanted = memberTags.ToHashSet(StringComparer.Ordinal);
                var regions = db.GetPlanarRegions(schemaId).Where(r => r.Id > 0).ToDictionary(r => r.Id);
                foreach (var m in db.GetFemMembers(schemaId).Where(m => wanted.Contains(m.ElemTag)))
                {
                    if (m.PlanarRegionId is int id && regions.TryGetValue(id, out var region) && region.Hull is { } hull)
                    {
                        for (int i = 0; i < hull.X.Count; i++)
                            points.Add(PlanarBoundaryFrameConverter.ToGlobalPoint(region.Frame, new PlanarVector3(hull.X[i], hull.Y[i], 0)));
                    }
                    else
                    {
                        foreach (var tag in NodeTags(m))
                            if (nodes.TryGetValue(tag, out var n)) points.Add(new(n.X, n.Y, n.Z));
                    }
                }
            }
        }
        points.AddRange(db.GetFemPickPoints(schemaId, meshElemTags, meshNodeTags));
        return StoreyFloorLevels.PickZ(points, out errorKey);
    }

    static IEnumerable<string> NodeTags(FemMember m)
    {
        if (string.IsNullOrWhiteSpace(m.NodeIdsJson)) return [];
        try { return (JsonSerializer.Deserialize<long[]>(m.NodeIdsJson) ?? []).Select(t => t.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        catch (JsonException) { return []; }
    }
}
