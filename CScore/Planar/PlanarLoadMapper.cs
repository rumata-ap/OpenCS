using CScore.Fem;

namespace CScore.Planar;

/// <summary>Дискретизирует PlanarLoad по узлам и рёбрам PlanarMeshSnapshot.</summary>
public static class PlanarLoadMapper
{
    public static PlanarLoadMappingResult Map(
        Frame3D frame,
        PlanarMeshSnapshot snapshot,
        IEnumerable<PlanarLoad> loads,
        PlanarLoadMappingOptions? options = null)
        => MapCore(frame, snapshot, loads, new(false, [], []), options);

    public static PlanarLoadMappingResult Map(
        PlanarRegion region,
        PlanarMeshSnapshot snapshot,
        IEnumerable<PlanarLoad> loads,
        PlanarLoadMappingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(region);
        var boundary = PlanarBoundaryContractMapper.Map(region, snapshot);
        return MapCore(region.Frame, snapshot, loads, boundary, options);
    }

    static PlanarLoadMappingResult MapCore(
        Frame3D frame,
        PlanarMeshSnapshot snapshot,
        IEnumerable<PlanarLoad> loads,
        PlanarBoundaryContractResult boundary,
        PlanarLoadMappingOptions? options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(loads);
        frame.Validate();
        options ??= new PlanarLoadMappingOptions();
        options.Validate();

        var diagnostics = new List<FemValidationDiagnostic>();
        diagnostics.AddRange(boundary.Diagnostics);
        var snapshotDiagnostics = PlanarMeshSnapshotValidator.Validate(snapshot);
        diagnostics.AddRange(snapshotDiagnostics);

        var nodeByIndex = snapshot.Nodes.ToDictionary(node => node.Index);
        var mappings = snapshot.BoundaryMappings
            .GroupBy(mapping => mapping.Key)
            .ToDictionary(group => group.Key, group => group.First());
        var nodal = new Dictionary<int, PlanarVector3>();
        var appliedForce = PlanarVector3.Zero;
        var appliedMoment = PlanarVector3.Zero;

        foreach (PlanarLoad load in loads)
        {
            try
            {
                load.Validate();
            }
            catch (ArgumentException ex)
            {
                diagnostics.Add(new("planar_load_invalid", ex.Message));
                continue;
            }

            PlanarVector3 vector = ToGlobalVector(frame, load.Components, load.CoordinateSystem);
            switch (load.Kind)
            {
                case PlanarLoadKind.Surface:
                    MapSurface(snapshot, frame, vector, nodal, ref appliedForce, ref appliedMoment, diagnostics);
                    break;
                case PlanarLoadKind.Boundary:
                    MapBoundary(load, mappings, nodeByIndex, vector, nodal, ref appliedForce, ref appliedMoment, diagnostics);
                    break;
                case PlanarLoadKind.Point:
                    MapPoint(load, frame, snapshot.Nodes, vector, nodal, ref appliedForce, ref appliedMoment, diagnostics);
                    break;
            }
        }

        PlanarVector3 mappedForce = PlanarVector3.Zero;
        PlanarVector3 mappedMoment = PlanarVector3.Zero;
        foreach ((int index, PlanarVector3 force) in nodal)
        {
            mappedForce += force;
            if (nodeByIndex.TryGetValue(index, out PlanarMeshNode? node))
                mappedMoment += new PlanarVector3(node.X, node.Y, node.Z).Cross(force);
        }

        AddBalanceDiagnostic(
            diagnostics, "force", appliedForce, mappedForce,
            options.ForceAbsoluteTolerance, options.RelativeTolerance);
        AddBalanceDiagnostic(
            diagnostics, "moment", appliedMoment, mappedMoment,
            options.MomentAbsoluteTolerance, options.RelativeTolerance);

        return new(
            diagnostics.All(diagnostic => !diagnostic.IsError),
            diagnostics,
            nodal,
            boundary.Sets,
            appliedForce,
            appliedMoment,
            mappedForce,
            mappedMoment);
    }

