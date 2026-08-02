using System.Text.Json;
using CScore.Fem;
using CScore.Planar;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Переводит точные planar constraint mappings в связи OpenSees.</summary>
public static class PlanarStructuralOpenSeesAdapter
{
    /// <summary>Атомарно применяет structural relations к уже собранной shell-модели.</summary>
    public static PlanarOpenSeesConstraintResult Apply(
        PlanarMeshShellModelResult shellResult,
        PlanarMeshSnapshot snapshot,
        IReadOnlyList<PlanarConstraintObject> constraints,
        FemSchemaTopology topology,
        IReadOnlyDictionary<int, int> sourceNodeTagById,
        PlanarOpenSeesConstraintOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(shellResult);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(constraints);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(sourceNodeTagById);
        options ??= new PlanarOpenSeesConstraintOptions();

        var diagnostics = new List<FemValidationDiagnostic>();
        if (!snapshot.IsCalculable)
            AddError(diagnostics, "planar_opensees_snapshot_not_calculable",
                "Нерасчётный PlanarMeshSnapshot нельзя передать в structural OpenSees adapter.");

        _ = BuildUniqueLookup(
            constraints,
            constraint => constraint.Id,
            "planar_opensees_constraint_duplicate",
            "constraint ID",
            diagnostics);
        var mappingById = BuildUniqueLookup(
            snapshot.ConstraintMappings,
            mapping => mapping.ConstraintObjectId,
            "planar_opensees_mapping_duplicate",
            "mapping ID",
            diagnostics);
        var sourceNodes = BuildUniqueLookup(
            topology.Nodes,
            node => node.Id,
            "planar_opensees_source_node_duplicate",
            "source node ID",
            diagnostics);
        var sourceMembers = BuildUniqueLookup(
            topology.Members,
            member => member.Id,
            "planar_opensees_source_member_duplicate",
            "source member ID",
            diagnostics);
        var sourceElements = BuildUniqueLookup(
            topology.Elements,
            element => element.Id,
            "planar_opensees_source_element_duplicate",
            "source element ID",
            diagnostics);
        var modelNodes = BuildUniqueLookup(
            shellResult.Model.Nodes,
            node => node.Tag,
            "planar_opensees_model_node_duplicate",
            "OpenSees node tag",
            diagnostics);

        var candidates = new List<Candidate>();
        foreach (PlanarConstraintObject constraint in constraints.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (constraint.StructuralFacet.Kind == PlanarStructuralKind.None)
                continue;

            if (!mappingById.TryGetValue(constraint.Id, out PlanarConstraintMeshMapping? mapping))
            {
                AddError(diagnostics, "planar_opensees_constraint_mapping_missing",
                    $"Для constraint '{constraint.Id}' отсутствует mesh mapping.");
                continue;
            }

            foreach (FemValidationDiagnostic diagnostic in mapping.Diagnostics.Where(item => item.IsError))
                diagnostics.Add(diagnostic);

            IReadOnlyList<PlanarStructuralRelation> relations = mapping.StructuralRelations.Count > 0
                ? mapping.StructuralRelations
                : constraint.StructuralRelations;
            IReadOnlyList<PlanarSourceReference> sourceReferences = mapping.SourceReferences.Count > 0
                ? mapping.SourceReferences
                : constraint.SourceReferences;
            if (relations.Count == 0)
            {
                AddError(diagnostics, "planar_opensees_structural_relation_missing",
                    $"Constraint '{constraint.Id}' не содержит structural relation.");
                continue;
            }

            foreach (PlanarStructuralRelation relation in relations
                         .OrderBy(item => item.SourceMemberId)
                         .ThenBy(item => item.SourceMemberTag, StringComparer.Ordinal))
            {
                PlanarStructuralKind kind = relation.Kind == PlanarStructuralKind.None
                    ? constraint.StructuralFacet.Kind
                    : relation.Kind;
                if (!TryResolvePolicy(kind, options, out PlanarOpenSeesConstraintPolicy policy))
                {
                    AddError(diagnostics, kind == PlanarStructuralKind.PointMpc
                        ? "planar_opensees_unsupported_mpc"
                        : "planar_opensees_unsupported_structural_kind",
                        $"Constraint '{constraint.Id}', relation '{relation.SourceMemberTag}' типа '{kind}' не поддерживается текущим OpenSees adapter-ом.");
                    continue;
                }

                PlanarDofMask mask = relation.DofMask != PlanarDofMask.None
                    ? relation.DofMask
                    : constraint.DofMask != PlanarDofMask.None
                        ? constraint.DofMask
                        : constraint.StructuralFacet.DofMask;
                if (!TryResolveDofs(policy, mask, out int[] dofs, out string? dofError))
                {
                    AddError(diagnostics, "planar_opensees_dof_invalid",
                        $"Constraint '{constraint.Id}', relation '{relation.SourceMemberTag}': {dofError}");
                    continue;
                }

                PlanarSourceReference[] matchingReferences = sourceReferences
                    .Where(item => item.MemberId == relation.SourceMemberId &&
                                   string.Equals(item.MemberTag, relation.SourceMemberTag, StringComparison.Ordinal))
                    .ToArray();
                if (matchingReferences.Length != 1)
                {
                    AddError(diagnostics, "planar_opensees_source_reference_ambiguous",
                        $"Constraint '{constraint.Id}', relation '{relation.SourceMemberTag}' имеет {matchingReferences.Length} подходящих source references.");
                    continue;
                }

                PlanarSourceReference sourceReference = matchingReferences[0];
                if (!sourceMembers.ContainsKey(relation.SourceMemberId))
                {
                    AddError(diagnostics, "planar_opensees_source_member_unknown",
                        $"Constraint '{constraint.Id}' ссылается на неизвестный source member {relation.SourceMemberId}.");
                    continue;
                }

                if (constraint.Geometry.Kind == PlanarConstraintGeometryKind.Point)
                {
                    TryAddPointCandidates(
                        candidates, diagnostics, constraint, mapping, relation, sourceReference,
                        policy, dofs, snapshot, shellResult, sourceNodes, modelNodes,
                        sourceNodeTagById);
                }
                else if (constraint.Geometry.Kind == PlanarConstraintGeometryKind.Curve)
                {
                    TryAddCurveCandidates(
                        candidates, diagnostics, constraint, mapping, relation, sourceReference,
                        policy, dofs, snapshot, shellResult, sourceNodes, sourceMembers, sourceElements,
                        modelNodes, sourceNodeTagById);
                }
                else
                {
                    AddError(diagnostics, "planar_opensees_unsupported_mpc",
                        $"Constraint '{constraint.Id}' имеет region geometry, для которой требуется MPC.");
                }
            }
        }

        ValidateExistingRelations(shellResult.Model, modelNodes, diagnostics);
        ValidateCandidateRelations(shellResult.Model, candidates, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.IsError))
            return new PlanarOpenSeesConstraintResult(null, [], diagnostics);

