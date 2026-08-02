using CScore.Fem;

namespace CScore.Planar;

/// <summary>Узловое force/moment действие в глобальной системе.</summary>
public sealed record PlanarNodalAction(int NodeIndex, PlanarVector3 ForceGlobal, PlanarVector3 MomentGlobal);

/// <summary>Результат переноса normalized boundary actions на mesh snapshot.</summary>
public sealed class PlanarBoundaryActionMeshMappingResult
{
    public bool IsCalculable => !Diagnostics.Any(diagnostic => diagnostic.IsError);
    public IReadOnlyList<FemValidationDiagnostic> Diagnostics { get; init; } = [];
    public IReadOnlyList<PlanarNodalAction> NodalActions { get; init; } = [];
    public IReadOnlyDictionary<(int NodeIndex, int Dof), double> PrescribedDofs { get; init; } =
        new Dictionary<(int, int), double>();
    public IReadOnlySet<(int NodeIndex, int Dof)> PreservedSupportDofs { get; init; } =
        new HashSet<(int, int)>();
    public PlanarVector3 AppliedForceGlobal { get; init; }
    public PlanarVector3 AppliedMomentGlobal { get; init; }
    public PlanarVector3 MappedForceGlobal { get; init; }
    public PlanarVector3 MappedMomentGlobal { get; init; }
    public PlanarCutInterfaceMeshMapping? Mapping { get; init; }
}

/// <summary>Интегрирует boundary actions по ordered interface mesh chain.</summary>
public static class PlanarBoundaryActionMeshMapper
{
    /// <summary>Переносит force/kinematic actions на узлы snapshot.</summary>
    public static PlanarBoundaryActionMeshMappingResult Map(
        PlanarCutInterface cut,
        PlanarMeshSnapshot snapshot,
        PlanarBoundaryActionSet actions,
        PlanarCutInterfaceMeshMapping mapping,
        double relativeTolerance = 1e-9,
        double absoluteTolerance = 1e-9)
    {
        ArgumentNullException.ThrowIfNull(cut);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(mapping);
        var diagnostics = new List<FemValidationDiagnostic>(actions.Diagnostics);
        diagnostics.AddRange(mapping.Diagnostics);
        if (mapping.InterfaceId != cut.Id)
            diagnostics.Add(new("planar_boundary_mapping_interface_mismatch", "Mesh mapping относится к другому interface."));
        if (mapping.SnapshotId != snapshot.Id || !string.Equals(mapping.SnapshotFingerprint, snapshot.InputFingerprint, StringComparison.Ordinal))
            diagnostics.Add(new("planar_boundary_mapping_stale", "Mesh mapping относится к другому snapshot fingerprint."));
        if (!mapping.IsCalculable || mapping.OrderedNodes.Count < 2)
            diagnostics.Add(new("planar_boundary_mapping_incomplete", "Mesh mapping cut interface нерасчётен или не содержит цепочку."));

        var nodal = new Dictionary<int, (PlanarVector3 Force, PlanarVector3 Moment)>();
        var prescribed = new Dictionary<(int NodeIndex, int Dof), double>();
        var preserved = new HashSet<(int NodeIndex, int Dof)>();
        PlanarVector3 appliedForce = PlanarVector3.Zero;
        PlanarVector3 appliedMoment = PlanarVector3.Zero;
        var nodesByIndex = snapshot.Nodes.ToDictionary(node => node.Index);

        foreach (var action in actions.ForceActions)
        {
            diagnostics.AddRange(action.Validate());
            if (action.InterfaceId != cut.Id) diagnostics.Add(new("planar_boundary_action_interface_mismatch", "Force action относится к другому interface."));
            MapForce(action, mapping, nodesByIndex, nodal, ref appliedForce, ref appliedMoment, diagnostics);
        }

        foreach (var action in actions.KinematicActions)
        {
            diagnostics.AddRange(action.Validate());
            if (action.InterfaceId != cut.Id) diagnostics.Add(new("planar_boundary_action_interface_mismatch", "Kinematic action относится к другому interface."));
            MapKinematic(action, mapping, prescribed, diagnostics);
        }

        AddPreservedSupport(cut.ModeByDof, mapping.OrderedNodes, preserved);
        var nodalActions = nodal
            .OrderBy(pair => pair.Key)
            .Select(pair => new PlanarNodalAction(pair.Key, pair.Value.Force, pair.Value.Moment))
            .ToArray();
        PlanarVector3 mappedForce = PlanarVector3.Zero;
        PlanarVector3 mappedMoment = PlanarVector3.Zero;
        foreach (var action in nodalActions)
        {
            mappedForce += action.ForceGlobal;
            if (nodesByIndex.TryGetValue(action.NodeIndex, out var node))
                mappedMoment += Position(node).Cross(action.ForceGlobal) + action.MomentGlobal;
            else
                diagnostics.Add(new("planar_boundary_mapping_node_unknown", $"Nodal action ссылается на неизвестный node {action.NodeIndex}."));
        }

        AddBalanceDiagnostic(diagnostics, "force", appliedForce, mappedForce, absoluteTolerance, relativeTolerance);
        AddBalanceDiagnostic(diagnostics, "moment", appliedMoment, mappedMoment, absoluteTolerance, relativeTolerance);
        return new()
        {
            Diagnostics = diagnostics,
            NodalActions = nodalActions,
            PrescribedDofs = prescribed,
            PreservedSupportDofs = preserved,
            AppliedForceGlobal = appliedForce,
            AppliedMomentGlobal = appliedMoment,
            MappedForceGlobal = mappedForce,
            MappedMomentGlobal = mappedMoment,
            Mapping = mapping
        };
    }

