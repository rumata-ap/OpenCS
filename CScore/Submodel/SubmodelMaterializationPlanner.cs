using CScore.Fem;
using CScore.Planar;

namespace CScore.Submodel;

/// <summary>Входы планировщика: извлечение, его граничный сценарий, дочерний mesh и узлы родителя.</summary>
public sealed record SubmodelMaterializationInput(
    SubmodelExtraction Extraction,
    BoundaryScenario Scenario,
    IReadOnlyList<FemMeshNode> ChildMeshNodes,
    IReadOnlyList<FemElement> ChildMeshElements,
    IReadOnlyList<FemNode> ParentNodes);

/// <summary>
/// Превращает граничный сценарий в конструктивный слой дочерней схемы один к одному по mesh-снимку:
/// узел на mesh-узел, стержень на КЭ, одно загружение «Граничный сценарий λ = 1» и линейная постановка
/// сверки. Недостающие до ранга 6 жёсткие моды фиксируются <c>sp</c> по перемещениям родителя на
/// силовых DOF (сила остаётся приложенной, реакция служит невязкой). Без БД и OpenSees.
/// </summary>
public static class SubmodelMaterializationPlanner
{
    public const string LoadCaseTag = "Граничный сценарий λ = 1";
    public const string AnalysisTag = "Сверка с родителем λ = 1";
    const int LoadCaseId = 1;

    public static SubmodelMaterializationBuild Plan(SubmodelMaterializationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var extraction = input.Extraction;
        var scenario = input.Scenario;
        var diagnostics = new List<FemValidationDiagnostic>();

        if (scenario.ExtractionId != extraction.Id)
            return Fail(diagnostics, SubmodelMaterializationDiagnostics.ScenarioBlocked,
                $"Сценарий относится к извлечению #{scenario.ExtractionId}, а не к #{extraction.Id}.");
        diagnostics.AddRange(SubmodelMeshIntegrity.Check(extraction, input.ChildMeshNodes, input.ChildMeshElements, checkIds: false));
        if (diagnostics.Any(d => d.IsError)) return new(null, diagnostics);
        if (scenario.Status != ScenarioStatus.Blocked && ScenarioReferencesMissing(input) is { } missing)
            return Fail(diagnostics, SubmodelMaterializationDiagnostics.ScenarioBlocked, missing);
        if (scenario.Status == ScenarioStatus.Blocked)
            return Fail(diagnostics, SubmodelMaterializationDiagnostics.ScenarioBlocked,
                "Граничный сценарий заблокирован — материализация невозможна; см. диагностику сценария.");
        if (scenario.Status == ScenarioStatus.Incomplete)
            diagnostics.Add(new(SubmodelMaterializationDiagnostics.LoadsUnknown,
                "Нагрузки родителя неизвестны: на цепочке могут отсутствовать нагрузки, сверка покажет расхождение.", false, []));

        var meshNodeByTag = input.ChildMeshNodes.ToDictionary(n => n.NodeTag, StringComparer.Ordinal);
        var meshElementByTag = input.ChildMeshElements.ToDictionary(e => e.ElemTag, StringComparer.Ordinal);
        var parentNodeByTag = new Dictionary<string, FemNode>(StringComparer.Ordinal);
        foreach (var node in input.ParentNodes) parentNodeByTag.TryAdd(node.NodeTag, node);
        var endByTag = scenario.Ends.ToDictionary(e => e.ChildNodeTag, StringComparer.Ordinal);
        var provenanceByTag = extraction.Nodes.ToDictionary(n => n.SubmodelNodeTag, StringComparer.Ordinal);

        // Узлы: по одному на mesh-узел, временные Id 1..n в порядке mesh-снимка.
        var nodes = new List<FemNode>();
        foreach (var mesh in input.ChildMeshNodes)
        {
            int mask = endByTag.TryGetValue(mesh.NodeTag, out var end)
                ? FixedMask(end)
                : provenanceByTag.TryGetValue(mesh.NodeTag, out var provenance)
                  && provenance.SourceNodeTag is { } sourceTag && parentNodeByTag.TryGetValue(sourceTag, out var parentNode)
                    ? parentNode.DofMask
                    : 0;
            nodes.Add(new FemNode { Id = nodes.Count + 1, NodeTag = mesh.NodeTag, X = mesh.X, Y = mesh.Y, Z = mesh.Z, DofMask = mask });
        }
        var nodeIdByTag = nodes.ToDictionary(n => n.NodeTag, n => n.Id, StringComparer.Ordinal);

        // Стержни: по одному на сегмент (КЭ), в порядке цепочки.
        var members = new List<FemMember>();
        var lengthByTag = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var segment in extraction.Segments.OrderBy(s => s.Ordinal))
        {
            var element = meshElementByTag[segment.SubmodelElementTag];
            var ends = FemMeshTopology.ReadNodeTags(element, 2)!;
            lengthByTag[element.ElemTag] = Distance(meshNodeByTag[ends[0]], meshNodeByTag[ends[1]]);
            if (segment.BetaSource == BetaSource.Absent || element.CrossSectionId is null)
                diagnostics.Add(new(SubmodelMaterializationDiagnostics.MemberIncomplete,
                    $"КЭ {element.ElemTag}: " + (element.CrossSectionId is null ? "не назначено сечение." : "неизвестен угол β местной системы."),
                    true, [element.ElemTag]));
            members.Add(new FemMember
            {
                Id = members.Count + 1, ElemTag = element.ElemTag, ElemType = "beam", NodeIdsJson = element.NodeIdsJson,
                CrossSectionId = element.CrossSectionId, GjStrategy = element.GjStrategy, GjManualValue = element.GjManualValue,
                GjTorsionTaskId = element.GjTorsionTaskId, RotationDeg = segment.BetaDeg
            });
        }
        var memberIdByTag = members.ToDictionary(m => m.ElemTag, m => m.Id, StringComparer.Ordinal);