        var orderedCandidates = candidates
            .OrderBy(candidate => candidate.ConstraintObjectId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.SourceMemberId)
            .ThenBy(candidate => candidate.SourceMemberTag, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.SlaveNodeTag)
            .ThenBy(candidate => candidate.MasterNodeTag)
            .GroupBy(candidate => candidate.Identity, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var equalDofs = shellResult.Model.EqualDofConstraints
            .Concat(orderedCandidates
                .Where(candidate => candidate.Policy == PlanarOpenSeesConstraintPolicy.EqualDof)
                .Select(candidate => new ShellEqualDofConstraint(
                    candidate.MasterNodeTag, candidate.SlaveNodeTag, candidate.Dofs)))
            .ToArray();
        var rigidLinks = shellResult.Model.RigidLinks
            .Concat(orderedCandidates
                .Where(candidate => candidate.Policy != PlanarOpenSeesConstraintPolicy.EqualDof)
                .Select(candidate => new ShellRigidLinkConstraint(
                    candidate.MasterNodeTag,
                    candidate.SlaveNodeTag,
                    candidate.Policy == PlanarOpenSeesConstraintPolicy.RigidLinkBar
                        ? ShellRigidLinkType.Bar
                        : ShellRigidLinkType.Beam)))
            .ToArray();
        var model = shellResult.Model with
        {
            EqualDofConstraints = equalDofs,
            RigidLinks = rigidLinks
        };
        var emissions = orderedCandidates.Select(candidate => candidate.ToEmission()).ToArray();
        return new PlanarOpenSeesConstraintResult(model, emissions, diagnostics);
    }