    static void MapForce(
        PlanarBoundaryForceAction action,
        PlanarCutInterfaceMeshMapping mapping,
        IReadOnlyDictionary<int, PlanarMeshNode> nodes,
        IDictionary<int, (PlanarVector3 Force, PlanarVector3 Moment)> nodal,
        ref PlanarVector3 appliedForce,
        ref PlanarVector3 appliedMoment,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        if (mapping.OrderedNodes.Count < 2) return;
        PlanarVector3 referencePoint = PlanarBoundaryFrameConverter.ToGlobalPoint(action.Frame, action.ReferencePoint);
        for (int index = 1; index < mapping.OrderedNodes.Count; index++)
        {
            var first = mapping.OrderedNodes[index - 1];
            var second = mapping.OrderedNodes[index];
            if (!nodes.ContainsKey(first.NodeIndex) || !nodes.ContainsKey(second.NodeIndex))
            {
                diagnostics.Add(new("planar_boundary_mapping_node_unknown", "Force mapping содержит неизвестный node."));
                continue;
            }
            PlanarVector3 a = first.Position;
            PlanarVector3 b = second.Position;
            double length = (b - a).Length;
            if (length <= 1e-12)
            {
                diagnostics.Add(new("planar_boundary_mapping_degenerate_edge", "Force mapping содержит нулевое ребро."));
                continue;
            }

            var q0 = ToGlobal(action.Frame, Evaluate(action.Samples, first.S, action.Interpolation, force: true).Force);
            var q1 = ToGlobal(action.Frame, Evaluate(action.Samples, second.S, action.Interpolation, force: true).Force);
            var m0 = ToGlobal(action.Frame, Evaluate(action.Samples, first.S, action.Interpolation, force: true).Moment);
            var m1 = ToGlobal(action.Frame, Evaluate(action.Samples, second.S, action.Interpolation, force: true).Moment);
            AddNodal(
                nodal,
                first.NodeIndex,
                q0 * (length / 6.0 * 2),
                (m0 + referencePoint.Cross(q0)) * (length / 6.0 * 2));
            AddNodal(
                nodal,
                second.NodeIndex,
                q1 * (length / 6.0 * 2),
                (m1 + referencePoint.Cross(q1)) * (length / 6.0 * 2));
            AddNodal(
                nodal,
                first.NodeIndex,
                q1 * (length / 6.0),
                (m1 + referencePoint.Cross(q1)) * (length / 6.0));
            AddNodal(
                nodal,
                second.NodeIndex,
                q0 * (length / 6.0),
                (m0 + referencePoint.Cross(q0)) * (length / 6.0));

            const double g = 0.5773502691896257645;
            for (int gauss = 0; gauss < 2; gauss++)
            {
                double xi = gauss == 0 ? -g : g;
                double t = (1 + xi) / 2;
                var point = a + (b - a) * t;
                var force = q0 + (q1 - q0) * t;
                var moment = m0 + (m1 - m0) * t;
                appliedForce += force * (length / 2);
                appliedMoment += point.Cross(force) * (length / 2) + moment * (length / 2);
            }
            appliedMoment += referencePoint.Cross((q0 + q1) * (length / 2));
        }
    }