        // Mesh-копии со ссылками на собственный конструктивный слой.
        var firstMemberOfNode = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in members)
            foreach (var tag in FemMeshTopology.ReadNodeTags(meshElementByTag[member.ElemTag], 2)!)
                firstMemberOfNode.TryAdd(tag, member.ElemTag);
        var meshNodes = input.ChildMeshNodes.Select(n => new FemMeshNode
        {
            Id = n.Id, SchemaId = n.SchemaId, NodeTag = n.NodeTag, X = n.X, Y = n.Y, Z = n.Z,
            SourceNodeTag = n.NodeTag, SourceMemberTag = firstMemberOfNode.GetValueOrDefault(n.NodeTag)
        }).ToList();
        var meshElements = input.ChildMeshElements.Select(e => new FemElement
        {
            Id = e.Id, SchemaId = e.SchemaId, ElemTag = e.ElemTag, ElemType = e.ElemType, NodeIdsJson = e.NodeIdsJson,
            SourceMemberTag = e.ElemTag, SectionTag = e.SectionTag, MaterialTag = e.MaterialTag, ThicknessM = e.ThicknessM,
            CrossSectionId = e.CrossSectionId, GjStrategy = e.GjStrategy, GjManualValue = e.GjManualValue,
            GjTorsionTaskId = e.GjTorsionTaskId
        }).ToList();

        // Нагрузки, оставшиеся на цепочке (глобальные компоненты).
        var memberLoads = new List<FemMemberLoad>();
        foreach (var load in scenario.RetainedDistributedLoads)
        {
            double length = lengthByTag[load.ChildElementTag];
            memberLoads.Add(new FemMemberLoad
            {
                LoadCaseId = LoadCaseId, MemberId = memberIdByTag[load.ChildElementTag], CoordinateSystem = "global",
                DistributionType = load.QAtA == load.QAtB ? "uniform" : "trapezoidal",
                StartOffsetM = load.AOverL * length, EndOffsetM = (1 - load.BOverL) * length,
                QxStart = load.QAtA.X, QyStart = load.QAtA.Y, QzStart = load.QAtA.Z,
                QxEnd = load.QAtB.X, QyEnd = load.QAtB.Y, QzEnd = load.QAtB.Z
            });
        }
        foreach (var load in scenario.RetainedPointLoads)
            memberLoads.Add(new FemMemberLoad
            {
                LoadCaseId = LoadCaseId, MemberId = memberIdByTag[load.ChildElementTag], CoordinateSystem = "global",
                DistributionType = "point", StartOffsetM = load.XOverL * lengthByTag[load.ChildElementTag],
                QxStart = load.Force.X, QyStart = load.Force.Y, QzStart = load.Force.Z
            });

        var nodeLoads = scenario.RetainedNodalLoads
            .Select(l => NodeLoad(nodeIdByTag[l.ChildNodeTag], l.Load)).ToList();
        var kinematicLoads = scenario.RetainedKinematicLoads
            .Select(l => Kinematic(nodeIdByTag[l.ChildNodeTag], l.Dof, l.Value)).ToList();

        // Концы: силы — в режиме Force, заданные перемещения — в режиме Kinematic.
        foreach (var end in scenario.Ends)
        {
            int nodeId = nodeIdByTag[end.ChildNodeTag];
            var force = new double[6];
            for (int dof = 0; dof < 6; dof++)
            {
                var assignment = end.Dofs[dof];
                if (assignment.Mode == DofMode.Force) force[dof] = assignment.Value ?? 0;
                else if (assignment.Mode == DofMode.Kinematic) kinematicLoads.Add(Kinematic(nodeId, dof, assignment.Value ?? 0));
            }
            if (force.Any(v => v != 0))
                nodeLoads.Add(NodeLoad(nodeId, new Dof6(force[0], force[1], force[2], force[3], force[4], force[5])));
        }

        // Фиксация жёстких мод.
        var gauge = Gauge(extraction, scenario, meshNodeByTag, nodes, nodeIdByTag, kinematicLoads, diagnostics);

        var loadCase = new FemLoadCase { Id = LoadCaseId, Tag = LoadCaseTag };
        var analysis = new FemAnalysis
        {
            Tag = AnalysisTag, Kind = "linear",
            LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [LoadCaseId] }.ToJson()
        };

        diagnostics.Add(new(SubmodelMaterializationDiagnostics.Info,
            $"Создано: узлов {nodes.Count}, стержней {members.Count}, узловых нагрузок {nodeLoads.Count}, " +
            $"нагрузок стержней {memberLoads.Count}, заданных перемещений {kinematicLoads.Count}.", false, []));

        if (diagnostics.Any(d => d.IsError)) return new(null, diagnostics);

        var summary = new SubmodelMaterializationSummary(extraction.Id, gauge.RankBefore, gauge.Dofs,
            nodes.Count, members.Count, nodeLoads.Count, memberLoads.Count, kinematicLoads.Count, diagnostics);
        var plan = new SubmodelMaterializationPlan(nodes, members, loadCase, nodeLoads, memberLoads, kinematicLoads,
            analysis, meshNodes, meshElements, summary);
        return new(plan, diagnostics);
    }

    static (int RankBefore, IReadOnlyList<GaugeDof> Dofs) Gauge(SubmodelExtraction extraction, BoundaryScenario scenario,
        IReadOnlyDictionary<string, FemMeshNode> meshNodeByTag, IReadOnlyList<FemNode> nodes,
        IReadOnlyDictionary<string, int> nodeIdByTag, List<FemKinematicLoad> kinematicLoads,
        List<FemValidationDiagnostic> diagnostics)
    {
        var start = scenario.Ends.Single(e => e.AtStart);
        var finish = scenario.Ends.Single(e => !e.AtStart);
        var startPoint = Point(meshNodeByTag[start.ChildNodeTag]);
        var finishPoint = Point(meshNodeByTag[finish.ChildNodeTag]);

        var constrained = new List<KinematicDofRef>();
        var endTags = new HashSet<string>([start.ChildNodeTag, finish.ChildNodeTag], StringComparer.Ordinal);
        foreach (var end in scenario.Ends)
            for (int dof = 0; dof < 6; dof++)
                if (end.Dofs[dof].Mode is DofMode.Fixed or DofMode.Kinematic)
                    constrained.Add(new(Point(meshNodeByTag[end.ChildNodeTag]), dof));
        foreach (var node in nodes.Where(n => !endTags.Contains(n.NodeTag)))
            for (int dof = 0; dof < 6; dof++)
                if ((node.DofMask & (1 << dof)) != 0)
                    constrained.Add(new(Point(meshNodeByTag[node.NodeTag]), dof));
        foreach (var load in scenario.RetainedKinematicLoads)
            constrained.Add(new(Point(meshNodeByTag[load.ChildNodeTag]), load.Dof));

        var candidates = new List<GaugeCandidate>();
        foreach (var (end, point) in new[] { (start, startPoint), (finish, finishPoint) })
            for (int dof = 0; dof < 6; dof++)
                if (end.Dofs[dof].Mode == DofMode.Force)
                    candidates.Add(new(end.AtStart, point, dof));

        var result = RigidModeGauge.Complete(startPoint, (finishPoint - startPoint).Length, constrained, candidates);
        if (!result.Complete)
            diagnostics.Add(new(SubmodelMaterializationDiagnostics.GaugeUnavailable,
                $"Жёсткие моды цепочки не зафиксированы: ранг {result.RankBefore + result.Added.Count} из 6, силовых DOF недостаточно.",
                true, []));

        var dofs = new List<GaugeDof>();
        foreach (var candidate in result.Added)
        {
            var end = candidate.AtStart ? start : finish;
            if (end.Displacement is not { } displacement)
            {
                diagnostics.Add(new(SubmodelMaterializationDiagnostics.GaugeUnavailable,
                    $"Для фиксации жёстких мод нужно перемещение родителя в узле {end.ParentNodeTag}, но его нет в результате.",
                    true, [end.ParentNodeTag]));
                break;
            }
            double value = displacement[candidate.Dof];
            dofs.Add(new GaugeDof(candidate.AtStart, end.ChildNodeTag, candidate.Dof, value));
            kinematicLoads.Add(Kinematic(nodeIdByTag[end.ChildNodeTag], candidate.Dof, value));
        }

        diagnostics.Add(new(SubmodelMaterializationDiagnostics.Info,
            $"Ранг жёстких мод до дополнения: {result.RankBefore}; фиксация: " +
            (dofs.Count == 0 ? "не нужна." : string.Join(", ", dofs.Select(d => $"узел {d.ChildNodeTag} DOF {d.Dof + 1}")) + "."),
            false, []));
        return (result.RankBefore, dofs);
    }

    /// <summary>Сообщение о первом объекте сценария, которого нет в дочернем mesh; null — всё на месте.</summary>
    static string? ScenarioReferencesMissing(SubmodelMaterializationInput input)
    {
        var nodeTags = input.ChildMeshNodes.Select(n => n.NodeTag).ToHashSet(StringComparer.Ordinal);
        var elementTags = input.ChildMeshElements.Select(e => e.ElemTag).ToHashSet(StringComparer.Ordinal);
        var scenario = input.Scenario;
        if (scenario.Ends.Count != 2 || scenario.Ends.Count(e => e.AtStart) != 1)
            return "Сценарий должен описывать ровно два конца цепочки (начальный и конечный).";
        foreach (var end in scenario.Ends)
        {
            if (!nodeTags.Contains(end.ChildNodeTag)) return $"Конец сценария ссылается на отсутствующий узел {end.ChildNodeTag}.";
            if (end.Dofs.Count != 6) return $"Конец {end.ChildNodeTag}: ожидается 6 DOF, в сценарии {end.Dofs.Count}.";
        }
        foreach (var tag in scenario.RetainedDistributedLoads.Select(l => l.ChildElementTag)
                     .Concat(scenario.RetainedPointLoads.Select(l => l.ChildElementTag)))
            if (!elementTags.Contains(tag)) return $"Нагрузка сценария ссылается на отсутствующий КЭ {tag}.";
        foreach (var tag in scenario.RetainedNodalLoads.Select(l => l.ChildNodeTag)
                     .Concat(scenario.RetainedKinematicLoads.Select(l => l.ChildNodeTag)))
            if (!nodeTags.Contains(tag)) return $"Нагрузка сценария ссылается на отсутствующий узел {tag}.";
        return null;
    }

    static int FixedMask(ScenarioEnd end)
    {
        int mask = 0;
        for (int dof = 0; dof < 6; dof++)
            if (end.Dofs[dof].Mode == DofMode.Fixed) mask |= 1 << dof;
        return mask;
    }

    static FemNodeLoad NodeLoad(int nodeId, Dof6 load) => new()
    {
        LoadCaseId = LoadCaseId, NodeId = nodeId,
        Fx = load.X, Fy = load.Y, Fz = load.Z, Mx = load.Rx, My = load.Ry, Mz = load.Rz
    };

    static FemKinematicLoad Kinematic(int nodeId, int dof, double value) =>
        new() { LoadCaseId = LoadCaseId, NodeId = nodeId, Dof = dof + 1, Value = value };

    static PlanarVector3 Point(FemMeshNode node) => new(node.X, node.Y, node.Z);

    static double Distance(FemMeshNode a, FemMeshNode b) => (Point(b) - Point(a)).Length;

    static SubmodelMaterializationBuild Fail(List<FemValidationDiagnostic> diagnostics, string code, string message)
    {
        diagnostics.Add(new(code, message, true, []));
        return new(null, diagnostics);
    }
}
