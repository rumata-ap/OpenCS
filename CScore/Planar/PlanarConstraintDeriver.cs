using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CScore.Fem;

namespace CScore.Planar;

/// <summary>Автоматически выводит point/curve constraints из фактической FEM topology.</summary>
public static class PlanarConstraintDeriver
{
    public static DerivedPlanarConstraintSet Derive(
        FemSchemaTopology topology,
        PlanarRegion region,
        PlanarConstraintDerivationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(region);
        options ??= new PlanarConstraintDerivationOptions();

        var diagnostics = new List<FemValidationDiagnostic>();
        try
        {
            options.Validate();
            region.Frame.Validate();
        }
        catch (InvalidOperationException ex)
        {
            diagnostics.Add(new("planar_constraint_frame_or_options_invalid", ex.Message));
        }

        var sourceFingerprint = ComputeSourceFingerprint(topology, region, options);
        var nodesByTag = BuildNodeLookup(topology.Nodes, diagnostics);
        var membersByTag = BuildMemberLookup(topology.Members, diagnostics);
        var hostMembers = topology.Members
            .Where(member => string.Equals(member.ElemType, "shell", StringComparison.OrdinalIgnoreCase) &&
                             member.PlanarRegionId == region.Id)
            .OrderBy(member => member.Id)
            .ThenBy(member => member.ElemTag, StringComparer.Ordinal)
            .ToArray();

        if (hostMembers.Length == 0)
            diagnostics.Add(new("planar_constraint_host_missing",
                $"Для PlanarRegion {region.Id} не найден однозначный host shell member."));
        else if (hostMembers.Length > 1)
            diagnostics.Add(new("planar_constraint_host_ambiguous",
                $"Для PlanarRegion {region.Id} найдено несколько host shell members."));

        var hostTags = hostMembers.Select(member => member.ElemTag).ToHashSet(StringComparer.Ordinal);
        var elementsByMemberTag = new Dictionary<string, List<FemElement>>(StringComparer.Ordinal);
        foreach (var element in topology.Elements.OrderBy(element => element.Id).ThenBy(element => element.ElemTag, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(element.SourceMemberTag))
                continue;
            if (!membersByTag.ContainsKey(element.SourceMemberTag))
            {
                diagnostics.Add(new("planar_constraint_source_member_missing",
                    $"Конечный элемент {element.ElemTag} ссылается на неизвестный source member '{element.SourceMemberTag}'."));
                continue;
            }

            if (!elementsByMemberTag.TryGetValue(element.SourceMemberTag, out var elements))
                elementsByMemberTag[element.SourceMemberTag] = elements = [];
            elements.Add(element);
        }

        var candidates = new List<Candidate>();
        var referencedNodeIds = new HashSet<int>();
        var foreignMembers = topology.Members
            .Where(member => !hostTags.Contains(member.ElemTag))
            .OrderBy(member => member.Id)
            .ThenBy(member => member.ElemTag, StringComparer.Ordinal)
            .ToArray();

        foreach (var member in foreignMembers)
        {
            var memberNodeIds = ReadNodeIds(member.NodeIdsJson, $"member {member.ElemTag}", diagnostics);
            var memberElements = elementsByMemberTag.TryGetValue(member.ElemTag, out var grouped)
                ? grouped
                : [];
            var allElementNodeIds = memberElements
                .SelectMany(element => ReadNodeIds(element.NodeIdsJson, $"element {element.ElemTag}", diagnostics))
                .Distinct()
                .ToArray();
            var allNodeIds = memberNodeIds.Concat(allElementNodeIds).Distinct().ToArray();
            var memberNodes = ResolveNodes(allNodeIds, nodesByTag, member.ElemTag, diagnostics);
            foreach (var node in memberNodes)
            {
                referencedNodeIds.Add(node.Id);
                var local = ToLocal(region.Frame, node);
                if (!IsValidRegionPoint(local, region, options))
                    continue;

                AddCandidate(candidates, new Candidate(
                    PlanarConstraintGeometryKind.Point,
                    [new PlanarPoint2D(local.U, local.V)],
                    new SourceData(member, memberElements, [node], options.CommonNodeDofMask),
                    isNodePoint: true,
                    nodeId: node.Id));
            }

            if (memberElements.Count > 0)
            {
                foreach (var element in memberElements.OrderBy(element => element.Id))
                {
                    var ids = ReadNodeIds(element.NodeIdsJson, $"element {element.ElemTag}", diagnostics);
                    var elementNodes = ResolveNodes(ids, nodesByTag, element.ElemTag, diagnostics);
                    if (elementNodes.Count != ids.Length || ids.Length < 2)
                        continue;

                    if (string.Equals(element.ElemType, "shell", StringComparison.OrdinalIgnoreCase))
                        AddShellCandidates(candidates, member, element, elementNodes, region, options, diagnostics);
                    else
                        AddSegmentCandidates(candidates, member, [element], elementNodes[0], elementNodes[1], region, options,
                            options.TransverseBarDofMask, diagnostics);
                }
            }
            else if (memberNodes.Count >= 2)
            {
                if (string.Equals(member.ElemType, "shell", StringComparison.OrdinalIgnoreCase))
                    AddShellCandidates(candidates, member, null, memberNodes, region, options, diagnostics);
                else
                    AddSegmentCandidates(candidates, member, [], memberNodes[0], memberNodes[1], region, options,
                        options.TransverseBarDofMask, diagnostics);
            }
        }

        var curveCandidates = MergeCurveCandidates(candidates.Where(candidate => candidate.Kind == PlanarConstraintGeometryKind.Curve).ToList(), options);
        var geometryDrafts = new Dictionary<string, GeometryDraft>(StringComparer.Ordinal);
        foreach (var candidate in candidates.Where(candidate => candidate.Kind == PlanarConstraintGeometryKind.Point).Concat(curveCandidates))
        {
            var key = GeometryKey(candidate.Kind, candidate.Points, options.GeometryToleranceM);
            if (!geometryDrafts.TryGetValue(key, out var draft))
            {
                geometryDrafts[key] = new GeometryDraft(candidate);
                continue;
            }

            if (draft.DofMask != candidate.DofMask)
            {
                diagnostics.Add(new("planar_constraint_dof_conflict",
                    $"Для geometry locus '{key}' обнаружены несовместимые DOF policies."));
                continue;
            }

            draft.Sources.AddRange(candidate.Sources);
            if (draft.NodeId is null) draft.NodeId = candidate.NodeId;
        }

        var intersectionOrdinals = new Dictionary<(int MemberId, string Tag), int>();
        var constraints = new List<PlanarConstraintObject>();
        foreach (var draft in geometryDrafts.Values
                     .OrderBy(draft => draft.Kind)
                     .ThenBy(draft => GeometryKey(draft.Kind, draft.Points, options.GeometryToleranceM), StringComparer.Ordinal))
        {
            var firstSource = draft.Sources
                .OrderBy(source => source.Member.Id)
                .ThenBy(source => source.Member.ElemTag, StringComparer.Ordinal)
                .First();
            string id;
            if (draft.Kind == PlanarConstraintGeometryKind.Point && draft.NodeId is int nodeId)
            {
                id = $"derived:fem-member:{firstSource.Member.Id}:node:{nodeId}";
            }
            else if (draft.Kind == PlanarConstraintGeometryKind.Point)
            {
                var sourceKey = (firstSource.Member.Id, firstSource.Member.ElemTag);
                intersectionOrdinals[sourceKey] = intersectionOrdinals.TryGetValue(sourceKey, out var ordinal) ? ordinal + 1 : 0;
                id = $"derived:fem-member:{firstSource.Member.Id}:intersection:{intersectionOrdinals[sourceKey]}";
            }
            else
            {
                id = $"derived:fem-member:{firstSource.Member.Id}:curve:{GeometryKey(draft.Kind, draft.Points, options.GeometryToleranceM)}";
            }

            var relationGroups = draft.Sources
                .GroupBy(source => (source.Member.Id, source.Member.ElemTag, source.DofMask))
                .OrderBy(group => group.Key.Id)
                .ThenBy(group => group.Key.ElemTag, StringComparer.Ordinal)
                .ThenBy(group => group.Key.DofMask)
                .ToArray();
            var relations = relationGroups.Select(group =>
            {
                var sources = group.ToArray();
                var elements = sources.SelectMany(source => source.Elements)
                    .GroupBy(element => element.Id)
                    .Select(grouped => grouped.First())
                    .OrderBy(element => element.Id)
                    .ToArray();
                var master = new PlanarMasterReference(
                    "fem-member",
                    group.Key.Id != 0 ? group.Key.Id.ToString(CultureInfo.InvariantCulture) : group.Key.ElemTag);
                return new PlanarStructuralRelation(
                    group.Key.Id,
                    group.Key.ElemTag,
                    elements.Select(element => element.Id).ToArray(),
                    elements.Select(element => element.ElemTag).ToArray(),
                    master,
                    PlanarStructuralKind.EmbeddedMember,
                    group.Key.DofMask);
            }).ToList();

            var sourceReferences = draft.Sources
                .GroupBy(source => (source.Member.Id, source.Member.ElemTag))
                .OrderBy(group => group.Key.Id)
                .ThenBy(group => group.Key.ElemTag, StringComparer.Ordinal)
                .Select(group =>
                {
                    var sources = group.ToArray();
                    var elements = sources.SelectMany(source => source.Elements)
                        .GroupBy(element => element.Id)
                        .Select(grouped => grouped.First())
                        .OrderBy(element => element.Id)
                        .ToArray();
                    var nodes = sources.SelectMany(source => source.Nodes)
                        .GroupBy(node => node.Id)
                        .Select(grouped => grouped.First())
                        .OrderBy(node => node.Id)
                        .ToArray();
                    return new PlanarSourceReference(
                        group.Key.Id,
                        group.Key.ElemTag,
                        elements.Select(element => element.Id).ToArray(),
                        elements.Select(element => element.ElemTag).ToArray(),
                        nodes.Select(node => node.Id).ToArray(),
                        nodes.Select(node => node.NodeTag).ToArray());
                }).ToList();

            var firstRelation = relations[0];
            var structuralFacet = new PlanarStructuralFacet(
                PlanarStructuralKind.EmbeddedMember,
                firstRelation.MasterReference,
                firstRelation.DofMask);
            var meshFacet = draft.Kind == PlanarConstraintGeometryKind.Point
                ? new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint)
                : new PlanarMeshFacet(PlanarMeshKind.ConformingPartition);
            var constraint = draft.Kind switch
            {
                PlanarConstraintGeometryKind.Point => PlanarConstraintObject.Point(id, draft.Points[0], structuralFacet, meshFacet, id),
                _ => PlanarConstraintObject.Curve(id, draft.Points, structuralFacet, meshFacet, id)
            };
            constraint.IsDerived = true;
            constraint.ToleranceM = options.GeometryToleranceM;
            constraint.DofMask = firstRelation.DofMask;
            constraint.MasterReference = firstRelation.MasterReference;
            constraint.SourceReferences = sourceReferences;
            constraint.StructuralRelations = relations;
            constraint.Provenance = string.Join(";", sourceReferences.Select(source =>
                $"member:{source.MemberId}:{source.MemberTag}:elements:{string.Join(',', source.ElementIds)}:nodes:{string.Join(',', source.NodeIds)}"));
            constraints.Add(constraint);
        }

