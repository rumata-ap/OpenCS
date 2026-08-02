using CScore.Fem;

namespace CScore.Planar;

/// <summary>Проверяет локусы constraint-объектов до запуска внешнего mesher-а.</summary>
public static class PlanarConstraintValidator
{
    const double GeometryTolerance = 1e-10;

    public static IReadOnlyList<FemValidationDiagnostic> Validate(
        PlanarRegion region,
        IReadOnlyList<PlanarConstraintObject> constraints)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(constraints);

        var diagnostics = new List<FemValidationDiagnostic>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var hull = OpenLoop(region.Hull);
        var holes = region.Holes.Select(OpenLoop).ToArray();

        foreach (var constraint in constraints)
        {
            if (!ids.Add(constraint.Id) || string.IsNullOrWhiteSpace(constraint.Id))
                diagnostics.Add(new("planar_constraint_id_duplicate", $"Constraint ID '{constraint.Id}' пуст или повторяется."));

            ValidateConstraint(constraint, hull, holes, diagnostics);
        }

        for (var i = 0; i < constraints.Count; i++)
        for (var j = i + 1; j < constraints.Count; j++)
        {
            if (HasNonTrivialOverlap(constraints[i].Geometry, constraints[j].Geometry))
                diagnostics.Add(new("planar_constraint_overlap",
                    $"Constraint-объекты '{constraints[i].Id}' и '{constraints[j].Id}' пересекаются или вложены."));
        }