    static void TryAddPointCandidates(
        ICollection<Candidate> candidates,
        ICollection<FemValidationDiagnostic> diagnostics,
        PlanarConstraintObject constraint,
        PlanarConstraintMeshMapping mapping,
        PlanarStructuralRelation relation,
        PlanarSourceReference sourceReference,
        PlanarOpenSeesConstraintPolicy policy,
        IReadOnlyList<int> dofs,
        PlanarMeshSnapshot snapshot,
        PlanarMeshShellModelResult shellResult,
        IReadOnlyDictionary<int, FemNode> sourceNodes,
        IReadOnlyDictionary<int, NormalizedShellNode> modelNodes,
        IReadOnlyDictionary<int, int> sourceNodeTagById)
    {
        if (mapping.PointNodeIndices.Count != 1)
        {
            AddError(diagnostics, "planar_opensees_point_cardinality",
                $"Constraint '{constraint.Id}' требует ровно один host point node, получено {mapping.PointNodeIndices.Count}.");
            return;
        }

        if (sourceReference.NodeIds.Count != 1)
        {
            AddError(diagnostics, "planar_opensees_source_node_cardinality",
                $"Constraint '{constraint.Id}', relation '{relation.SourceMemberTag}' требует ровно один source node для point mapping.");
            return;
        }

        int sourceNodeId = sourceReference.NodeIds[0];
        if (!TryResolveHostNode(
                mapping.PointNodeIndices[0], snapshot, shellResult, modelNodes, constraint.Id,
                diagnostics, out PlanarMeshNode hostMeshNode, out NormalizedShellNode hostNode))
            return;
        if (!sourceNodes.TryGetValue(sourceNodeId, out FemNode? sourceNode))
        {
            AddError(diagnostics, "planar_opensees_source_node_unknown",
                $"Constraint '{constraint.Id}' ссылается на неизвестный source node {sourceNodeId}.");
            return;
        }
        if (!sourceNodeTagById.TryGetValue(sourceNodeId, out int masterTag) || !modelNodes.ContainsKey(masterTag))
        {
            AddError(diagnostics, "planar_opensees_source_node_unknown",
                $"Constraint '{constraint.Id}' не имеет source OpenSees master tag для node {sourceNodeId}.");
            return;
        }
        if (masterTag == hostNode.Tag)
        {
            AddError(diagnostics, "planar_opensees_master_slave_same",
                $"Constraint '{constraint.Id}' использует один и тот же master/slave tag {masterTag}.");
            return;
        }
        if (policy == PlanarOpenSeesConstraintPolicy.EqualDof &&
            !CoordinatesEqual(sourceNode, hostMeshNode, constraint.ToleranceM))
        {
            AddError(diagnostics, "planar_opensees_equal_dof_coordinates_mismatch",
                $"Constraint '{constraint.Id}' требует совпадающие координаты source node {sourceNodeId} и host node {hostMeshNode.Index} для equalDOF.");
            return;
        }

        candidates.Add(new Candidate(
            constraint.Id,
            relation.Kind == PlanarStructuralKind.None ? constraint.StructuralFacet.Kind : relation.Kind,
            policy,
            relation.SourceMemberId,
            relation.SourceMemberTag,
            relation.SourceElementIds,
            relation.SourceElementTags,
            masterTag,
            hostNode.Tag,
            dofs,
            [hostMeshNode.Index],
            [sourceNodeId]));
    }