    static void MapSurface(
        PlanarMeshSnapshot snapshot,
        Frame3D frame,
        PlanarVector3 traction,
        Dictionary<int, PlanarVector3> nodal,
        ref PlanarVector3 appliedForce,
        ref PlanarVector3 appliedMoment,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        foreach (PlanarMeshElement element in snapshot.Elements)
        {
            if (element.NodeIndices.Any(index => !snapshot.Nodes.Any(node => node.Index == index)))
                continue;
            var nodes = element.NodeIndices.Select(index => snapshot.Nodes.First(node => node.Index == index)).ToArray();
            if (element.Kind == PlanarMeshElementKind.Triangle3)
            {
                double area = LocalTriangleArea(nodes);
                if (area <= 1e-12)
                {
                    diagnostics.Add(new("planar_load_element_degenerate", $"Элемент {element.Index} не имеет площади для surface load."));
                    continue;
                }
                PlanarVector3 force = traction * area;
                appliedForce += force;
                appliedMoment += Centroid(nodes).Cross(force);
                PlanarVector3 nodalForce = force * (1.0 / 3.0);
                foreach (PlanarMeshNode node in nodes) Add(nodal, node.Index, nodalForce);
                continue;
            }

            IntegrateQuadrangle(nodes, traction, nodal, ref appliedForce, ref appliedMoment, diagnostics);
        }
    }

    static void IntegrateQuadrangle(
        IReadOnlyList<PlanarMeshNode> nodes,
        PlanarVector3 traction,
        Dictionary<int, PlanarVector3> nodal,
        ref PlanarVector3 appliedForce,
        ref PlanarVector3 appliedMoment,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        double g = 1.0 / Math.Sqrt(3.0);
        foreach (double xi in new[] { -g, g })
            foreach (double eta in new[] { -g, g })
            {
                double[] shape =
                [
                    0.25 * (1 - xi) * (1 - eta),
                    0.25 * (1 + xi) * (1 - eta),
                    0.25 * (1 + xi) * (1 + eta),
                    0.25 * (1 - xi) * (1 + eta),
                ];
                double[] dXi =
                [
                    -0.25 * (1 - eta),
                    0.25 * (1 - eta),
                    0.25 * (1 + eta),
                    -0.25 * (1 + eta),
                ];
                double[] dEta =
                [
                    -0.25 * (1 - xi),
                    -0.25 * (1 + xi),
                    0.25 * (1 + xi),
                    0.25 * (1 - xi),
                ];
                double duXi = 0, duEta = 0, dvXi = 0, dvEta = 0;
                var point = PlanarVector3.Zero;
                for (int i = 0; i < 4; i++)
                {
                    duXi += dXi[i] * nodes[i].U;
                    duEta += dEta[i] * nodes[i].U;
                    dvXi += dXi[i] * nodes[i].V;
                    dvEta += dEta[i] * nodes[i].V;
                    point += new PlanarVector3(nodes[i].X, nodes[i].Y, nodes[i].Z) * shape[i];
                }
                double determinant = duXi * dvEta - duEta * dvXi;
                if (determinant <= 1e-12)
                {
                    diagnostics.Add(new("planar_load_element_degenerate", "Q4 имеет некорректный Jacobian для surface load."));
                    continue;
                }

                PlanarVector3 force = traction * determinant;
                appliedForce += force;
                appliedMoment += point.Cross(force);
                for (int i = 0; i < 4; i++)
                    Add(nodal, nodes[i].Index, force * shape[i]);
            }
    }

    static void MapBoundary(
        PlanarLoad load,
        IReadOnlyDictionary<PlanarBoundaryKey, PlanarMeshBoundaryMapping> mappings,
        IReadOnlyDictionary<int, PlanarMeshNode> nodeByIndex,
        PlanarVector3 traction,
        Dictionary<int, PlanarVector3> nodal,
        ref PlanarVector3 appliedForce,
        ref PlanarVector3 appliedMoment,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        PlanarBoundaryKey key = load.BoundaryKey!;
        if (!mappings.TryGetValue(key, out PlanarMeshBoundaryMapping? mapping))
        {
            diagnostics.Add(new("planar_load_boundary_mapping_missing", $"Краевая нагрузка «{load.Tag}» ссылается на отсутствующий mapping {Format(key)}."));
            return;
        }

        for (int i = 1; i < mapping.NodeIndices.Count; i++)
        {
            if (!nodeByIndex.TryGetValue(mapping.NodeIndices[i - 1], out PlanarMeshNode? a) ||
                !nodeByIndex.TryGetValue(mapping.NodeIndices[i], out PlanarMeshNode? b))
            {
                diagnostics.Add(new("planar_load_boundary_unknown_node", $"Краевая нагрузка «{load.Tag}» ссылается на неизвестный узел."));
                continue;
            }

            var pa = new PlanarVector3(a.X, a.Y, a.Z);
            var pb = new PlanarVector3(b.X, b.Y, b.Z);
            double length = (pb - pa).Length;
            if (length <= 1e-12)
            {
                diagnostics.Add(new("planar_load_boundary_degenerate_edge", $"Краевая нагрузка «{load.Tag}» содержит нулевое ребро."));
                continue;
            }
            PlanarVector3 force = traction * length;
            appliedForce += force;
            appliedMoment += ((pa + pb) * 0.5).Cross(force);
            Add(nodal, a.Index, force * 0.5);
            Add(nodal, b.Index, force * 0.5);
        }
    }

