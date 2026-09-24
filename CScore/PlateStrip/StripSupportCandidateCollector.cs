using System.Globalization;
using System.Text.Json;
using CScore.Fem;
using CScore.Planar;

namespace CScore.PlateStrip;

/// <summary>Допуски сборщика опор.</summary>
/// <param name="PlaneToleranceM">Допуск принадлежности плоскости плиты и контуру, м.</param>
/// <param name="ColumnAngleToleranceDeg">Допустимый наклон оси колонны к нормали плиты, град.</param>
public sealed record StripSupportCollectorOptions(
    double PlaneToleranceM = 1e-3,
    double ColumnAngleToleranceDeg = 5.0);

/// <summary>Кандидаты в опоры и предупреждения о пропущенных повреждённых объектах.</summary>
public sealed record StripSupportCollectionResult(
    IReadOnlyList<StripSupportCandidate> Candidates,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>Сборщик кандидатов в опоры полосы из родительской FEM-схемы (спека Среза 8a, A.1):
/// узловые закрепления, колонны (стержни, примыкающие к плите по нормали) и стены (плоские
/// регионы, чьё пересечение с плоскостью плиты лежит в ней).
///
/// Чистая функция над пользовательской топологией: на её дефектах (повреждённый
/// <c>NodeIdsJson</c>, неразрешимый тег, нечисловые координаты, вырожденная ось или контур)
/// объект пропускается с предупреждением <c>plate_strip_support_source_invalid</c>, исключений
/// нет. Исключение — только на null-аргументах и невалидном Frame региона плиты.
///
/// Узлы стержней читаются из <c>NodeIdsJson</c> как теги (не Id) и сравниваются с
/// <see cref="FemNode.NodeTag"/> в каноническом виде.</summary>
public static class StripSupportCandidateCollector
{
    const string InvalidCode = "plate_strip_support_source_invalid";

    public static StripSupportCollectionResult Collect(
        PlanarRegion region,
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemMember> members,
        IReadOnlyList<PlanarRegion> planarRegions,
        StripSupportCollectorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(planarRegions);
        options ??= new StripSupportCollectorOptions();
        region.Frame.Validate();
        var hull = region.RequireHull();
        var holes = region.Holes.ToList();

        var context = new Context(region.Frame, hull, holes, options);
        var candidates = new List<StripSupportCandidate>();
        var diagnostics = new List<FemValidationDiagnostic>();

        CollectNodalRestraints(context, nodes, candidates, diagnostics);
        CollectColumns(context, nodes, members, candidates, diagnostics);
        CollectWalls(context, region, members, planarRegions, candidates, diagnostics);

        return new(candidates.OrderBy(c => c.Id, StringComparer.Ordinal).ToList(), diagnostics);
    }

    sealed record Context(Frame3D Frame, Contour Hull, List<Contour> Holes, StripSupportCollectorOptions Options)
    {
        public PlanarVector3 ToLocal(PlanarVector3 global) =>
            PlanarBoundaryFrameConverter.ToLocalPoint(Frame, global);

        public bool InPlaneAndInside(PlanarVector3 local) =>
            Math.Abs(local.Z) <= Options.PlaneToleranceM &&
            StripSupportGeometry.IsInsideRegion(new(local.X, local.Y), Hull, Holes, Options.PlaneToleranceM);
    }

    static void CollectNodalRestraints(
        Context context, IReadOnlyList<FemNode> nodes,
        List<StripSupportCandidate> candidates, List<FemValidationDiagnostic> diagnostics)
    {
        var normal = context.Frame.LocalZ;
        double[] normalComponents = [normal.X, normal.Y, normal.Z];

        foreach (var node in nodes)
        {
            if (node.DofMask == 0) continue;
            var global = new PlanarVector3(node.X, node.Y, node.Z);
            if (!global.IsFinite)
            {
                diagnostics.Add(Invalid($"Узел «{node.NodeTag}» (Id={node.Id}): нечисловые координаты."));
                continue;
            }

            var local = context.ToLocal(global);
            if (!context.InPlaneAndInside(local)) continue;

            bool restrainedAlongNormal = true;
            for (int i = 0; i < 3; i++)
                if (Math.Abs(normalComponents[i]) > 1e-9 && ((node.DofMask >> i) & 1) == 0)
                    restrainedAlongNormal = false;
            if (!restrainedAlongNormal) continue;

            var restrained = new bool[6];
            for (int k = 0; k < 6; k++) restrained[k] = ((node.DofMask >> k) & 1) == 1;

            var source = new PlanarBoundarySourceReference("fem_node", node.NodeTag, NodeId: node.Id);
            candidates.Add(new(
                StripSupportCandidate.BuildId(StripSupportKind.NodalRestraint, source, 0),
                StripSupportKind.NodalRestraint,
                new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Point, [new(local.X, local.Y)]),
                restrained,
                source));
        }
    }

    static void CollectColumns(
        Context context, IReadOnlyList<FemNode> nodes, IReadOnlyList<FemMember> members,
        List<StripSupportCandidate> candidates, List<FemValidationDiagnostic> diagnostics)
    {
        var byTag = new Dictionary<string, FemNode>(StringComparer.Ordinal);
        foreach (var node in nodes)
            if (FemMeshTopology.CanonicalNodeTag(node.NodeTag) is { } tag)
                byTag.TryAdd(tag, node);

        double cosTolerance = Math.Cos(context.Options.ColumnAngleToleranceDeg * Math.PI / 180.0);

        foreach (var member in members)
        {
            if (member.ElemType != "beam") continue;
            string label = $"Стержень «{member.ElemTag}» (Id={member.Id})";

            var tags = ReadNodeTags(member.NodeIdsJson);
            if (tags == null)
            {
                diagnostics.Add(Invalid($"{label}: список узлов повреждён."));
                continue;
            }
            if (tags.Count < 2)
            {
                diagnostics.Add(Invalid($"{label}: меньше двух узлов."));
                continue;
            }
            if (!byTag.TryGetValue(tags[0], out var first) || !byTag.TryGetValue(tags[^1], out var last))
            {
                diagnostics.Add(Invalid($"{label}: узел не найден в схеме."));
                continue;
            }

            var a = new PlanarVector3(first.X, first.Y, first.Z);
            var b = new PlanarVector3(last.X, last.Y, last.Z);
            if (!a.IsFinite || !b.IsFinite)
            {
                diagnostics.Add(Invalid($"{label}: нечисловые координаты узлов."));
                continue;
            }
            var axis = b - a;
            if (axis.Length <= context.Options.PlaneToleranceM)
            {
                diagnostics.Add(Invalid($"{label}: ось нулевой длины."));
                continue;
            }

            if (Math.Abs(axis.Normalize().Dot(context.Frame.LocalZ)) < cosTolerance) continue;

            var localA = context.ToLocal(a);
            var localB = context.ToLocal(b);
            PlanarVector3? foot = context.InPlaneAndInside(localA) ? localA
                : context.InPlaneAndInside(localB) ? localB
                : null;
            if (foot is not { } point) continue;

            var source = new PlanarBoundarySourceReference("fem_member", member.ElemTag, MemberId: member.Id);
            candidates.Add(new(
                StripSupportCandidate.BuildId(StripSupportKind.Column, source, 0),
                StripSupportKind.Column,
                new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Point, [new(point.X, point.Y)]),
                new bool[6],
                source));
        }
    }

    static void CollectWalls(
        Context context, PlanarRegion slab, IReadOnlyList<FemMember> members,
        IReadOnlyList<PlanarRegion> planarRegions,
        List<StripSupportCandidate> candidates, List<FemValidationDiagnostic> diagnostics)
    {
        var wallIds = members
            .Where(m => m.Kind == "wall" && m.PlanarRegionId.HasValue)
            .Select(m => m.PlanarRegionId!.Value)
            .ToHashSet();

        foreach (var wall in planarRegions)
        {
            if (ReferenceEquals(wall, slab) || wall.Id == slab.Id || !wallIds.Contains(wall.Id)) continue;
            string label = $"Стена «{wall.Tag}» (Id={wall.Id})";

            Contour? hull;
            try
            {
                wall.Frame.Validate();
                hull = wall.Hull;
            }
            catch (InvalidOperationException ex)
            {
                diagnostics.Add(Invalid($"{label}: {ex.Message}"));
                continue;
            }
            if (hull == null || hull.X.Count < 3 || hull.X.Count != hull.Y.Count)
            {
                diagnostics.Add(Invalid($"{label}: нет корректного внешнего контура."));
                continue;
            }

            var loop = StripSupportGeometry.OpenLoop(hull);
            var local = loop
                .Select(p => context.ToLocal(PlanarBoundaryFrameConverter.ToGlobalPoint(
                    wall.Frame, new PlanarVector3(p[0], p[1], 0.0))))
                .ToList();
            if (local.Any(p => !p.IsFinite))
            {
                diagnostics.Add(Invalid($"{label}: нечисловые координаты контура."));
                continue;
            }

            var segment = IntersectWithSlabPlane(local, context.Options.PlaneToleranceM);
            if (segment is not { } s) continue;

            var pieces = StripSupportGeometry.ClipSegmentToRegion(
                s.A, s.B, context.Hull, context.Holes, context.Options.PlaneToleranceM);
            var source = new PlanarBoundarySourceReference("planar_region", wall.Tag);
            var ordered = pieces
                .OrderBy(p => Math.Min(p.A.U, p.B.U))
                .ThenBy(p => Math.Min(p.A.V, p.B.V))
                .ToList();
            for (int ordinal = 0; ordinal < ordered.Count; ordinal++)
                candidates.Add(new(
                    StripSupportCandidate.BuildId(StripSupportKind.Wall, source, ordinal),
                    StripSupportKind.Wall,
                    new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve, [ordered[ordinal].A, ordered[ordinal].B]),
                    new bool[6],
                    source));
        }
    }

    /// <summary>Отрезок пересечения плоского контура стены с плоскостью плиты (w = 0) в координатах
    /// плиты. Вершина в плоскости — одна точка, примыкающие к ней рёбра новых точек не дают (без
    /// двойного учёта); ребро со строгой сменой знака w — точка интерполяции. Точки дедуплицируются
    /// и упорядочиваются вдоль линии пересечения; берётся отрезок между крайними. Для невыпуклой
    /// стены это оболочка нескольких отрезков — консервативно в сторону «опора есть».</summary>
    static (PlanarPoint2D A, PlanarPoint2D B)? IntersectWithSlabPlane(IReadOnlyList<PlanarVector3> loop, double tol)
    {
        var points = new List<PlanarPoint2D>();
        for (int i = 0; i < loop.Count; i++)
        {
            var p = loop[i];
            var q = loop[(i + 1) % loop.Count];
            if (Math.Abs(p.Z) <= tol)
                points.Add(new(p.X, p.Y));
            else if (Math.Abs(q.Z) > tol && p.Z * q.Z < 0.0)
            {
                double t = p.Z / (p.Z - q.Z);
                points.Add(new(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t));
            }
        }

        var unique = new List<PlanarPoint2D>();
        foreach (var point in points)
            if (unique.All(u => StripSupportGeometry.Distance(u, point) > tol))
                unique.Add(point);
        if (unique.Count < 2) return null;

        // Направление линии — от первой точки к самой удалённой; проекции на него упорядочивают точки.
        var origin = unique[0];
        var far = unique.MaxBy(u => StripSupportGeometry.Distance(origin, u))!;
        double du = far.U - origin.U, dv = far.V - origin.V;
        var sorted = unique.OrderBy(u => (u.U - origin.U) * du + (u.V - origin.V) * dv).ToList();
        return (sorted[0], sorted[^1]);
    }

    static IReadOnlyList<string>? ReadNodeTags(string json)
    {
        int[]? ids;
        try { ids = JsonSerializer.Deserialize<int[]>(json); }
        catch (JsonException) { return null; }
        return ids?.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
    }

    static FemValidationDiagnostic Invalid(string message) =>
        new(InvalidCode, message + " Объект пропущен при поиске опор полосы.", false);
}
