using CScore.Fire.Entities;
using CSfea.Thermal;
using CSfea.Thermal.Bc;

namespace CScore.Fire;

/// <summary>
/// Сопоставление граничных рёбер огневой сетки с параметрами Робина.
/// </summary>
public static class FireBoundaryMapper
{
    /// <summary>
    /// Преобразовать описания рёбер секции пожара в список граничных условий для решателя.
    /// </summary>
    /// <param name="fireSection">Параметры огневого сечения.</param>
    /// <param name="meshResult">Результат построения сетки с метаданными граничных рёбер.</param>
    /// <param name="fireCurveName">Имя огневой кривой для рёбер типа <c>fire</c>.</param>
    public static List<HeatBoundaryEdge> MapEdges(
        FireSectionDef fireSection,
        FireMeshBuildResult meshResult,
        string fireCurveName)
    {
        ArgumentNullException.ThrowIfNull(fireSection);
        ArgumentNullException.ThrowIfNull(meshResult);

        var fireCurve = FireCurves.Get(fireCurveName);
        var profile = BuildOuterPresetProfile(meshResult.BoundaryEdges, meshResult.Mesh, fireSection.BcPreset);

        var mapped = new List<HeatBoundaryEdge>(meshResult.BoundaryEdges.Count);
        foreach (var edge in meshResult.BoundaryEdges)
        {
            var explicitDef = fireSection.Edges.FirstOrDefault(e =>
                string.Equals(Norm(e.ContourType), edge.ContourType, StringComparison.Ordinal) &&
                e.HoleIndex == edge.HoleIndex &&
                e.EdgeIndex == edge.OriginalEdgeIndex);

            var resolved = ResolveBoundaryDefinition(fireSection, edge, explicitDef, profile);
            if (resolved.BcType == HeatBoundaryBcType.Adiabatic)
                continue;

            mapped.Add(new HeatBoundaryEdge(
                NodeA: edge.NodeA,
                NodeB: edge.NodeB,
                LengthM: edge.LengthM,
                BcType: resolved.BcType,
                AlphaConv: resolved.AlphaConv,
                Emissivity: resolved.Emissivity,
                TAmbientCelsius: resolved.TAmbientCelsius,
                FireCurveAtTime: resolved.BcType == HeatBoundaryBcType.Fire ? fireCurve : null,
                NodeMid: ResolveMidNode(meshResult, edge.NodeA, edge.NodeB)));
        }

        return mapped;
    }

    static int? ResolveMidNode(FireMeshBuildResult meshResult, int a, int b)
    {
        if (meshResult.LinearMesh == null)
            return null;
        return HeatMeshQuadratic.TryGetMidNode(meshResult.LinearMesh, meshResult.Mesh, a, b);
    }

    private static ResolvedBoundary ResolveBoundaryDefinition(
        FireSectionDef fireSection,
        FireBoundaryEdgeInfo edge,
        FireBoundaryEdgeDef? explicitDef,
        HashSet<(string ContourType, int? HoleIndex, int EdgeIndex)> profileFireEdges)
    {
        if (explicitDef != null)
            return FromExplicitDef(explicitDef);

        if (edge.ContourType == "hole")
        {
            string holePreset = Norm(fireSection.HoleBcPreset);
            if (holePreset == "ambient")
                return AmbientDefault();
            return AdiabaticDefault();
        }

        string preset = Norm(fireSection.BcPreset);
        if (preset == "all-sided")
            return FireDefault();
        if (preset == "manual")
            return AdiabaticDefault();
        if (profileFireEdges.Contains((edge.ContourType, edge.HoleIndex, edge.OriginalEdgeIndex)))
            return FireDefault();

        return AmbientDefault();
    }

    private static ResolvedBoundary FromExplicitDef(FireBoundaryEdgeDef def)
    {
        string type = Norm(def.BcType);
        return type switch
        {
            "fire" => new ResolvedBoundary(
                HeatBoundaryBcType.Fire,
                def.AlphaConv > 0.0 ? def.AlphaConv : 25.0,
                def.Emissivity > 0.0 ? def.Emissivity : 0.7,
                def.TAmbientCelsius),
            "ambient" => new ResolvedBoundary(
                HeatBoundaryBcType.Ambient,
                def.AlphaConv > 0.0 ? def.AlphaConv : 9.0,
                def.Emissivity > 0.0 ? def.Emissivity : 0.8,
                def.TAmbientCelsius),
            "adiabatic" => AdiabaticDefault(),
            _ => throw new ArgumentException($"Неизвестный тип граничного условия: '{def.BcType}'.")
        };
    }

    private static ResolvedBoundary FireDefault()
        => new(HeatBoundaryBcType.Fire, 25.0, 0.7, 20.0);

    private static ResolvedBoundary AmbientDefault()
        => new(HeatBoundaryBcType.Ambient, 9.0, 0.8, 20.0);

    private static ResolvedBoundary AdiabaticDefault()
        => new(HeatBoundaryBcType.Adiabatic, 0.0, 0.0, 20.0);

    private static string Norm(string? value)
        => (value ?? "").Trim().ToLowerInvariant();

    private static HashSet<(string ContourType, int? HoleIndex, int EdgeIndex)> BuildOuterPresetProfile(
        IReadOnlyList<FireBoundaryEdgeInfo> edges,
        CSfea.Thermal.HeatMesh mesh,
        string? bcPreset)
    {
        string preset = Norm(bcPreset);
        var outerEdges = edges.Where(e => e.ContourType == "outer").ToList();
        var result = new HashSet<(string, int?, int)>();
        if (outerEdges.Count == 0 || preset is "manual" or "all-sided")
            return result;

        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        foreach (var e in outerEdges)
        {
            var (mx, my) = MidPoint(mesh, e);
            minX = Math.Min(minX, mx);
            maxX = Math.Max(maxX, mx);
            minY = Math.Min(minY, my);
            maxY = Math.Max(maxY, my);
        }

        double tolX = Math.Max((maxX - minX) * 0.05, 1e-9);
        double tolY = Math.Max((maxY - minY) * 0.05, 1e-9);
        foreach (var e in outerEdges)
        {
            var (mx, my) = MidPoint(mesh, e);
            bool isBottom = Math.Abs(my - minY) <= tolY;
            bool isTop = Math.Abs(my - maxY) <= tolY;
            bool isLeft = Math.Abs(mx - minX) <= tolX;
            bool isRight = Math.Abs(mx - maxX) <= tolX;

            bool isFire = preset switch
            {
                "1-sided" => isBottom,
                "2-sided" => isLeft || isRight,
                "3-sided" => !isTop,
                _ => false
            };
            if (isFire)
                result.Add((e.ContourType, e.HoleIndex, e.OriginalEdgeIndex));
        }

        return result;
    }

    private static (double X, double Y) MidPoint(CSfea.Thermal.HeatMesh mesh, FireBoundaryEdgeInfo edge)
    {
        return (
            0.5 * (mesh.X[edge.NodeA] + mesh.X[edge.NodeB]),
            0.5 * (mesh.Y[edge.NodeA] + mesh.Y[edge.NodeB]));
    }

    private sealed record ResolvedBoundary(
        HeatBoundaryBcType BcType,
        double AlphaConv,
        double Emissivity,
        double TAmbientCelsius);
}
