using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public class PlanarMeshSnapshotValidatorTests
{
    [Fact]
    public void Validate_ReportsDegenerateTriangleAndUnknownNode()
    {
        var snapshot = new PlanarMeshSnapshot
        {
            Nodes =
            [
                new(0, 0, 0, 0, 0, 0),
                new(1, 1, 0, 1, 0, 0),
                new(2, 2, 0, 2, 0, 0)
            ],
            Elements =
            [
                new(0, PlanarMeshElementKind.Triangle3, [0, 1, 2]),
                new(1, PlanarMeshElementKind.Quadrangle4, [0, 1, 2, 99])
            ]
        };

        var diagnostics = PlanarMeshSnapshotValidator.Validate(snapshot);

        Assert.Contains(diagnostics, d => d.Code == "planar_mesh_element_degenerate");
        Assert.Contains(diagnostics, d => d.Code == "planar_mesh_element_unknown_node");
    }

    [Fact]
    public void Validate_ReportsInvalidBoundaryMapping()
    {
        var snapshot = new PlanarMeshSnapshot
        {
            Nodes = [new(0, 0, 0, 0, 0, 0)],
            BoundaryMappings =
            [
                new()
                {
                    Key = new(BoundaryLoop.Outer, 0, 0, 1),
                    NodeIndices = [0, 99]
                },
                new()
                {
                    Key = new(BoundaryLoop.Outer, 0, 0, 1),
                    NodeIndices = [0]
                }
            ]
        };

        var diagnostics = PlanarMeshSnapshotValidator.Validate(snapshot);

        Assert.Contains(diagnostics, d => d.Code == "planar_mesh_boundary_unknown_node");
        Assert.Contains(diagnostics, d => d.Code == "planar_mesh_boundary_node_count");
        Assert.Contains(diagnostics, d => d.Code == "planar_mesh_boundary_duplicate");
    }
}