    static void TryAddCurveCandidates(
        ICollection<Candidate> candidates,
        ICollection<FemValidationDiagnostic> diagnostics,
        PlanarConstraintObject constraint,
        PlanarConstraintMeshMapping mapping,
        PlanarStructuralRelation relation,
        PlanarSourceReference sourceReference,
        PlanarOpenSeesConstraintPolicy policy,
        IReadOnlyList<int> dofs,
        PlanarMeshSnapshot snapshot,
        PlanarMeshShellModelResult shellResult,
        IReadOnlyDictionary<int, FemNode> sourceNodes,
        IReadOnlyDictionary<int, FemMember> sourceMembers,
        IReadOnlyDictionary<int, FemElement> sourceElements,
        IReadOnlyDictionary<int, NormalizedShellNode> modelNodes,
        IReadOnlyDictionary<int, int> sourceNodeTagById)
    {
        if (!TryWalkMeshChain(mapping.OrderedCurveEdges, out int[] hostIndices, out string meshError))
        {
            AddError(diagnostics, "planar_opensees_curve_mapping_invalid",
                $"Constraint '{constraint.Id}': {meshError}");
            return;
        }
        if (!TryBuildSourceChain(
                relation, sourceReference, sourceMembers, sourceElements, out int[] sourceIds, out string sourceError))
        {
            AddError(diagnostics, "planar_opensees_source_curve_invalid",
                $"Constraint '{constraint.Id}', relation '{relation.SourceMemberTag}': {sourceError}");
            return;
        }
        if (hostIndices.Length != sourceIds.Length)
        {
            AddError(diagnostics, "planar_opensees_curve_cardinality",
                $"Constraint '{constraint.Id}' имеет {hostIndices.Length} host nodes и {sourceIds.Length} source nodes.");
            return;
        }

        if (!TryResolveCurvePairing(
                hostIndices, sourceIds, policy, constraint.ToleranceM, snapshot, shellResult,
                sourceNodes, modelNodes, sourceNodeTagById, constraint.Id, diagnostics,
                out (int HostIndex, int SourceId)[] pairs))
            return;

        foreach ((int hostIndex, int sourceId) in pairs)
        {
            if (!TryResolveHostNode(
                    hostIndex, snapshot, shellResult, modelNodes, constraint.Id, diagnostics,
                    out _, out NormalizedShellNode hostNode))
                return;
            if (!sourceNodeTagById.TryGetValue(sourceId, out int masterTag) || !modelNodes.ContainsKey(masterTag))
            {
                AddError(diagnostics, "planar_opensees_source_node_unknown",
                    $"Constraint '{constraint.Id}' не имеет source OpenSees master tag для node {sourceId}.");
                return;
            }
            if (masterTag == hostNode.Tag)
            {
                AddError(diagnostics, "planar_opensees_master_slave_same",
                    $"Constraint '{constraint.Id}' использует один и тот же master/slave tag {masterTag}.");
                return;
            }

            candidates.Add(new Candidate(
                constraint.Id,
                relation.Kind == PlanarStructuralKind.None ? constraint.StructuralFacet.Kind : relation.Kind,
                policy,
                relation.SourceMemberId,
                relation.SourceMemberTag,
                relation.SourceElementIds,
                relation.SourceElementTags,
                masterTag,
                hostNode.Tag,
                dofs,
                [hostIndex],
                [sourceId]));
        }
    }

