using System.Globalization;
using System.Text.Json;
using CScore.Planar;

namespace CScore.Fem.Fragment;

/// <summary>
/// Отбор схемного уровня родителя по полосе фрагмента: КонЭ, заходящие в [z₁ − tol, z₂ + tol] (включительно — плита на
/// границе остаётся, чтобы построитель выдал отказ), их узлы и узлы полосы, нагрузки только на них. Сетку и нагрузки КЭ
/// отбирает сервис фильтром в SQL.
/// </summary>
public static class StoreyFragmentBand
{
    /// <summary>КонЭ, заходящие в полосу: стержень — по z своих узлов, пластина — по z контура области в общих осях.</summary>
    public static List<FemMember> KeepMembers(
        IEnumerable<FemMember> members, IEnumerable<FemNode> nodes, IEnumerable<PlanarRegion> regions, StoreyFragmentRequest request)
    {
        var z = nodes.GroupBy(n => n.NodeTag).ToDictionary(g => g.Key, g => g.First().Z, StringComparer.Ordinal);
        var regionById = regions.Where(r => r.Id > 0).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        double lo = request.Z1 - request.ToleranceM, hi = request.Z2 + request.ToleranceM;
        var result = new List<FemMember>();
        foreach (var m in members)
        {
            var range = m.PlanarRegionId is int id && regionById.TryGetValue(id, out var region)
                ? RegionRange(region)
                : NodeRange(m, z);
            if (range is { } r && r.Max >= lo && r.Min <= hi) result.Add(m);
        }
        return result;
    }

    /// <summary>
    /// Снимок с КонЭ полосы (<see cref="KeepMembers"/>), узлами этих КонЭ и узлами в полосе, нагрузками КонЭ и узлов только
    /// для оставшихся; прочие наборы — как есть.
    /// </summary>
    public static StoreyFragmentParentSnapshot Trim(StoreyFragmentParentSnapshot s, StoreyFragmentRequest request)
    {
        var members = KeepMembers(s.Members, s.Nodes, s.Regions, request);
        var memberIds = members.Select(m => m.Id).ToHashSet();
        var regionIds = members.Where(m => m.PlanarRegionId is not null).Select(m => m.PlanarRegionId!.Value).ToHashSet();
        var memberNodeTags = members.SelectMany(NodeTags).ToHashSet(StringComparer.Ordinal);
        double lo = request.Z1 - request.ToleranceM, hi = request.Z2 + request.ToleranceM;
        var nodes = s.Nodes.Where(n => memberNodeTags.Contains(n.NodeTag) || (n.Z >= lo && n.Z <= hi)).ToList();
        var nodeIds = nodes.Select(n => n.Id).ToHashSet();
        return new StoreyFragmentParentSnapshot
        {
            Nodes = nodes,
            Members = members,
            Regions = s.Regions.Where(r => regionIds.Contains(r.Id)).ToList(),
            MeshNodes = s.MeshNodes,
            MeshElements = s.MeshElements,
            RigidBodies = s.RigidBodies,
            LoadCases = s.LoadCases,
            LoadDefinitions = s.LoadDefinitions,
            MemberLoads = s.MemberLoads.Where(l => memberIds.Contains(l.MemberId)).ToList(),
            ElementLoads = s.ElementLoads,
            MeshNodeLoads = s.MeshNodeLoads,
            NodeLoads = s.NodeLoads.Where(l => nodeIds.Contains(l.NodeId)).ToList(),
            KinematicLoads = s.KinematicLoads.Where(l => nodeIds.Contains(l.NodeId)).ToList(),
            Groups = s.Groups,
            Springs = s.Springs,
            MeshNodeSupports = s.MeshNodeSupports,
        };
    }

    static (double Min, double Max)? NodeRange(FemMember m, Dictionary<string, double> z)
    {
        var zs = NodeTags(m).Where(z.ContainsKey).Select(t => z[t]).ToList();
        return zs.Count == 0 ? null : (zs.Min(), zs.Max());
    }

    static (double Min, double Max)? RegionRange(PlanarRegion region)
    {
        var hull = region.RequireHull();
        if (hull.X.Count == 0) return null;
        var zs = Enumerable.Range(0, hull.X.Count)
            .Select(i => PlanarBoundaryFrameConverter.ToGlobalPoint(region.Frame, new PlanarVector3(hull.X[i], hull.Y[i], 0)).Z)
            .ToList();
        return (zs.Min(), zs.Max());
    }

    static IEnumerable<string> NodeTags(FemMember m)
    {
        if (string.IsNullOrWhiteSpace(m.NodeIdsJson)) return [];
        try
        {
            return (JsonSerializer.Deserialize<long[]>(m.NodeIdsJson) ?? []).Select(t => t.ToString(CultureInfo.InvariantCulture));
        }
        catch (JsonException) { return []; }
    }
}