    static void MapKinematic(
        PlanarBoundaryKinematicAction action,
        PlanarCutInterfaceMeshMapping mapping,
        IDictionary<(int NodeIndex, int Dof), double> prescribed,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        foreach (var node in mapping.OrderedNodes)
        {
            var sample = Evaluate(action.Samples, node.S, action.Interpolation, force: false);
            PlanarVector3 displacement = ToGlobal(action.Frame, sample.Displacement);
            PlanarVector3 rotation = ToGlobal(action.Frame, sample.Rotation);
            double[] values = [displacement.X, displacement.Y, displacement.Z, rotation.X, rotation.Y, rotation.Z];
            for (int bit = 0; bit < values.Length; bit++)
            {
                var dof = (PlanarDofMask)(1 << bit);
                if ((action.DofMask & dof) == 0) continue;
                var key = (node.NodeIndex, bit);
                if (prescribed.TryGetValue(key, out double existing) && Math.Abs(existing - values[bit]) > 1e-12)
                    diagnostics.Add(new("planar_boundary_prescribed_dof_conflict", $"DOF {dof} узла {node.NodeIndex} задан с разными значениями."));
                else
                    prescribed[key] = values[bit];
            }
        }
    }

    static void AddPreservedSupport(
        PlanarBoundaryModeByDof modes,
        IReadOnlyList<PlanarCutInterfaceMeshNode> nodes,
        ISet<(int NodeIndex, int Dof)> preserved)
    {
        for (int bit = 0; bit < 6; bit++)
        {
            var dof = (PlanarDofMask)(1 << bit);
            if (modes.Get(dof) != PlanarBoundaryDofMode.PreserveSupport) continue;
            foreach (var node in nodes) preserved.Add((node.NodeIndex, bit));
        }
    }

    static (PlanarVector3 Force, PlanarVector3 Moment, PlanarVector3 Displacement, PlanarVector3 Rotation) Evaluate<T>(
        IReadOnlyList<T> samples,
        double s,
        PlanarBoundaryInterpolationKind interpolation,
        bool force)
    {
        if (samples.Count == 0)
            return (PlanarVector3.Zero, PlanarVector3.Zero, PlanarVector3.Zero, PlanarVector3.Zero);
        if (samples.Count == 1 || interpolation == PlanarBoundaryInterpolationKind.Uniform)
            return ToValues(samples[0]);
        int upper = 1;
        while (upper < samples.Count && GetS(samples[upper]) < s) upper++;
        if (upper >= samples.Count) return ToValues(samples[^1]);
        int lower = upper - 1;
        double denominator = GetS(samples[upper]) - GetS(samples[lower]);
        double t = denominator <= 1e-12 ? 0 : (s - GetS(samples[lower])) / denominator;
        var left = ToValues(samples[lower]);
        var right = ToValues(samples[upper]);
        return (
            left.Force + (right.Force - left.Force) * t,
            left.Moment + (right.Moment - left.Moment) * t,
            left.Displacement + (right.Displacement - left.Displacement) * t,
            left.Rotation + (right.Rotation - left.Rotation) * t);
    }

    static double GetS<T>(T sample) => sample switch
    {
        PlanarBoundaryForceSample force => force.S,
        PlanarBoundaryKinematicSample kinematic => kinematic.S,
        _ => throw new ArgumentException("Неизвестный тип boundary sample.", nameof(sample))
    };

    static (PlanarVector3 Force, PlanarVector3 Moment, PlanarVector3 Displacement, PlanarVector3 Rotation) ToValues<T>(T sample) => sample switch
    {
        PlanarBoundaryForceSample force => (force.ForcePerLength, force.MomentPerLength, PlanarVector3.Zero, PlanarVector3.Zero),
        PlanarBoundaryKinematicSample kinematic => (PlanarVector3.Zero, PlanarVector3.Zero, kinematic.Displacement, kinematic.Rotation),
        _ => throw new ArgumentException("Неизвестный тип boundary sample.", nameof(sample))
    };

    static PlanarVector3 ToGlobal(Frame3D frame, PlanarVector3 vector) =>
        PlanarBoundaryFrameConverter.ToGlobalVector(frame, vector);

    static PlanarVector3 Position(PlanarMeshNode node) => new(node.X, node.Y, node.Z);

    static void AddNodal(
        IDictionary<int, (PlanarVector3 Force, PlanarVector3 Moment)> nodal,
        int nodeIndex,
        PlanarVector3 force,
        PlanarVector3 moment)
    {
        if (nodal.TryGetValue(nodeIndex, out var current))
            nodal[nodeIndex] = (current.Force + force, current.Moment + moment);
        else
            nodal[nodeIndex] = (force, moment);
    }

    static void AddBalanceDiagnostic(
        ICollection<FemValidationDiagnostic> diagnostics,
        string quantity,
        PlanarVector3 expected,
        PlanarVector3 actual,
        double absoluteTolerance,
        double relativeTolerance)
    {
        var delta = actual - expected;
        double scale = Math.Max(1.0, Math.Max(expected.Length, actual.Length));
        if (delta.Length <= absoluteTolerance + relativeTolerance * scale) return;
        diagnostics.Add(new(
            $"planar_boundary_{quantity}_imbalance",
            $"Баланс {quantity} нарушен: ошибка {delta.Length:G17}."));
    }
}