    static void MapPoint(
        PlanarLoad load,
        Frame3D frame,
        IReadOnlyList<PlanarMeshNode> nodes,
        PlanarVector3 force,
        Dictionary<int, PlanarVector3> nodal,
        ref PlanarVector3 appliedForce,
        ref PlanarVector3 appliedMoment,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        PlanarVector3 point = frame.Origin + frame.LocalX * load.PointU + frame.LocalY * load.PointV;
        double toleranceSquared = load.PointToleranceM * load.PointToleranceM;
        var matches = nodes.Where(node =>
        {
            var delta = new PlanarVector3(node.X, node.Y, node.Z) - point;
            return delta.LengthSquared <= toleranceSquared;
        }).ToArray();

        if (matches.Length == 0)
        {
            diagnostics.Add(new("planar_load_point_node_missing", $"Точка нагрузки «{load.Tag}» не совпадает ни с одним узлом snapshot."));
            return;
        }
        if (matches.Length > 1)
        {
            diagnostics.Add(new("planar_load_point_node_ambiguous", $"Точка нагрузки «{load.Tag}» совпадает с несколькими узлами snapshot."));
            return;
        }

        appliedForce += force;
        appliedMoment += point.Cross(force);
        Add(nodal, matches[0].Index, force);
    }

    static PlanarVector3 ToGlobalVector(Frame3D frame, PlanarVector3 vector, PlanarLoadCoordinateSystem system) =>
        system == PlanarLoadCoordinateSystem.Global
            ? vector
            : frame.LocalX * vector.X + frame.LocalY * vector.Y + frame.LocalZ * vector.Z;

    static double LocalTriangleArea(IReadOnlyList<PlanarMeshNode> nodes)
    {
        double du1 = nodes[1].U - nodes[0].U;
        double dv1 = nodes[1].V - nodes[0].V;
        double du2 = nodes[2].U - nodes[0].U;
        double dv2 = nodes[2].V - nodes[0].V;
        return Math.Abs(du1 * dv2 - dv1 * du2) / 2.0;
    }

    static PlanarVector3 Centroid(IReadOnlyList<PlanarMeshNode> nodes)
    {
        var result = PlanarVector3.Zero;
        foreach (PlanarMeshNode node in nodes)
            result += new PlanarVector3(node.X, node.Y, node.Z);
        return result * (1.0 / nodes.Count);
    }

    static void Add(Dictionary<int, PlanarVector3> values, int index, PlanarVector3 value)
    {
        values[index] = values.TryGetValue(index, out PlanarVector3 current) ? current + value : value;
    }

    static void AddBalanceDiagnostic(
        ICollection<FemValidationDiagnostic> diagnostics,
        string quantity,
        PlanarVector3 reference,
        PlanarVector3 actual,
        double absoluteTolerance,
        double relativeTolerance)
    {
        var delta = actual - reference;
        double scale = Math.Max(1.0, Math.Max(reference.Length, actual.Length));
        if (delta.Length <= absoluteTolerance + relativeTolerance * scale)
            return;
        diagnostics.Add(new(
            $"planar_load_{quantity}_imbalance",
            $"Баланс {quantity} нарушен: ошибка {delta.Length:G17}, допуск {absoluteTolerance + relativeTolerance * scale:G17}."));
    }

    static string Format(PlanarBoundaryKey key) =>
        $"{key.Loop}:{key.HoleIndex}:{key.StartVertex}-{key.EndVertex}";
}
