using System.Globalization;
using System.Text.Json;
using CScore.Planar;

namespace CScore.Fem;

/// <summary>Ограничения сетки области: ручные объекты области, выведенные из схемы и свободные узлы.</summary>
public sealed record FemPlanarRegionConstraints(
    IReadOnlyList<PlanarConstraintObject> Constraints,
    string SourceFingerprint,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics,
    DerivedPlanarConstraintSet Derived,
    int FreeNodeCount);

/// <summary>
/// Общий для команды «Построить сетку схемы» и диалога области порядок построения сеток областей и вход вывода
/// их ограничений (CSfea 4г). Вход не зависит от дробления стержней по длине и от Id строк сетки в БД — отпечаток
/// снимка стабилен между сборками. Область k видит узлы схемы, стержни, разбитые только узлами схемы, и пластины
/// областей 1…k−1 (поздняя область встраивает узлы ранних — стыки совпадают).
/// </summary>
public static class FemPlanarMeshPlan
{
    /// <summary>КонЭ-пластины своей схемы с областью и незаблокированной сеткой — мелкие (по размеру КЭ) раньше,
    /// затем по Id и тегу.</summary>
    public static List<(FemMember Member, PlanarRegion Region)> OrderedRegions(
        IReadOnlyList<FemMember> members, IReadOnlyList<PlanarRegion> regions)
    {
        var byId = new Dictionary<int, PlanarRegion>();
        foreach (var region in regions) byId.TryAdd(region.Id, region);
        return members
            .Where(m => m.PlanarRegionId is int id && byId.ContainsKey(id) && !m.IsMeshLocked)
            .Select(m => (Member: m, Region: byId[m.PlanarRegionId!.Value]))
            .OrderBy(p => p.Region.MeshMaxElementSizeM)
            .ThenBy(p => p.Member.Id)
            .ThenBy(p => p.Member.ElemTag, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Сетка стержней для вывода ограничений: КонЭ-стержни, разбитые только узлами схемы (без дробления
    /// по целевой длине — она задаётся в редакторе и не хранится).</summary>
    public static (List<FemMeshNode> Nodes, List<FemElement> Elements) DerivationBeams(
        int schemaId, IReadOnlyList<FemNode> nodes, IReadOnlyList<FemMember> members) =>
        FemMeshDiscretizer.Discretize(schemaId, nodes,
            members.Where(m => m.ElemType == "beam" && !m.IsMeshLocked)
                .Select(m => new FemMember { Id = m.Id, ElemTag = m.ElemTag, ElemType = m.ElemType, NodeIdsJson = m.NodeIdsJson })
                .ToList(),
            null);

    /// <summary>
    /// Ограничения сетки области: вывод из топологии (узлы схемы, сетка <paramref name="prefixMesh"/> — стержни и
    /// пластины ранее построенных областей) + ручные объекты области + точки свободных узлов схемы (не принадлежащих
    /// ни одному КонЭ), лежащих в области, — опоры и сосредоточенные нагрузки на плите.
    /// </summary>
    public static FemPlanarRegionConstraints Constraints(
        int schemaId,
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemMember> members,
        FemPlanarMaterializationResult prefixMesh,
        PlanarRegion region,
        PlanarConstraintDerivationOptions? options = null)
    {
        options ??= new PlanarConstraintDerivationOptions();
        var diagnostics = new List<FemValidationDiagnostic>();

        // Id узлов, КонЭ и КЭ — синтетические, по порядку тегов: выведенные ограничения именуются по Id, а отпечаток не
        // должен зависеть от Id строк в БД (меняются при пересохранении схемы) и от того, сохранена ли сессия редактора.
        int synthetic = 0;
        var topologyNodes = new Dictionary<string, FemNode>(StringComparer.Ordinal);
        foreach (var node in nodes.OrderBy(n => n.NodeTag, StringComparer.Ordinal))
            topologyNodes.TryAdd(node.NodeTag, new FemNode
            {
                Id = ++synthetic, SchemaId = schemaId, NodeTag = node.NodeTag, X = node.X, Y = node.Y, Z = node.Z,
            });
        foreach (var meshNode in prefixMesh.Nodes)
        {
            if (topologyNodes.TryGetValue(meshNode.NodeTag, out var source))
            {
                if (Math.Abs(source.X - meshNode.X) > 1e-9 || Math.Abs(source.Y - meshNode.Y) > 1e-9 ||
                    Math.Abs(source.Z - meshNode.Z) > 1e-9)
                    diagnostics.Add(new("planar_constraint_node_tag_coordinate_conflict",
                        $"Тег узла сетки '{meshNode.NodeTag}' совпадает с тегом узла схемы с другими координатами."));
                continue;
            }
            topologyNodes.Add(meshNode.NodeTag, new FemNode
            {
                Id = ++synthetic, SchemaId = schemaId, NodeTag = meshNode.NodeTag, X = meshNode.X, Y = meshNode.Y, Z = meshNode.Z,
            });
        }
        int elementId = 0;
        var elements = prefixMesh.Elements.Select(e => new FemElement
        {
            Id = ++elementId, SchemaId = schemaId, ElemTag = e.ElemTag, ElemType = e.ElemType,
            NodeIdsJson = e.NodeIdsJson, SourceMemberTag = e.SourceMemberTag, Origin = e.Origin,
        }).ToList();

        int memberId = 0;
        var topologyMembers = members.OrderBy(m => m.ElemTag, StringComparer.Ordinal).Select(m => new FemMember
        {
            Id = ++memberId, SchemaId = schemaId, ElemTag = m.ElemTag, ElemType = m.ElemType, NodeIdsJson = m.NodeIdsJson,
            PlanarRegionId = m.PlanarRegionId, MeshSource = m.MeshSource,
        }).ToList();
        var topology = new FemSchemaTopology(schemaId, topologyNodes.Values.ToList(), topologyMembers, elements);
        var derived = PlanarConstraintDeriver.Derive(topology, region, options);
        diagnostics.AddRange(derived.Diagnostics);

        var constraints = region.ConstraintObjects.ToList();
        var ids = constraints.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var constraint in derived.Constraints.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            if (!ids.Add(constraint.Id))
            {
                diagnostics.Add(new("planar_constraint_derived_id_duplicate",
                    $"Выведенное ограничение '{constraint.Id}' совпадает с ручным объектом области."));
                continue;
            }
            constraints.Add(constraint);
        }

        int free = 0;
        var used = new HashSet<int>();
        foreach (var member in members)
            foreach (var id in ReadIds(member.NodeIdsJson)) used.Add(id);
        foreach (var node in nodes.OrderBy(n => n.NodeTag, StringComparer.Ordinal))
        {
            if (!int.TryParse(node.NodeTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tag) || used.Contains(tag))
                continue;
            var delta = new PlanarVector3(node.X, node.Y, node.Z) - region.Frame.Origin;
            double u = delta.Dot(region.Frame.LocalX), v = delta.Dot(region.Frame.LocalY), w = delta.Dot(region.Frame.LocalZ);
            if (Math.Abs(w) > options.PlaneToleranceM || !InsideRegion(u, v, region, options.GeometryToleranceM)) continue;
            var id = $"free-node:{node.NodeTag}";
            if (!ids.Add(id)) continue;
            var point = PlanarConstraintObject.Point(id, new PlanarPoint2D(u, v),
                new PlanarStructuralFacet(PlanarStructuralKind.None), new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint), id);
            point.IsDerived = true;
            point.ToleranceM = options.GeometryToleranceM;
            point.SourceReferences = [new PlanarSourceReference(0, "", [], [], [topologyNodes[node.NodeTag].Id], [node.NodeTag])];
            constraints.Add(point);
            free++;
        }

        return new FemPlanarRegionConstraints(constraints, derived.SourceFingerprint, diagnostics, derived, free);
    }

    static int[] ReadIds(string json)
    {
        try { return JsonSerializer.Deserialize<int[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>Точка в области: внутри или на внешнем контуре и не строго внутри отверстия.</summary>
    static bool InsideRegion(double u, double v, PlanarRegion region, double tolerance)
    {
        if (region.Hull is not { } hull || !InsideOrOn(u, v, hull, tolerance)) return false;
        return !region.Holes.Any(hole => InsideOrOn(u, v, hole, -1) && !OnBoundary(u, v, hole, tolerance));
    }

    static bool InsideOrOn(double u, double v, Contour contour, double tolerance)
    {
        if (tolerance >= 0 && OnBoundary(u, v, contour, tolerance)) return true;
        var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(contour.X, contour.Y);
        var inside = false;
        for (int i = 0, j = x.Length - 1; i < x.Length; j = i++)
            if ((y[i] > v) != (y[j] > v) && u < (x[j] - x[i]) * (v - y[i]) / (y[j] - y[i]) + x[i])
                inside = !inside;
        return inside;
    }

    static bool OnBoundary(double u, double v, Contour contour, double tolerance)
    {
        var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(contour.X, contour.Y);
        for (int i = 0; i < x.Length; i++)
        {
            int j = (i + 1) % x.Length;
            double rx = x[j] - x[i], ry = y[j] - y[i];
            var lengthSquared = rx * rx + ry * ry;
            var t = lengthSquared == 0 ? 0 : Math.Clamp(((u - x[i]) * rx + (v - y[i]) * ry) / lengthSquared, 0, 1);
            double dx = u - (x[i] + t * rx), dy = v - (y[i] + t * ry);
            if (dx * dx + dy * dy <= tolerance * tolerance) return true;
        }
        return false;
    }
}
