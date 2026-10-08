using CScore.Fem;
using CScore.Planar;
using OpenCS.Gmsh.Parsing;

namespace OpenCS.Gmsh.Mapping;

/// <summary>Результат отображения внутренних Gmsh entities на constraint-объекты.</summary>
public sealed class PlanarConstraintMeshMappingResult
{
    public IReadOnlyList<PlanarConstraintMeshMapping> Mappings { get; init; } = [];
    public IReadOnlyList<FemValidationDiagnostic> Diagnostics { get; init; } = [];
    public bool IsCalculable => !Diagnostics.Any(diagnostic => diagnostic.IsError);
}

/// <summary>Сопоставляет physical/entity provenance MSH 4.1 с dense snapshot indices.</summary>
public static class PlanarConstraintMeshMapper
{
    public static PlanarConstraintMeshMappingResult Map(
        PlanarRegion region,
        GmshMsh41Document document,
        IReadOnlyList<PlanarMeshNode> nodes,
        IReadOnlyList<PlanarMeshElement> elements,
        IReadOnlyList<PlanarConstraintObject>? constraintObjects = null)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(elements);

        var diagnostics = document.Diagnostics.ToList();
        var rawToDense = document.Nodes.OrderBy(node => node.RawId)
            .Select((node, index) => (node.RawId, Index: index))
            .ToDictionary(pair => pair.RawId, pair => pair.Index);
        var shellElements = document.Elements.Where(IsShell).Select((element, index) => (element, index)).ToArray();
        var mappings = new List<PlanarConstraintMeshMapping>();

        foreach (var constraint in (constraintObjects ?? region.ConstraintObjects).OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var localDiagnostics = new List<FemValidationDiagnostic>();
            var mapped = constraint.Geometry.Kind switch
            {
                PlanarConstraintGeometryKind.Point => MapPoint(constraint, document, nodes, rawToDense, localDiagnostics),
                PlanarConstraintGeometryKind.Curve => MapCurve(constraint, document, nodes, rawToDense, localDiagnostics),
                PlanarConstraintGeometryKind.Region => MapRegion(constraint, document, nodes, shellElements, elements, localDiagnostics),
                _ => new PartialMapping()
            };
            var mapping = new PlanarConstraintMeshMapping
            {
                ConstraintObjectId = constraint.Id,
                PointNodeIndices = mapped.PointNodeIndices,
                OrderedCurveEdges = mapped.OrderedCurveEdges,
                CurveElementIndices = mapped.CurveElementIndices,
                RegionNodeIndices = mapped.RegionNodeIndices,
                RegionElementIndices = mapped.RegionElementIndices,
                EntityProvenance = mapped.EntityProvenance,
                SourceReferences = constraint.SourceReferences ?? [],
                StructuralRelations = constraint.StructuralRelations ?? [],
                Diagnostics = localDiagnostics
            };
            mappings.Add(mapping);
            diagnostics.AddRange(localDiagnostics);
        }