    static bool TryResolveCurvePairing(
        IReadOnlyList<int> hostIndices,
        IReadOnlyList<int> sourceIds,
        PlanarOpenSeesConstraintPolicy policy,
        double tolerance,
        PlanarMeshSnapshot snapshot,
        PlanarMeshShellModelResult shellResult,
        IReadOnlyDictionary<int, FemNode> sourceNodes,
        IReadOnlyDictionary<int, NormalizedShellNode> modelNodes,
        IReadOnlyDictionary<int, int> sourceNodeTagById,
        string constraintId,
        ICollection<FemValidationDiagnostic> diagnostics,
        out (int HostIndex, int SourceId)[] pairs)
    {
        pairs = [];
        var direct = hostIndices.Zip(sourceIds, (host, source) => (HostIndex: host, SourceId: source)).ToArray();
        var reverse = hostIndices.Zip(sourceIds.Reverse(), (host, source) => (HostIndex: host, SourceId: source)).ToArray();
        if (policy == PlanarOpenSeesConstraintPolicy.EqualDof)
        {
            if (CoordinatesMatch(direct, snapshot, shellResult, sourceNodes, tolerance))
                pairs = direct;
            else if (CoordinatesMatch(reverse, snapshot, shellResult, sourceNodes, tolerance))
                pairs = reverse;
            else
            {
                AddError(diagnostics, "planar_opensees_equal_dof_coordinates_mismatch",
                    $"Constraint '{constraintId}' curve source/host coordinates do not match for equalDOF.");
                return false;
            }
        }
        else
        {
            pairs = direct;
        }

        foreach ((int hostIndex, int sourceId) in pairs)
        {
            if (!sourceNodes.ContainsKey(sourceId))
            {
                AddError(diagnostics, "planar_opensees_source_node_unknown",
                    $"Constraint '{constraintId}' ссылается на неизвестный source node {sourceId}.");
                return false;
            }
            if (!sourceNodeTagById.TryGetValue(sourceId, out int sourceTag) || !modelNodes.ContainsKey(sourceTag))
            {
                AddError(diagnostics, "planar_opensees_source_node_unknown",
                    $"Constraint '{constraintId}' не имеет source OpenSees master tag для node {sourceId}.");
                return false;
            }
            if (!shellResult.NodeIndexToTag.ContainsKey(hostIndex))
            {
                AddError(diagnostics, "planar_opensees_host_node_unknown",
                    $"Constraint '{constraintId}' ссылается на неизвестный host snapshot node {hostIndex}.");
                return false;
            }
        }
        return true;
    }

    static bool CoordinatesMatch(
        IEnumerable<(int HostIndex, int SourceId)> pairs,
        PlanarMeshSnapshot snapshot,
        PlanarMeshShellModelResult shellResult,
        IReadOnlyDictionary<int, FemNode> sourceNodes,
        double tolerance)
    {
        foreach ((int hostIndex, int sourceId) in pairs)
        {
            PlanarMeshNode? host = snapshot.Nodes.FirstOrDefault(node => node.Index == hostIndex);
            if (host is null || !sourceNodes.TryGetValue(sourceId, out FemNode? source) || source is null)
                return false;
            if (!shellResult.NodeIndexToTag.ContainsKey(hostIndex) || !CoordinatesEqual(source, host, tolerance))
                return false;
        }
        return true;
    }

    static bool TryResolveHostNode(
        int hostIndex,
        PlanarMeshSnapshot snapshot,
        PlanarMeshShellModelResult shellResult,
        IReadOnlyDictionary<int, NormalizedShellNode> modelNodes,
        string constraintId,
        ICollection<FemValidationDiagnostic> diagnostics,
        out PlanarMeshNode meshNode,
        out NormalizedShellNode modelNode)
    {
        meshNode = snapshot.Nodes.FirstOrDefault(node => node.Index == hostIndex)!;
        if (meshNode is null || !shellResult.NodeIndexToTag.TryGetValue(hostIndex, out int hostTag) ||
            !modelNodes.TryGetValue(hostTag, out NormalizedShellNode? resolvedModelNode) || resolvedModelNode is null)
        {
            modelNode = null!;
            AddError(diagnostics, "planar_opensees_host_node_unknown",
                $"Constraint '{constraintId}' ссылается на неизвестный host snapshot node {hostIndex}.");
            return false;
        }
        modelNode = resolvedModelNode;
        return true;
    }

