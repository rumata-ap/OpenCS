using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using CSfea.Core;

namespace CSfea.CScoreBridge;

/// <summary>Результат сборки CSfea.Core.ShellMesh из PlanarMeshSnapshot: сетка + диагностики
/// зон армирования (не проглатываются, прокидываются вызывающему).</summary>
public sealed record PlanarMeshShellMeshResult(
    ShellMesh Mesh,
    IReadOnlyList<(int ElementId, FemValidationDiagnostic Diagnostic)> Diagnostics);

/// <summary>Геометрический адаптер PlanarMeshSnapshot (срез 1 Gmsh-сетки) → CSfea.Core.ShellMesh,
/// поверх уже готового моста PlateRebarField → CSfea (PlateRebarFieldShellResponseFactory).</summary>
public static class PlanarMeshSnapshotShellMeshAdapter
{
    public static PlanarMeshShellMeshResult Build(
        PlanarMeshSnapshot snapshot,
        PlateSection section,
        PlateRebarField field,
        PlateSectionMaterials materials)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        double[][] nodes = snapshot.Nodes
            .Select(n => new[] { n.X, n.Y, n.Z })
            .ToArray();
        int[][] elements = snapshot.Elements
            .Select(e => e.NodeIndices.ToArray())
            .ToArray();
        var centroids = snapshot.Elements
            .Select(e => (e.Index, Centroid: e.Centroid(snapshot.Nodes)))
            .Select(t => (t.Index, t.Centroid.U, t.Centroid.V))
            .ToList();

        PlateRebarFieldShellResponseSet mapped =
            PlateRebarFieldShellResponseFactory.MapMesh(section, field, materials, centroids);
        var order = snapshot.Elements.Select(e => e.Index).ToList();
        var mesh = new ShellMesh(nodes, elements, mapped.ToPerElementArray(order));

        return new PlanarMeshShellMeshResult(mesh, mapped.Diagnostics);
    }
}