        return new PlanarConstraintMeshMappingResult { Mappings = mappings, Diagnostics = diagnostics };
    }

    static PartialMapping MapPoint(
        PlanarConstraintObject constraint,
        GmshMsh41Document document,
        IReadOnlyList<PlanarMeshNode> nodes,
        IReadOnlyDictionary<long, int> rawToDense,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        var prefix = $"constraint:{SafeName(constraint.Id)}:point";
        var pointElements = document.Elements.Where(element => element.ElementType == 15 && element.PhysicalName == prefix).ToArray();
        var candidates = pointElements.SelectMany(element => element.RawNodeIds)
            .Where(rawToDense.ContainsKey)
            .Select(raw => rawToDense[raw])
            .Distinct()
            .ToArray();
        // Точка, совпавшая с точкой другого constraint-а, попадает в его физическую группу (Gmsh пишет
        // сущность один раз) — тогда узел ищется по координатам.
        if (candidates.Length == 0)
            candidates = Enumerable.Range(0, nodes.Count)
                .Where(index => MatchesPoint(nodes[index], constraint.Geometry.Points[0], Math.Max(constraint.ToleranceM, 1e-9)))
                .ToArray();
        if (candidates.Length == 0)
        {
            diagnostics.Add(new("planar_constraint_point_missing", $"Для constraint '{constraint.Id}' не найден точный mesh node."));
            return new();
        }
        if (candidates.Length > 1)
        {
            diagnostics.Add(new("planar_constraint_point_ambiguous", $"Для constraint '{constraint.Id}' найдено несколько mesh nodes."));
            return new();
        }
        if (!MatchesPoint(nodes[candidates[0]], constraint.Geometry.Points[0], constraint.ToleranceM))
        {
            diagnostics.Add(new("planar_constraint_point_outside_tolerance", $"Mesh node constraint '{constraint.Id}' не совпадает с исходной точкой в заданном допуске."));
            return new();
        }
        return new() { PointNodeIndices = [candidates[0]], EntityProvenance = Provenance(pointElements, constraint.Id) };
    }

    static PartialMapping MapCurve(
        PlanarConstraintObject constraint,
        GmshMsh41Document document,
        IReadOnlyList<PlanarMeshNode> nodes,
        IReadOnlyDictionary<long, int> rawToDense,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        // Рёбра кривой — все линейные элементы сетки, лежащие на её ломаной: участок по кромке — это
        // линия контура, общий с другим constraint-ом — линия под его физической группой
        // (GmshPlanarGeoBuilder пишет линию в одну группу), поэтому отбор геометрический, не по имени.
        var tolerance = Math.Max(constraint.ToleranceM, 1e-9);
        var polyline = constraint.Geometry.Points;
        var edges = document.Elements
            .Select((element, index) => (element, index))
            .Where(pair => pair.element.ElementType == 1 && pair.element.RawNodeIds.Count == 2 &&
                rawToDense.ContainsKey(pair.element.RawNodeIds[0]) && rawToDense.ContainsKey(pair.element.RawNodeIds[1]))
            .Select(pair => new RawEdge(pair.element, rawToDense[pair.element.RawNodeIds[0]], rawToDense[pair.element.RawNodeIds[1]], pair.index))
            .Where(edge => OnPolyline(nodes[edge.A], nodes[edge.B], polyline, tolerance))
            .ToArray();
        var lineElements = edges.Select(edge => edge.Element).ToArray();
        var start = FindExactNode(nodes, constraint.Geometry.Points[0], constraint.ToleranceM, edges.SelectMany(edge => new[] { edge.A, edge.B }));
        var end = FindExactNode(nodes, constraint.Geometry.Points[^1], constraint.ToleranceM, edges.SelectMany(edge => new[] { edge.A, edge.B }));
        if (start is null || end is null)
        {
            diagnostics.Add(new("planar_constraint_curve_endpoint_missing", $"Для constraint '{constraint.Id}' не найдены точные endpoints curve."));
            return new();
        }

        var adjacency = new Dictionary<int, List<(int Node, RawEdge Edge)>>();
        foreach (var edge in edges)
        {
            AddAdjacency(adjacency, edge.A, edge.B, edge);
            AddAdjacency(adjacency, edge.B, edge.A, edge);
        }
        var orderedEdges = new List<PlanarMeshEdge>();
        var elementIndices = new List<int>();
        var current = start.Value;
        var previous = -1;
        var visited = new HashSet<RawEdge>();
        while (current != end.Value)
        {
            if (!adjacency.TryGetValue(current, out var candidates)) break;
            var next = candidates.Where(candidate => candidate.Node != previous && !visited.Contains(candidate.Edge)).ToArray();
            if (next.Length != 1) break;
            var selected = next[0];
            visited.Add(selected.Edge);
            orderedEdges.Add(new(current, selected.Node));
            elementIndices.Add(selected.Edge.ElementIndex);
            previous = current;
            current = selected.Node;
        }

        if (current != end.Value || visited.Count != edges.Length)
        {
            diagnostics.Add(new("planar_constraint_curve_chain_invalid", $"Curve constraint '{constraint.Id}' не образует однозначную полную цепочку."));
            return new();
        }
        return new()
        {
            OrderedCurveEdges = orderedEdges,
            CurveElementIndices = elementIndices,
            EntityProvenance = Provenance(lineElements, constraint.Id)
        };
    }

    static PartialMapping MapRegion(
        PlanarConstraintObject constraint,
        GmshMsh41Document document,
        IReadOnlyList<PlanarMeshNode> nodes,
        IReadOnlyList<(GmshMsh41Element element, int index)> shellElements,
        IReadOnlyList<PlanarMeshElement> snapshotElements,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        var prefix = $"constraint:{SafeName(constraint.Id)}:region";
        var selected = shellElements.Where(pair => pair.element.PhysicalName == prefix).ToArray();
        if (selected.Length == 0)
        {
            selected = shellElements.Where(pair => ElementCentroidInside(pair.index, snapshotElements, nodes, constraint.Geometry.Points)).ToArray();
        }
        if (selected.Length == 0)
        {
            diagnostics.Add(new("planar_constraint_region_empty", $"Для region constraint '{constraint.Id}' не найдены mesh elements."));
            return new();
        }
        var nodeIndices = selected.SelectMany(pair => snapshotElements[pair.index].NodeIndices).Distinct().OrderBy(index => index).ToArray();
        var regionEntities = document.Elements.Where(element => element.PhysicalName == prefix);
        return new()
        {
            RegionNodeIndices = nodeIndices,
            RegionElementIndices = selected.Select(pair => pair.index).ToArray(),
            EntityProvenance = Provenance(selected.Select(pair => pair.element).Concat(regionEntities), constraint.Id)
        };
    }

    static bool ElementCentroidInside(int elementIndex, IReadOnlyList<PlanarMeshElement> elements, IReadOnlyList<PlanarMeshNode> nodes, IReadOnlyList<PlanarPoint2D> polygon)
    {
        var element = elements[elementIndex];
        var u = element.NodeIndices.Average(index => nodes[index].U);
        var v = element.NodeIndices.Average(index => nodes[index].V);
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            if ((a.V > v) == (b.V > v)) continue;
            if (u < (b.U - a.U) * (v - a.V) / (b.V - a.V) + a.U) inside = !inside;
        }
        return inside;
    }

    static bool IsShell(GmshMsh41Element element) => element.ElementType is 2 or 3;

    static bool MatchesPoint(PlanarMeshNode node, PlanarPoint2D point, double tolerance) =>
        Math.Pow(node.U - point.U, 2) + Math.Pow(node.V - point.V, 2) <= tolerance * tolerance;

    static int? FindExactNode(IReadOnlyList<PlanarMeshNode> nodes, PlanarPoint2D point, double tolerance, IEnumerable<int> candidates)
    {
        var matches = candidates.Distinct().Where(index => MatchesPoint(nodes[index], point, tolerance)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    static void AddAdjacency(IDictionary<int, List<(int Node, RawEdge Edge)>> adjacency, int from, int to, RawEdge edge)
    {
        if (!adjacency.TryGetValue(from, out var values)) adjacency[from] = values = [];
        values.Add((to, edge));
    }

    static IReadOnlyList<PlanarMeshEntityProvenance> Provenance(IEnumerable<GmshMsh41Element> elements, string constraintId) =>
        elements.Select(element => new PlanarMeshEntityProvenance(constraintId, element.EntityDimension,
                checked((int)element.EntityTag), element.PhysicalGroup, element.PhysicalName))
            .Distinct()
            .ToArray();

    static string SafeName(string value) => value.Replace("\"", "_");

    sealed record RawEdge(GmshMsh41Element Element, int A, int B, int ElementIndex);

    /// <summary>Ребро лежит на одном звене ломаной: оба конца и середина — на звене в пределах допуска.</summary>
    static bool OnPolyline(PlanarMeshNode a, PlanarMeshNode b, IReadOnlyList<PlanarPoint2D> polyline, double tolerance)
    {
        double mu = (a.U + b.U) / 2, mv = (a.V + b.V) / 2;
        for (var i = 0; i + 1 < polyline.Count; i++)
            if (OnSegment(a.U, a.V, polyline[i], polyline[i + 1], tolerance) &&
                OnSegment(b.U, b.V, polyline[i], polyline[i + 1], tolerance) &&
                OnSegment(mu, mv, polyline[i], polyline[i + 1], tolerance))
                return true;
        return false;
    }

    static bool OnSegment(double u, double v, PlanarPoint2D p, PlanarPoint2D q, double tolerance)
    {
        double rx = q.U - p.U, ry = q.V - p.V;
        var lengthSquared = rx * rx + ry * ry;
        var t = lengthSquared == 0 ? 0 : ((u - p.U) * rx + (v - p.V) * ry) / lengthSquared;
        t = Math.Clamp(t, 0, 1);
        double dx = u - (p.U + t * rx), dy = v - (p.V + t * ry);
        return dx * dx + dy * dy <= tolerance * tolerance;
    }

    sealed class PartialMapping
    {
        public IReadOnlyList<int> PointNodeIndices { get; init; } = [];
        public IReadOnlyList<PlanarMeshEdge> OrderedCurveEdges { get; init; } = [];
        public IReadOnlyList<int> CurveElementIndices { get; init; } = [];
        public IReadOnlyList<int> RegionNodeIndices { get; init; } = [];
        public IReadOnlyList<int> RegionElementIndices { get; init; } = [];
        public IReadOnlyList<PlanarMeshEntityProvenance> EntityProvenance { get; init; } = [];
    }
}