    static bool TryWalkMeshChain(
        IReadOnlyList<PlanarMeshEdge> edges,
        out int[] nodes,
        out string error)
    {
        nodes = [];
        error = "curve mapping не содержит ordered edges.";
        if (edges.Count == 0) return false;
        if (TryWalkMeshChainFrom(edges, edges[0].A, edges[0].B, out nodes)) return true;
        if (TryWalkMeshChainFrom(edges, edges[0].B, edges[0].A, out nodes)) return true;
        error = "curve mapping содержит разрыв, ветвление или цикл.";
        return false;
    }

    static bool TryWalkMeshChainFrom(
        IReadOnlyList<PlanarMeshEdge> edges,
        int first,
        int second,
        out int[] nodes)
    {
        var result = new List<int> { first, second };
        var used = new HashSet<int> { 0 };
        for (var i = 1; i < edges.Count; i++)
        {
            PlanarMeshEdge edge = edges[i];
            if (edge.A == result[^1] && edge.B != result[^1])
            {
                result.Add(edge.B);
                used.Add(i);
            }
            else if (edge.B == result[^1] && edge.A != result[^1])
            {
                result.Add(edge.A);
                used.Add(i);
            }
            else
            {
                nodes = [];
                return false;
            }
        }
        nodes = result.ToArray();
        return used.Count == edges.Count && nodes.Distinct().Count() == nodes.Length;
    }

    static bool TryBuildSourceChain(
        PlanarStructuralRelation relation,
        PlanarSourceReference sourceReference,
        IReadOnlyDictionary<int, FemMember> sourceMembers,
        IReadOnlyDictionary<int, FemElement> sourceElements,
        out int[] sourceIds,
        out string error)
    {
        sourceIds = [];
        error = "source curve connectivity отсутствует.";
        var allowed = sourceReference.NodeIds.Distinct().ToHashSet();
        if (allowed.Count < 2) return false;

        var edges = new List<(int A, int B)>();
        foreach (int elementId in relation.SourceElementIds)
        {
            if (!sourceElements.TryGetValue(elementId, out FemElement? element))
            {
                error = $"не найден source element {elementId}.";
                return false;
            }
            int[] ids;
            try { ids = JsonSerializer.Deserialize<int[]>(element.NodeIdsJson) ?? []; }
            catch (JsonException ex)
            {
                error = $"source element {elementId} содержит повреждённый connectivity JSON: {ex.Message}";
                return false;
            }
            if (ids.Length != 2 || ids.Any(id => !allowed.Contains(id)))
            {
                error = $"source element {elementId} не является двухузловым элементом требуемой curve.";
                return false;
            }
            edges.Add((ids[0], ids[1]));
        }

        if (edges.Count == 0 && sourceMembers.TryGetValue(relation.SourceMemberId, out FemMember? member))
        {
            int[] ids;
            try { ids = JsonSerializer.Deserialize<int[]>(member.NodeIdsJson) ?? []; }
            catch (JsonException ex)
            {
                error = $"source member {relation.SourceMemberId} содержит повреждённый connectivity JSON: {ex.Message}";
                return false;
            }
            if (ids.Length == 2 && ids.All(allowed.Contains))
                edges.Add((ids[0], ids[1]));
        }

        if (edges.Count == 0 && allowed.Count == 2)
        {
            sourceIds = allowed.OrderBy(id => id).ToArray();
            return true;
        }
        if (edges.Count == 0)
        {
            error = "source curve не содержит двухузловой connectivity.";
            return false;
        }

        var adjacency = allowed.ToDictionary(id => id, _ => new List<int>());
        foreach ((int a, int b) in edges)
        {
            if (a == b || adjacency[a].Contains(b) || adjacency[b].Contains(a))
            {
                error = "source curve содержит дублирующееся ребро или петлю.";
                return false;
            }
            adjacency[a].Add(b);
            adjacency[b].Add(a);
        }
        int[] endpoints = adjacency.Where(item => item.Value.Count == 1).Select(item => item.Key).ToArray();
        if (endpoints.Length != 2 || adjacency.Any(item => item.Value.Count > 2))
        {
            error = "source curve содержит ветвление или цикл.";
            return false;
        }

        var result = new List<int>();
        var visited = new HashSet<int>();
        int current = endpoints.Min();
        int previous = -1;
        while (true)
        {
            if (!visited.Add(current))
            {
                error = "source curve содержит цикл.";
                return false;
            }
            result.Add(current);
            int next = adjacency[current].FirstOrDefault(id => id != previous, -1);
            if (next < 0) break;
            previous = current;
            current = next;
        }
        if (visited.Count != allowed.Count)
        {
            error = "source curve содержит disconnected connectivity.";
            return false;
        }
        sourceIds = result.ToArray();
        return true;
    }