        return diagnostics;
    }

    static void ValidateConstraint(
        PlanarConstraintObject constraint,
        IReadOnlyList<PlanarPoint2D> hull,
        IReadOnlyList<IReadOnlyList<PlanarPoint2D>> holes,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        var points = constraint.Geometry.Points;
        if (points.Any(point => !point.IsFinite))
            diagnostics.Add(new("planar_constraint_coordinate_nonfinite", $"Constraint '{constraint.Id}' содержит нечисловую координату."));

        var requiredCount = constraint.Geometry.Kind switch
        {
            PlanarConstraintGeometryKind.Point => 1,
            PlanarConstraintGeometryKind.Curve => 2,
            PlanarConstraintGeometryKind.Region => 3,
            _ => 0
        };
        if (points.Count < requiredCount)
            diagnostics.Add(new("planar_constraint_geometry_degenerate", $"Constraint '{constraint.Id}' имеет недостаточную геометрию."));

        if (!double.IsFinite(constraint.ToleranceM) || constraint.ToleranceM <= 0)
            diagnostics.Add(new("planar_constraint_tolerance_invalid", $"Допуск constraint '{constraint.Id}' должен быть конечным и положительным."));

        if (!IsCompatible(constraint.Geometry.Kind, constraint.MeshFacet.Kind))
            diagnostics.Add(new("planar_constraint_mesh_facet_incompatible", $"Mesh facet '{constraint.MeshFacet.Kind}' несовместим с геометрией '{constraint.Geometry.Kind}'."));

        ValidateStructuralFacet(constraint, diagnostics);
        if (points.Count < requiredCount || points.Any(point => !point.IsFinite)) return;

        if (constraint.Geometry.Kind == PlanarConstraintGeometryKind.Curve && HasSelfIntersection(points, closed: false) ||
            constraint.Geometry.Kind == PlanarConstraintGeometryKind.Region && HasSelfIntersection(points, closed: true))
            diagnostics.Add(new("planar_constraint_self_intersection", $"Constraint '{constraint.Id}' имеет самопересечение."));

        if (points.Any(point => !IsInsideOrOn(point, hull)))
            diagnostics.Add(new("planar_constraint_outside_host", $"Constraint '{constraint.Id}' выходит за пределы host-региона."));
        if (points.Any(point => holes.Any(hole => IsInside(point, hole))))
            diagnostics.Add(new("planar_constraint_inside_hole", $"Constraint '{constraint.Id}' попадает в отверстие host-региона."));

        if (IntersectsLoop(points, constraint.Geometry.Kind == PlanarConstraintGeometryKind.Region, hull))
            diagnostics.Add(new("planar_constraint_host_boundary_crossing", $"Constraint '{constraint.Id}' пересекает внешнюю границу host-региона."));
        if (holes.Any(hole => IntersectsLoop(points, constraint.Geometry.Kind == PlanarConstraintGeometryKind.Region, hole)))
            diagnostics.Add(new("planar_constraint_hole_crossing", $"Constraint '{constraint.Id}' пересекает границу отверстия."));
    }

    static void ValidateStructuralFacet(PlanarConstraintObject constraint, ICollection<FemValidationDiagnostic> diagnostics)
    {
        var facet = constraint.StructuralFacet;
        if (facet is null)
        {
            diagnostics.Add(new("planar_constraint_structural_facet_missing", $"Constraint '{constraint.Id}' не имеет structural facet."));
            return;
        }

        if (facet.Kind is PlanarStructuralKind.RigidBody or PlanarStructuralKind.PointMpc or PlanarStructuralKind.EmbeddedMember &&
            (facet.MasterReference is null || string.IsNullOrWhiteSpace(facet.MasterReference.Provider) || string.IsNullOrWhiteSpace(facet.MasterReference.Key)))
            diagnostics.Add(new("planar_constraint_master_reference_missing", $"Constraint '{constraint.Id}' требует master reference."));

        if (facet.Kind is PlanarStructuralKind.Support or PlanarStructuralKind.Symmetry && facet.DofMask == PlanarDofMask.None)
            diagnostics.Add(new("planar_constraint_dof_mask_missing", $"Constraint '{constraint.Id}' требует непустую DOF mask."));

        if (facet.Frame is not null)
        {
            try { facet.Frame.Validate(); }
            catch (InvalidOperationException ex)
            {
                diagnostics.Add(new("planar_constraint_structural_frame_invalid", ex.Message));
            }
        }
    }

    static bool IsCompatible(PlanarConstraintGeometryKind geometry, PlanarMeshKind mesh) =>
        mesh == PlanarMeshKind.None ||
        geometry == PlanarConstraintGeometryKind.Point && mesh == PlanarMeshKind.EmbeddedPoint ||
        geometry == PlanarConstraintGeometryKind.Curve && mesh is PlanarMeshKind.EmbeddedCurve or PlanarMeshKind.ConformingPartition ||
        geometry == PlanarConstraintGeometryKind.Region && mesh is PlanarMeshKind.EmbeddedRegion or PlanarMeshKind.ConformingPartition;

    static IReadOnlyList<PlanarPoint2D> OpenLoop(Contour contour)
    {
        var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(contour.X, contour.Y);
        return x.Select((u, index) => new PlanarPoint2D(u, y[index])).ToArray();
    }

    static bool IsInsideOrOn(PlanarPoint2D point, IReadOnlyList<PlanarPoint2D> polygon) =>
        IsOnBoundary(point, polygon) || IsInside(point, polygon);

    static bool IsInside(PlanarPoint2D point, IReadOnlyList<PlanarPoint2D> polygon)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            if ((a.V > point.V) == (b.V > point.V)) continue;
            var intersectionU = (b.U - a.U) * (point.V - a.V) / (b.V - a.V) + a.U;
            if (point.U < intersectionU) inside = !inside;
        }
        return inside;
    }

    static bool IsOnBoundary(PlanarPoint2D point, IReadOnlyList<PlanarPoint2D> polygon) =>
        Enumerable.Range(0, polygon.Count)
            .Any(index => OnSegment(polygon[index], polygon[(index + 1) % polygon.Count], point));

    static bool IntersectsLoop(IReadOnlyList<PlanarPoint2D> points, bool closed, IReadOnlyList<PlanarPoint2D> loop)
    {
        var sourceSegments = Segments(points, closed).ToArray();
        var targetSegments = Segments(loop, closed: true).ToArray();
        return sourceSegments.Any(source => targetSegments.Any(target =>
            SegmentsCross(source.A, source.B, target.A, target.B) &&
            !SamePoint(source.A, target.A) && !SamePoint(source.A, target.B) &&
            !SamePoint(source.B, target.A) && !SamePoint(source.B, target.B)));
    }

    static bool HasSelfIntersection(IReadOnlyList<PlanarPoint2D> points, bool closed)
    {
        var segments = Segments(points, closed).ToArray();
        for (var i = 0; i < segments.Length; i++)
        for (var j = i + 1; j < segments.Length; j++)
        {
            if (Math.Abs(i - j) <= 1 || closed && i == 0 && j == segments.Length - 1) continue;
            if (SegmentsCross(segments[i].A, segments[i].B, segments[j].A, segments[j].B)) return true;
        }
        return false;
    }

    static bool HasNonTrivialOverlap(PlanarConstraintGeometry first, PlanarConstraintGeometry second)
    {
        if (first.Kind == PlanarConstraintGeometryKind.Point || second.Kind == PlanarConstraintGeometryKind.Point)
            return false;

        var firstSegments = Segments(first.Points, first.Kind == PlanarConstraintGeometryKind.Region).ToArray();
        var secondSegments = Segments(second.Points, second.Kind == PlanarConstraintGeometryKind.Region).ToArray();
        if (firstSegments.Any(a => secondSegments.Any(b => SegmentsCross(a.A, a.B, b.A, b.B)))) return true;
        if (first.Kind == PlanarConstraintGeometryKind.Region && IsInside(second.Points[0], first.Points)) return true;
        if (second.Kind == PlanarConstraintGeometryKind.Region && IsInside(first.Points[0], second.Points)) return true;
        return false;
    }

    static IEnumerable<(PlanarPoint2D A, PlanarPoint2D B)> Segments(IReadOnlyList<PlanarPoint2D> points, bool closed)
    {
        var count = closed ? points.Count : points.Count - 1;
        for (var i = 0; i < count; i++)
            yield return (points[i], points[(i + 1) % points.Count]);
    }

    static bool SegmentsCross(PlanarPoint2D a, PlanarPoint2D b, PlanarPoint2D c, PlanarPoint2D d)
    {
        var abC = Orientation(a, b, c);
        var abD = Orientation(a, b, d);
        var cdA = Orientation(c, d, a);
        var cdB = Orientation(c, d, b);
        if (Math.Abs(abC) <= GeometryTolerance && OnSegment(a, b, c) ||
            Math.Abs(abD) <= GeometryTolerance && OnSegment(a, b, d) ||
            Math.Abs(cdA) <= GeometryTolerance && OnSegment(c, d, a) ||
            Math.Abs(cdB) <= GeometryTolerance && OnSegment(c, d, b)) return true;
        return abC * abD < 0 && cdA * cdB < 0;
    }

    static double Orientation(PlanarPoint2D a, PlanarPoint2D b, PlanarPoint2D c) =>
        (b.U - a.U) * (c.V - a.V) - (b.V - a.V) * (c.U - a.U);

    static bool OnSegment(PlanarPoint2D a, PlanarPoint2D b, PlanarPoint2D point) =>
        Math.Abs(Orientation(a, b, point)) <= GeometryTolerance &&
        point.U >= Math.Min(a.U, b.U) - GeometryTolerance &&
        point.U <= Math.Max(a.U, b.U) + GeometryTolerance &&
        point.V >= Math.Min(a.V, b.V) - GeometryTolerance &&
        point.V <= Math.Max(a.V, b.V) + GeometryTolerance;

    static bool SamePoint(PlanarPoint2D a, PlanarPoint2D b) =>
        Math.Abs(a.U - b.U) <= GeometryTolerance && Math.Abs(a.V - b.V) <= GeometryTolerance;
}
