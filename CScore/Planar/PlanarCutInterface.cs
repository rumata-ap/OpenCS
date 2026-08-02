using CScore.Fem;

namespace CScore.Planar;

/// <summary>Роль геометрического cut interface вертикального фрагмента.</summary>
public enum PlanarCutInterfaceKind
{
    BottomCut,
    TopCut,
    SideCut
}

/// <summary>Геометрический интерфейс между сохраняемым fragment и omitted parent side.</summary>
public sealed class PlanarCutInterface
{
    public string Id { get; init; } = "";
    public PlanarCutInterfaceKind Kind { get; init; }
    public PlanarConstraintGeometry Geometry { get; init; } =
        new(PlanarConstraintGeometryKind.Curve, []);
    public PlanarVector3 NormalFromFragmentToOmittedSide { get; init; }
    public Frame3D Frame { get; init; } = Frame3D.Identity;
    public PlanarBoundaryModeByDof ModeByDof { get; init; } =
        PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.Incomplete);
    public string? MeshConstraintId { get; init; }
    public PlanarBoundaryKey? BoundaryKey { get; init; }
    public string? OmittedSideReference { get; init; }
    public double ToleranceM { get; init; } = 1e-9;

    /// <summary>Проверяет геометрию, frame, нормаль и режимы DOF interface.</summary>
    public IReadOnlyList<FemValidationDiagnostic> Validate()
    {
        var diagnostics = new List<FemValidationDiagnostic>();
        if (string.IsNullOrWhiteSpace(Id))
            diagnostics.Add(new("planar_boundary_interface_id_missing", "У cut interface не задан Id."));
        if (Geometry.Kind != PlanarConstraintGeometryKind.Curve || Geometry.Points.Count < 2)
            diagnostics.Add(new("planar_boundary_interface_curve_invalid", "Cut interface должен содержать открытую curve минимум из двух точек."));
        if (Geometry.Points.Any(point => !point.IsFinite))
            diagnostics.Add(new("planar_boundary_interface_geometry_invalid", "Cut interface содержит нечисловую точку."));
        if (!NormalFromFragmentToOmittedSide.IsFinite || NormalFromFragmentToOmittedSide.Length < 1e-12)
            diagnostics.Add(new("planar_boundary_interface_normal_invalid", "Нормаль cut interface должна быть конечной и ненулевой."));
        if (!double.IsFinite(ToleranceM) || ToleranceM < 0)
            diagnostics.Add(new("planar_boundary_interface_tolerance_invalid", "Допуск cut interface должен быть конечным и неотрицательным."));
        try { Frame.Validate(); }
        catch (InvalidOperationException ex) { diagnostics.Add(new("planar_boundary_interface_frame_invalid", ex.Message)); }
        diagnostics.AddRange(ModeByDof.Validate());
        return diagnostics;
    }

    /// <summary>Создаёт request-local curve constraint для существующего Gmsh pipeline.</summary>
    public PlanarConstraintObject CreateMeshConstraint()
    {
        string id = MeshConstraintId ?? $"cut:{Id}";
        return PlanarConstraintObject.Curve(
            id,
            Geometry.Points,
            new PlanarStructuralFacet(PlanarStructuralKind.None),
            new PlanarMeshFacet(PlanarMeshKind.ConformingPartition),
            $"Cut {Id}");
    }
}