    static bool TryResolvePolicy(
        PlanarStructuralKind kind,
        PlanarOpenSeesConstraintOptions options,
        out PlanarOpenSeesConstraintPolicy policy)
    {
        switch (kind)
        {
            case PlanarStructuralKind.Tie:
                policy = PlanarOpenSeesConstraintPolicy.EqualDof;
                return true;
            case PlanarStructuralKind.EmbeddedMember:
                policy = options.EmbeddedMemberPolicy;
                return true;
            case PlanarStructuralKind.RigidBody:
                policy = options.RigidBodyPolicy;
                return true;
            default:
                policy = default;
                return false;
        }
    }

    static bool TryResolveDofs(
        PlanarOpenSeesConstraintPolicy policy,
        PlanarDofMask mask,
        out int[] dofs,
        out string? error)
    {
        dofs = [];
        error = null;
        if (mask == PlanarDofMask.None)
        {
            error = "DOF mask пуста.";
            return false;
        }
        int[] requested = Enumerable.Range(1, 6)
            .Where(dof => (mask & (PlanarDofMask)(1 << (dof - 1))) != 0)
            .ToArray();
        switch (policy)
        {
            case PlanarOpenSeesConstraintPolicy.EqualDof:
                dofs = requested;
                return true;
            case PlanarOpenSeesConstraintPolicy.RigidLinkBar when requested.SequenceEqual([1, 2, 3]):
                dofs = [1, 2, 3];
                return true;
            case PlanarOpenSeesConstraintPolicy.RigidLinkBeam when requested.SequenceEqual([1, 2, 3, 4, 5, 6]):
                dofs = [1, 2, 3, 4, 5, 6];
                return true;
            default:
                error = $"DOF mask {mask} несовместима с policy {policy}.";
                return false;
        }
    }

    static void ValidateExistingRelations(
        ShellOpenSeesModel model,
        IReadOnlyDictionary<int, NormalizedShellNode> modelNodes,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        foreach (ShellEqualDofConstraint relation in model.EqualDofConstraints)
        {
            if (!modelNodes.ContainsKey(relation.MasterNode) || !modelNodes.ContainsKey(relation.SlaveNode))
                AddError(diagnostics, "planar_opensees_model_constraint_node_unknown",
                    $"Существующий equalDOF {relation.MasterNode}->{relation.SlaveNode} ссылается на неизвестный node.");
            if (relation.MasterNode == relation.SlaveNode)
                AddError(diagnostics, "planar_opensees_master_slave_same",
                    $"Существующий equalDOF использует один и тот же node {relation.MasterNode}.");
        }
        foreach (ShellRigidLinkConstraint relation in model.RigidLinks)
        {
            if (!modelNodes.ContainsKey(relation.MasterNode) || !modelNodes.ContainsKey(relation.SlaveNode))
                AddError(diagnostics, "planar_opensees_model_constraint_node_unknown",
                    $"Существующий rigidLink {relation.MasterNode}->{relation.SlaveNode} ссылается на неизвестный node.");
            if (relation.MasterNode == relation.SlaveNode)
                AddError(diagnostics, "planar_opensees_master_slave_same",
                    $"Существующий rigidLink использует один и тот же node {relation.MasterNode}.");
        }
    }