        return new DerivedPlanarConstraintSet
        {
            Constraints = constraints,
            SourceFingerprint = sourceFingerprint,
            Diagnostics = diagnostics,
            SourceNodeCount = referencedNodeIds.Count,
            PointLocusCount = constraints.Count(constraint => constraint.Geometry.Kind == PlanarConstraintGeometryKind.Point),
            CurveLocusCount = constraints.Count(constraint => constraint.Geometry.Kind == PlanarConstraintGeometryKind.Curve),
            SourceMemberCount = foreignMembers.Length
        };
    }

    static Dictionary<string, FemNode> BuildNodeLookup(IReadOnlyList<FemNode> nodes, ICollection<FemValidationDiagnostic> diagnostics)
    {
        var result = new Dictionary<string, FemNode>(StringComparer.Ordinal);
        foreach (var node in nodes.OrderBy(node => node.Id).ThenBy(node => node.NodeTag, StringComparer.Ordinal))
        {
            if (!node.X.IsFinite() || !node.Y.IsFinite() || !node.Z.IsFinite())
                diagnostics.Add(new("planar_constraint_node_coordinate_invalid", $"Узел {node.NodeTag} содержит нечисловые координаты."));
            if (!result.TryAdd(node.NodeTag, node))
                diagnostics.Add(new("planar_constraint_node_tag_duplicate", $"Тег узла '{node.NodeTag}' повторяется."));
        }
        return result;
    }

    static Dictionary<string, FemMember> BuildMemberLookup(IReadOnlyList<FemMember> members, ICollection<FemValidationDiagnostic> diagnostics)
    {
        var result = new Dictionary<string, FemMember>(StringComparer.Ordinal);
        foreach (var member in members.OrderBy(member => member.Id).ThenBy(member => member.ElemTag, StringComparer.Ordinal))
        {
            if (!result.TryAdd(member.ElemTag, member))
                diagnostics.Add(new("planar_constraint_member_tag_duplicate", $"Тег member '{member.ElemTag}' повторяется."));
        }
        return result;
    }

    static IReadOnlyList<FemNode> ResolveNodes(
        IEnumerable<int> ids,
        IReadOnlyDictionary<string, FemNode> nodesByTag,
        string source,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        var result = new List<FemNode>();
        foreach (var id in ids)
        {
            var tag = id.ToString(CultureInfo.InvariantCulture);
            if (nodesByTag.TryGetValue(tag, out var node)) result.Add(node);
            else diagnostics.Add(new("planar_constraint_source_node_missing", $"Источник '{source}' ссылается на отсутствующий узел {id}."));
        }
        return result;
    }

    static int[] ReadNodeIds(string json, string source, ICollection<FemValidationDiagnostic> diagnostics)
    {
        try
        {
            var ids = JsonSerializer.Deserialize<int[]>(json);
            if (ids is null)
            {
                diagnostics.Add(new("planar_constraint_connectivity_invalid", $"У источника '{source}' connectivity JSON равен null."));
                return [];
            }
            return ids;
        }
        catch (JsonException ex)
        {
            diagnostics.Add(new("planar_constraint_connectivity_invalid", $"У источника '{source}' повреждён connectivity JSON: {ex.Message}"));
            return [];
        }
    }

    static void AddShellCandidates(
        ICollection<Candidate> candidates,
        FemMember member,
        FemElement? element,
        IReadOnlyList<FemNode> nodes,
        PlanarRegion region,
        PlanarConstraintDerivationOptions options,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        if (nodes.Count < 3) return;
        var local = nodes.Select(node => (Node: node, Point: ToLocal(region.Frame, node))).ToArray();
        var sourceElements = element is null ? Array.Empty<FemElement>() : [element];
        if (local.All(item => Math.Abs(item.Point.W) <= options.PlaneToleranceM))
        {
            for (var i = 0; i < local.Length; i++)
            {
                var a = local[i];
                var b = local[(i + 1) % local.Length];
                AddCoplanarCurve(candidates, member, sourceElements, a.Node, b.Node, a.Point, b.Point, region, options, options.WallLineDofMask, diagnostics);
            }
            return;
        }

        var intersections = new List<PlanarPoint2D>();
        foreach (var (a, b) in Edges(local))
        {
            if (Math.Abs(a.Point.W) <= options.PlaneToleranceM)
                intersections.Add(new(a.Point.U, a.Point.V));
            if (Math.Abs(b.Point.W) <= options.PlaneToleranceM)
                intersections.Add(new(b.Point.U, b.Point.V));
            if ((a.Point.W < -options.PlaneToleranceM && b.Point.W > options.PlaneToleranceM) ||
                (a.Point.W > options.PlaneToleranceM && b.Point.W < -options.PlaneToleranceM))
            {
                var t = a.Point.W / (a.Point.W - b.Point.W);
                intersections.Add(new(
                    a.Point.U + (b.Point.U - a.Point.U) * t,
                    a.Point.V + (b.Point.V - a.Point.V) * t));
            }
        }

        var unique = intersections
            .DistinctBy(point => PointKey(point, options.GeometryToleranceM))
            .ToList();
        if (unique.Count < 2) return;
        var ordered = OrderAlongPrincipalAxis(unique);
        var piecesAdded = 0;
        foreach (var piece in ClipCoplanarSegment(ordered[0], ordered[^1], region, options))
        {
            if (PolylineLength(piece) < options.MinimumCurveLengthM) continue;
            piecesAdded++;
            candidates.Add(new Candidate(
                PlanarConstraintGeometryKind.Curve,
                piece,
                new SourceData(member, sourceElements, nodes, options.WallLineDofMask)));
        }
        if (piecesAdded == 0 && IsInsideHole(ordered[0], region, options.GeometryToleranceM))
            diagnostics.Add(new("planar_constraint_locus_inside_hole",
                $"Locus source member '{member.ElemTag}' находится внутри отверстия PlanarRegion."));
    }

    static void AddSegmentCandidates(
        ICollection<Candidate> candidates,
        FemMember member,
        IReadOnlyList<FemElement> elements,
        FemNode nodeA,
        FemNode nodeB,
        PlanarRegion region,
        PlanarConstraintDerivationOptions options,
        PlanarDofMask intersectionMask,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        var a = ToLocal(region.Frame, nodeA);
        var b = ToLocal(region.Frame, nodeB);
        if (Math.Abs(a.W) <= options.PlaneToleranceM && Math.Abs(b.W) <= options.PlaneToleranceM)
        {
            AddCoplanarCurve(candidates, member, elements, nodeA, nodeB, a, b, region, options, options.CoplanarBarDofMask, diagnostics);
            return;
        }

        if ((a.W < -options.PlaneToleranceM && b.W > options.PlaneToleranceM) ||
            (a.W > options.PlaneToleranceM && b.W < -options.PlaneToleranceM) ||
            Math.Abs(a.W) <= options.PlaneToleranceM || Math.Abs(b.W) <= options.PlaneToleranceM)
        {
            var t = Math.Abs(a.W) <= options.PlaneToleranceM ? 0 : Math.Abs(b.W) <= options.PlaneToleranceM ? 1 : a.W / (a.W - b.W);
            var point = new LocalPoint(a.U + (b.U - a.U) * t, a.V + (b.V - a.V) * t, 0);
            if (!IsValidRegionPoint(point, region, options))
            {
                if (IsInsideHole(new PlanarPoint2D(point.U, point.V), region, options.GeometryToleranceM))
                    diagnostics.Add(new("planar_constraint_locus_inside_hole",
                        $"Locus source member '{member.ElemTag}' попадает в отверстие PlanarRegion."));
                return;
            }
            var endpoint = t <= options.GeometryToleranceM ? nodeA : t >= 1 - options.GeometryToleranceM ? nodeB : null;
            var mask = endpoint is not null ? options.CommonNodeDofMask : intersectionMask;
            candidates.Add(new Candidate(
                PlanarConstraintGeometryKind.Point,
                [new PlanarPoint2D(point.U, point.V)],
                new SourceData(member, elements, endpoint is null ? [nodeA, nodeB] : [endpoint], mask),
                isNodePoint: endpoint is not null,
                nodeId: endpoint?.Id));
        }
        else if (Math.Abs(a.W - b.W) <= options.PlaneToleranceM)
        {
            diagnostics.Add(new("planar_constraint_plane_intersection_ambiguous",
                $"Пересечение member '{member.ElemTag}' с плоскостью PlanarRegion неоднозначно."));
        }
    }

    static void AddCoplanarCurve(
        ICollection<Candidate> candidates,
        FemMember member,
        IReadOnlyList<FemElement> elements,
        FemNode nodeA,
        FemNode nodeB,
        LocalPoint a,
        LocalPoint b,
        PlanarRegion region,
        PlanarConstraintDerivationOptions options,
        PlanarDofMask mask,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        var pieces = ClipCoplanarSegment(new(a.U, a.V), new(b.U, b.V), region, options);
        foreach (var piece in pieces)
        {
            if (PolylineLength(piece) < options.MinimumCurveLengthM) continue;
            candidates.Add(new Candidate(
                PlanarConstraintGeometryKind.Curve,
                piece,
                new SourceData(member, elements, [nodeA, nodeB], mask)));
        }
        if (pieces.Count == 0 && IsInsideHole(new PlanarPoint2D((a.U + b.U) / 2, (a.V + b.V) / 2), region, options.GeometryToleranceM))
            diagnostics.Add(new("planar_constraint_locus_inside_hole",
                $"Locus source member '{member.ElemTag}' находится внутри отверстия PlanarRegion."));
    }

    static List<Candidate> MergeCurveCandidates(List<Candidate> candidates, PlanarConstraintDerivationOptions options)
    {
        var exact = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var key = $"{candidate.Sources[0].Member.Id}:{candidate.Sources[0].Member.ElemTag}:{candidate.DofMask}:{GeometryKey(candidate.Kind, candidate.Points, options.GeometryToleranceM)}";
            if (!exact.TryGetValue(key, out var existing)) exact[key] = candidate;
            else existing.Sources.AddRange(candidate.Sources);
        }

        var chains = exact.Values.ToList();
        bool changed;
        do
        {
            changed = false;
            for (var i = 0; i < chains.Count && !changed; i++)
            for (var j = i + 1; j < chains.Count && !changed; j++)
            {
                if (chains[i].Sources[0].Member.Id != chains[j].Sources[0].Member.Id ||
                    chains[i].Sources[0].Member.ElemTag != chains[j].Sources[0].Member.ElemTag ||
                    chains[i].DofMask != chains[j].DofMask) continue;
                if (!TryJoin(chains[i], chains[j], options.GeometryToleranceM)) continue;
                chains[i].Sources.AddRange(chains[j].Sources);
                chains.RemoveAt(j);
                changed = true;
            }
        } while (changed);

        return chains;
    }

    static bool TryJoin(Candidate first, Candidate second, double tolerance)
    {
        var a0 = first.Points[0];
        var a1 = first.Points[^1];
        var b0 = second.Points[0];
        var b1 = second.Points[^1];
        if (SamePoint(a1, b0, tolerance))
        {
            first.Points = first.Points.Concat(second.Points.Skip(1)).ToArray();
            return true;
        }
        if (SamePoint(a1, b1, tolerance))
        {
            first.Points = first.Points.Concat(second.Points.Reverse().Skip(1)).ToArray();
            return true;
        }
        if (SamePoint(a0, b1, tolerance))
        {
            first.Points = second.Points.Concat(first.Points.Skip(1)).ToArray();
            return true;
        }
        if (SamePoint(a0, b0, tolerance))
        {
            first.Points = second.Points.Reverse().Concat(first.Points.Skip(1)).ToArray();
            return true;
        }
        return false;
    }

    static List<PlanarPoint2D[]> ClipCoplanarSegment(PlanarPoint2D a, PlanarPoint2D b, PlanarRegion region, PlanarConstraintDerivationOptions options)
    {
        var parameters = new List<double> { 0, 1 };
        foreach (var polygon in new[] { region.Hull }.Concat(region.Holes))
        {
            var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(polygon.X, polygon.Y);
            for (var i = 0; i < x.Length; i++)
            {
                var c = new PlanarPoint2D(x[i], y[i]);
                var d = new PlanarPoint2D(x[(i + 1) % x.Length], y[(i + 1) % y.Length]);
                AddIntersectionParameter(a, b, c, d, options.GeometryToleranceM, parameters);
            }
        }

        var sorted = parameters.Distinct().OrderBy(value => value).ToArray();
        var result = new List<PlanarPoint2D[]>();
        for (var i = 0; i + 1 < sorted.Length; i++)
        {
            var t0 = sorted[i];
            var t1 = sorted[i + 1];
            if (t1 - t0 <= options.GeometryToleranceM) continue;
            var mid = Interpolate(a, b, (t0 + t1) / 2);
            if (!IsValidRegionPoint(new LocalPoint(mid.U, mid.V, 0), region, options)) continue;
            var p0 = Interpolate(a, b, t0);
            var p1 = Interpolate(a, b, t1);
            result.Add([new PlanarPoint2D(p0.U, p0.V), new PlanarPoint2D(p1.U, p1.V)]);
        }
        return result;
    }

    static void AddIntersectionParameter(PlanarPoint2D a, PlanarPoint2D b, PlanarPoint2D c, PlanarPoint2D d, double tolerance, ICollection<double> parameters)
    {
        var r = new PlanarPoint2D(b.U - a.U, b.V - a.V);
        var s = new PlanarPoint2D(d.U - c.U, d.V - c.V);
        var denominator = Cross(r, s);
        var qMinusP = new PlanarPoint2D(c.U - a.U, c.V - a.V);
        if (Math.Abs(denominator) <= tolerance)
        {
            if (Math.Abs(Cross(qMinusP, r)) > tolerance) return;
            var rr = r.U * r.U + r.V * r.V;
            if (rr <= tolerance * tolerance) return;
            parameters.Add(((c.U - a.U) * r.U + (c.V - a.V) * r.V) / rr);
            parameters.Add(((d.U - a.U) * r.U + (d.V - a.V) * r.V) / rr);
            return;
        }
        var t = Cross(qMinusP, s) / denominator;
        var u = Cross(qMinusP, r) / denominator;
        if (t >= -tolerance && t <= 1 + tolerance && u >= -tolerance && u <= 1 + tolerance)
            parameters.Add(Math.Clamp(t, 0, 1));
    }

    static LocalPoint Interpolate(PlanarPoint2D a, PlanarPoint2D b, double t) =>
        new(a.U + (b.U - a.U) * t, a.V + (b.V - a.V) * t, 0);

    static bool IsValidRegionPoint(LocalPoint point, PlanarRegion region, PlanarConstraintDerivationOptions options)
    {
        if (Math.Abs(point.W) > options.PlaneToleranceM) return false;
        var p = new PlanarPoint2D(point.U, point.V);
        if (!IsInsideOrOn(p, region.Hull, options.GeometryToleranceM)) return false;
        return !region.Holes.Any(hole => IsInsideOrOn(p, hole, options.GeometryToleranceM));
    }

    static bool IsInsideHole(PlanarPoint2D point, PlanarRegion region, double tolerance) =>
        region.Holes.Any(hole => IsInsideOrOn(point, hole, tolerance));

    static bool IsInsideOrOn(PlanarPoint2D point, Contour contour, double tolerance)
    {
        var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(contour.X, contour.Y);
        for (var i = 0; i < x.Length; i++)
        {
            if (DistanceToSegmentSquared(point.U, point.V, x[i], y[i], x[(i + 1) % x.Length], y[(i + 1) % y.Length]) <= tolerance * tolerance)
                return true;
        }
        var inside = false;
        for (var i = 0; i < x.Length; i++)
        {
            var j = (i + 1) % x.Length;
            if ((y[i] > point.V) == (y[j] > point.V)) continue;
            if (point.U < (x[j] - x[i]) * (point.V - y[i]) / (y[j] - y[i]) + x[i]) inside = !inside;
        }
        return inside;
    }

    static double DistanceToSegmentSquared(double px, double py, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 1e-24) return (px - ax) * (px - ax) + (py - ay) * (py - ay);
        var t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lengthSquared, 0, 1);
        var x = ax + t * dx;
        var y = ay + t * dy;
        return (px - x) * (px - x) + (py - y) * (py - y);
    }

    static IEnumerable<((FemNode Node, LocalPoint Point) A, (FemNode Node, LocalPoint Point) B)> Edges((FemNode Node, LocalPoint Point)[] nodes)
    {
        for (var i = 0; i < nodes.Length; i++)
            yield return (nodes[i], nodes[(i + 1) % nodes.Length]);
    }

    static PlanarPoint2D[] OrderAlongPrincipalAxis(IReadOnlyList<PlanarPoint2D> points)
    {
        var minU = points.Min(point => point.U);
        var maxU = points.Max(point => point.U);
        var minV = points.Min(point => point.V);
        var maxV = points.Max(point => point.V);
        return (maxU - minU >= maxV - minV
                ? points.OrderBy(point => point.U).ThenBy(point => point.V)
                : points.OrderBy(point => point.V).ThenBy(point => point.U)).ToArray();
    }

    static double PolylineLength(IReadOnlyList<PlanarPoint2D> points)
    {
        double result = 0;
        for (var i = 1; i < points.Count; i++)
            result += Math.Sqrt(Math.Pow(points[i].U - points[i - 1].U, 2) + Math.Pow(points[i].V - points[i - 1].V, 2));
        return result;
    }

    static void AddCandidate(ICollection<Candidate> candidates, Candidate candidate) => candidates.Add(candidate);

    static LocalPoint ToLocal(Frame3D frame, FemNode node)
    {
        var delta = new PlanarVector3(node.X, node.Y, node.Z) - frame.Origin;
        return new(delta.Dot(frame.LocalX), delta.Dot(frame.LocalY), delta.Dot(frame.LocalZ));
    }

    static string GeometryKey(PlanarConstraintGeometryKind kind, IReadOnlyList<PlanarPoint2D> points, double tolerance)
    {
        var forward = string.Join(";", points.Select(point => PointKey(point, tolerance)));
        var reverse = string.Join(";", points.Reverse().Select(point => PointKey(point, tolerance)));
        return $"{kind}:{(string.CompareOrdinal(forward, reverse) <= 0 ? forward : reverse)}";
    }

    static string PointKey(PlanarPoint2D point, double tolerance) =>
        $"{Math.Round(point.U / tolerance):G0},{Math.Round(point.V / tolerance):G0}";

    static bool SamePoint(PlanarPoint2D a, PlanarPoint2D b, double tolerance) =>
        (a.U - b.U) * (a.U - b.U) + (a.V - b.V) * (a.V - b.V) <= tolerance * tolerance;

    static double Cross(PlanarPoint2D a, PlanarPoint2D b) => a.U * b.V - a.V * b.U;

    static string ComputeSourceFingerprint(FemSchemaTopology topology, PlanarRegion region, PlanarConstraintDerivationOptions options)
    {
        var parts = new List<string> { "fem-driven-source-v1", topology.SchemaId.ToString(CultureInfo.InvariantCulture), options.AlgorithmVersion };
        parts.AddRange(new[] { options.PlaneToleranceM, options.GeometryToleranceM, options.MinimumCurveLengthM }.Select(value => value.ToString("G17", CultureInfo.InvariantCulture)));
        parts.Add(options.AutomaticMode.ToString(CultureInfo.InvariantCulture));
        parts.Add(((int)options.CommonNodeDofMask).ToString(CultureInfo.InvariantCulture));
        parts.Add(((int)options.TransverseBarDofMask).ToString(CultureInfo.InvariantCulture));
        parts.Add(((int)options.CoplanarBarDofMask).ToString(CultureInfo.InvariantCulture));
        parts.Add(((int)options.WallLineDofMask).ToString(CultureInfo.InvariantCulture));
        parts.Add(PlanarGeometryFingerprint.Compute(
            region.Contours,
            region.Frame,
            region.BoundarySegments,
            region.ConstraintObjects));
        foreach (var node in topology.Nodes.OrderBy(node => node.Id).ThenBy(node => node.NodeTag, StringComparer.Ordinal))
            parts.Add($"node:{node.Id}:{node.NodeTag}:{node.X:G17}:{node.Y:G17}:{node.Z:G17}");
        foreach (var member in topology.Members.OrderBy(member => member.Id).ThenBy(member => member.ElemTag, StringComparer.Ordinal))
            parts.Add($"member:{member.Id}:{member.ElemTag}:{member.ElemType}:{member.PlanarRegionId}:{member.NodeIdsJson}");
        foreach (var element in topology.Elements.OrderBy(element => element.Id).ThenBy(element => element.ElemTag, StringComparer.Ordinal))
            parts.Add($"element:{element.Id}:{element.ElemTag}:{element.ElemType}:{element.SourceMemberTag}:{element.NodeIdsJson}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts)))).ToLowerInvariant();
    }

    sealed record LocalPoint(double U, double V, double W);

    sealed class SourceData
    {
        public FemMember Member { get; }
        public IReadOnlyList<FemElement> Elements { get; }
        public IReadOnlyList<FemNode> Nodes { get; }
        public PlanarDofMask DofMask { get; }

        public SourceData(FemMember member, IReadOnlyList<FemElement> elements, IReadOnlyList<FemNode> nodes, PlanarDofMask dofMask)
        {
            Member = member;
            Elements = elements;
            Nodes = nodes;
            DofMask = dofMask;
        }
    }

    sealed class Candidate
    {
        public PlanarConstraintGeometryKind Kind { get; }
        public IReadOnlyList<PlanarPoint2D> Points { get; set; }
        public List<SourceData> Sources { get; } = [];
        public bool IsNodePoint { get; }
        public int? NodeId { get; set; }
        public PlanarDofMask DofMask => Sources[0].DofMask;

        public Candidate(PlanarConstraintGeometryKind kind, IReadOnlyList<PlanarPoint2D> points, SourceData source, bool isNodePoint = false, int? nodeId = null)
        {
            Kind = kind;
            Points = points;
            Sources.Add(source);
            IsNodePoint = isNodePoint;
            NodeId = nodeId;
        }
    }

    sealed class GeometryDraft
    {
        public PlanarConstraintGeometryKind Kind { get; }
        public IReadOnlyList<PlanarPoint2D> Points { get; }
        public List<SourceData> Sources { get; } = [];
        public PlanarDofMask DofMask { get; }
        public int? NodeId { get; set; }

        public GeometryDraft(Candidate candidate)
        {
            Kind = candidate.Kind;
            Points = candidate.Points;
            Sources.AddRange(candidate.Sources);
            DofMask = candidate.DofMask;
            NodeId = candidate.NodeId;
        }
    }
}

file static class FiniteExtensions
{
    public static bool IsFinite(this double value) => double.IsFinite(value);
}
