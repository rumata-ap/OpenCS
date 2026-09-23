using System.Globalization;
using System.Text.Json;
using CScore.Fem;
using CScore.Fem.Combinations;
using CScore.Planar;

namespace CScore.Submodel;

/// <summary>Узловая нагрузка, уходящая в граничный вектор конца цепочки (глобальные компоненты).</summary>
public sealed record BoundaryNodalLoad(string ParentNodeTag, Dof6 Load, NodalLoadSource Source);

/// <summary>Заданное перемещение в концевом узле: DOF этого конца принудительно кинематический.</summary>
public sealed record InterfaceKinematicLoad(string ParentNodeTag, int Dof, double Value, int SourceNodeId);

/// <summary>Результат классификации нагрузок родителя относительно цепочки.</summary>
public sealed record SubmodelLoadClassification(
    IReadOnlyList<RetainedDistributedLoad> RetainedDistributed,
    IReadOnlyList<RetainedPointLoad> RetainedPoints,
    IReadOnlyList<RetainedNodalLoad> RetainedNodal,
    IReadOnlyList<RetainedKinematicLoad> RetainedKinematic,
    IReadOnlyList<BoundaryNodalLoad> BoundaryNodal,
    IReadOnlyList<InterfaceKinematicLoad> InterfaceKinematic,
    LoadAccounting Accounting,
    LoadCompleteness Completeness,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>
/// Делит разрешённые нагрузки родителя (при λ = 1) на retained / boundary / discarded относительно
/// извлечённой цепочки. Retained-нагрузки переводятся в глобальные компоненты по кадру исходного
/// стержня (<see cref="BeamLocalAxisConvention"/>). Доли участков не зеркалируются: дочерний КЭ —
/// клон родительского с тем же порядком узлов.
/// </summary>
public static class SubmodelLoadClassifier
{
    public static SubmodelLoadClassification Classify(
        SubmodelChainTopology chain,
        string parentSourceType,
        IReadOnlyList<FemNode> parentNodes,
        IReadOnlyList<FemMember> parentMembers,
        IReadOnlyList<FemMeshNode> parentMeshNodes,
        IReadOnlyList<FemElement> parentMeshElements,
        FemResolvedLoads loads)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(loads);
        var diagnostics = new List<FemValidationDiagnostic>();
        var retainedDistributed = new List<RetainedDistributedLoad>();
        var retainedPoints = new List<RetainedPointLoad>();
        var retainedNodal = new List<RetainedNodalLoad>();
        var retainedKinematic = new List<RetainedKinematicLoad>();
        var boundaryNodal = new List<BoundaryNodalLoad>();
        var interfaceKinematic = new List<InterfaceKinematicLoad>();
        int discarded = 0;

        var chainMemberTags = new HashSet<string>(
            chain.Segments.Select(s => s.Segment.SourceMemberTag).OfType<string>(), StringComparer.Ordinal);
        var memberByTag = new Dictionary<string, FemMember>(StringComparer.Ordinal);
        foreach (var member in parentMembers) memberByTag.TryAdd(member.ElemTag, member);
        var nodeByTag = new Dictionary<string, FemNode>(StringComparer.Ordinal);
        foreach (var node in parentNodes)
            if (!string.IsNullOrWhiteSpace(node.NodeTag)) nodeByTag.TryAdd(node.NodeTag, node);
        var nodeById = parentNodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());

        // --- Нагрузки стержней ---
        var segmentation = FemMemberLoadSegmenter.Segment(
            parentMeshNodes, parentMeshElements, parentNodes, parentMembers, loads.MemberLoads);
        var failedLoads = new HashSet<int>();
        foreach (var error in segmentation.Errors)
        {
            bool touchesChain = error.MemberTag is null || chainMemberTags.Contains(error.MemberTag);
            if (touchesChain)
            {
                failedLoads.Add(error.LoadId);
                diagnostics.Add(new(BoundaryScenarioDiagnostics.LoadUnresolved,
                    $"Нагрузка {error.LoadId}, затрагивающая цепочку, не разрешена: {error.Message}", true,
                    error.MemberTag is null ? [] : [error.MemberTag]));
            }
            else
            {
                diagnostics.Add(new(BoundaryScenarioDiagnostics.Info,
                    $"Нагрузка {error.LoadId} стержня {error.MemberTag} вне цепочки не разрешена и не учитывается: {error.Message}", false,
                    [error.MemberTag!]));
            }
        }

        var loadsWithRetainedPart = new HashSet<int>();
        foreach (var piece in segmentation.Distributed)
        {
            if (!chain.SegmentByParentElement.TryGetValue(piece.MeshElementTag, out var segment)) continue;
            var frame = MemberFrame(piece.MemberTag, memberByTag, nodeByTag);
            retainedDistributed.Add(new RetainedDistributedLoad(
                segment.Segment.SubmodelElementTag, piece.AOverL, piece.BOverL,
                ToGlobal(piece.QAtA, piece.CoordinateSystem, frame),
                ToGlobal(piece.QAtB, piece.CoordinateSystem, frame),
                piece.LoadId, piece.MemberTag));
            loadsWithRetainedPart.Add(piece.LoadId);
        }

        foreach (var point in segmentation.InElements)
        {
            if (!chain.SegmentByParentElement.TryGetValue(point.MeshElementTag, out var segment)) { discarded++; continue; }
            var frame = MemberFrame(point.MemberTag, memberByTag, nodeByTag);
            retainedPoints.Add(new RetainedPointLoad(segment.Segment.SubmodelElementTag, point.XOverL,
                ToGlobal(point.Force, point.CoordinateSystem, frame), point.LoadId, point.MemberTag));
        }

        foreach (var point in segmentation.OnNodes)
        {
            var frame = MemberFrame(point.MemberTag, memberByTag, nodeByTag);
            var load = Dof6.FromParts(ToGlobal(point.Force, point.CoordinateSystem, frame),
                ToGlobal(point.Moment, point.CoordinateSystem, frame));
            var source = new NodalLoadSource("member_point_load", point.LoadId, point.MemberTag);
            RouteNodal(point.MeshNodeTag, load, source);
        }

        var distributedLoadIds = loads.MemberLoads
            .Where(l => !l.DistributionType.Equals("point", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Id).Distinct();
        discarded += distributedLoadIds.Count(id => !loadsWithRetainedPart.Contains(id) && !failedLoads.Contains(id));

        // --- Узловые нагрузки конструктивного слоя ---
        var meshTagsBySource = MeshTagsBySourceNode(parentMeshNodes);
        foreach (var nodeLoad in loads.NodeLoads)
        {
            if (!nodeById.TryGetValue(nodeLoad.NodeId, out var node) ||
                !meshTagsBySource.TryGetValue(node.NodeTag, out var meshTag))
            {
                discarded++;
                diagnostics.Add(new(BoundaryScenarioDiagnostics.Info,
                    $"Узловая нагрузка узла {nodeLoad.NodeId} не отображается на расчётную сетку и не относится к цепочке.", false, []));
                continue;
            }
            RouteNodal(meshTag, new Dof6(nodeLoad.Fx, nodeLoad.Fy, nodeLoad.Fz, nodeLoad.Mx, nodeLoad.My, nodeLoad.Mz),
                new NodalLoadSource("node_load", node.Id, node.NodeTag));
        }

        // --- Кинематические нагрузки ---
        foreach (var kinematic in loads.KinematicLoads)
        {
            if (!nodeById.TryGetValue(kinematic.NodeId, out var node) ||
                !meshTagsBySource.TryGetValue(node.NodeTag, out var meshTag) ||
                kinematic.Dof is < 1 or > 6)
            {
                discarded++;
                continue;
            }
            int dof = kinematic.Dof - 1;
            if (chain.IsEnd(meshTag))
                interfaceKinematic.Add(new InterfaceKinematicLoad(meshTag, dof, kinematic.Value, node.Id));
            else if (chain.IsInterior(meshTag))
                retainedKinematic.Add(new RetainedKinematicLoad(chain.ChildNodeByParent[meshTag], dof, kinematic.Value, node.Id));
            else
                discarded++;
        }

        var completeness = parentSourceType is "internal" or "opensees" ? LoadCompleteness.Known : LoadCompleteness.Unknown;
        if (completeness == LoadCompleteness.Unknown)
            diagnostics.Add(new(BoundaryScenarioDiagnostics.LoadsUnknown,
                $"Родительская схема из источника '{parentSourceType}' не хранит нагрузки: нагрузки на цепочке неизвестны, сценарий неполный.", false, []));

        int retained = retainedDistributed.Count + retainedPoints.Count + retainedNodal.Count + retainedKinematic.Count;
        int boundary = boundaryNodal.Count + interfaceKinematic.Count;
        diagnostics.Add(new(BoundaryScenarioDiagnostics.Info,
            $"Нагрузки: retained {retained}, boundary {boundary}, discarded {discarded}.", false, []));

        return new SubmodelLoadClassification(retainedDistributed, retainedPoints, retainedNodal, retainedKinematic,
            boundaryNodal, interfaceKinematic, new LoadAccounting(retained, boundary, discarded), completeness, diagnostics);

        void RouteNodal(string meshNodeTag, Dof6 load, NodalLoadSource source)
        {
            if (chain.IsEnd(meshNodeTag))
                boundaryNodal.Add(new BoundaryNodalLoad(meshNodeTag, load, source));
            else if (chain.IsInterior(meshNodeTag))
                retainedNodal.Add(new RetainedNodalLoad(chain.ChildNodeByParent[meshNodeTag], load, source));
            else
                discarded++;
        }
    }

    /// <summary>Канонический тег mesh-узла по тегу конструктивного узла (первый найденный).</summary>
    static Dictionary<string, string> MeshTagsBySourceNode(IReadOnlyList<FemMeshNode> meshNodes)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in meshNodes)
            if (!string.IsNullOrWhiteSpace(node.SourceNodeTag) && FemMeshTopology.CanonicalNodeTag(node.NodeTag) is string tag)
                result.TryAdd(node.SourceNodeTag, tag);
        return result;
    }

    static (PlanarVector3 X, PlanarVector3 Y, PlanarVector3 Z) MemberFrame(
        string memberTag, Dictionary<string, FemMember> memberByTag, Dictionary<string, FemNode> nodeByTag)
    {
        // Сегментатор уже проверил стержень, его узлы и длину для всех выданных им кусков.
        var member = memberByTag[memberTag];
        int[] ends = JsonSerializer.Deserialize<int[]>(member.NodeIdsJson)!;
        var i = nodeByTag[ends[0].ToString(CultureInfo.InvariantCulture)];
        var j = nodeByTag[ends[1].ToString(CultureInfo.InvariantCulture)];
        return BeamLocalAxisConvention.Frame(new PlanarVector3(i.X, i.Y, i.Z), new PlanarVector3(j.X, j.Y, j.Z), member.RotationDeg);
    }

    static PlanarVector3 ToGlobal(PlanarVector3 value, string coordinateSystem,
        (PlanarVector3 X, PlanarVector3 Y, PlanarVector3 Z) frame) =>
        coordinateSystem.Equals("local", StringComparison.OrdinalIgnoreCase)
            ? frame.X * value.X + frame.Y * value.Y + frame.Z * value.Z
            : value;
}