    static void ValidateCandidateRelations(
        ShellOpenSeesModel model,
        IReadOnlyList<Candidate> candidates,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        var claims = new Dictionary<(int Node, int Dof), string>();
        foreach (ShellEqualDofConstraint relation in model.EqualDofConstraints)
            foreach (int dof in relation.Dofs)
                claims[(relation.SlaveNode, dof)] = $"existing equalDOF {relation.MasterNode}->{relation.SlaveNode}";
        foreach (ShellRigidLinkConstraint relation in model.RigidLinks)
        {
            int[] dofs = relation.Type == ShellRigidLinkType.Bar ? [1, 2, 3] : [1, 2, 3, 4, 5, 6];
            foreach (int dof in dofs)
                claims[(relation.SlaveNode, dof)] = $"existing rigidLink {relation.MasterNode}->{relation.SlaveNode}";
        }

        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (Candidate candidate in candidates)
        {
            string identity = candidate.Identity;
            if (!emitted.Add(identity))
                continue;
            foreach (int dof in candidate.Dofs)
            {
                var key = (candidate.SlaveNodeTag, dof);
                if (claims.TryGetValue(key, out string? owner))
                {
                    AddError(diagnostics, "planar_opensees_constraint_conflict",
                        $"Constraint '{candidate.ConstraintObjectId}' конфликтует с '{owner}' на slave node {candidate.SlaveNodeTag}, DOF {dof}.");
                    continue;
                }
                claims[key] = candidate.Identity;
            }
        }
    }

    static Dictionary<TKey, TValue> BuildUniqueLookup<TValue, TKey>(
        IEnumerable<TValue> values,
        Func<TValue, TKey> keySelector,
        string duplicateCode,
        string keyDescription,
        ICollection<FemValidationDiagnostic> diagnostics)
        where TKey : notnull
    {
        var result = new Dictionary<TKey, TValue>();
        foreach (TValue value in values)
        {
            TKey key = keySelector(value);
            if (!result.TryAdd(key, value))
                AddError(diagnostics, duplicateCode, $"Повторяющийся {keyDescription} '{key}'.");
        }
        return result;
    }

    static bool CoordinatesEqual(FemNode source, PlanarMeshNode host, double tolerance) =>
        CoordinatesEqual(source.X, source.Y, source.Z, host.X, host.Y, host.Z, tolerance);

    static bool CoordinatesEqual(FemNode source, NormalizedShellNode host, double tolerance) =>
        CoordinatesEqual(source.X, source.Y, source.Z, host.X, host.Y, host.Z, tolerance);

    static bool CoordinatesEqual(
        double sourceX,
        double sourceY,
        double sourceZ,
        double hostX,
        double hostY,
        double hostZ,
        double tolerance) =>
        double.IsFinite(tolerance) && tolerance > 0 &&
        Math.Sqrt(
            Math.Pow(sourceX - hostX, 2) +
            Math.Pow(sourceY - hostY, 2) +
            Math.Pow(sourceZ - hostZ, 2)) <= tolerance;

    static void AddError(ICollection<FemValidationDiagnostic> diagnostics, string code, string message) =>
        diagnostics.Add(new FemValidationDiagnostic(code, message));

    sealed record Candidate(
        string ConstraintObjectId,
        PlanarStructuralKind StructuralKind,
        PlanarOpenSeesConstraintPolicy Policy,
        int SourceMemberId,
        string SourceMemberTag,
        IReadOnlyList<int> SourceElementIds,
        IReadOnlyList<string> SourceElementTags,
        int MasterNodeTag,
        int SlaveNodeTag,
        IReadOnlyList<int> Dofs,
        IReadOnlyList<int> HostSnapshotNodeIndices,
        IReadOnlyList<int> SourceNodeIds)
    {
        public string Identity =>
            $"{Policy}:{MasterNodeTag}:{SlaveNodeTag}:{string.Join(',', Dofs)}";

        public PlanarOpenSeesConstraintEmission ToEmission() => new(
            ConstraintObjectId,
            StructuralKind,
            Policy,
            SourceMemberId,
            SourceMemberTag,
            SourceElementIds,
            SourceElementTags,
            MasterNodeTag,
            SlaveNodeTag,
            Dofs,
            HostSnapshotNodeIndices,
            SourceNodeIds);
    }
}
