using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public sealed class PlanarConstraintMeshMappingTests
{
    [Fact]
    public void Validate_RejectsDuplicateConstraintAndUnknownMappedIndices()
    {
        var snapshot = Snapshot(
            mappings:
            [
                new()
                {
                    ConstraintObjectId = "constraint-1",
                    PointNodeIndices = [0, 0],
                    OrderedCurveEdges = [new(0, 99)],
                    RegionElementIndices = [42]
                },
                new() { ConstraintObjectId = "constraint-1" }
            ]);

        var diagnostics = PlanarMeshSnapshotValidator.Validate(snapshot);

        Assert.Contains(diagnostics, d => d.Code == "planar_mesh_constraint_duplicate");
        Assert.Contains(diagnostics, d => d.Code == "planar_mesh_constraint_point_cardinality");
        Assert.Contains(diagnostics, d => d.Code == "planar_mesh_constraint_edge_unknown_node");
        Assert.Contains(diagnostics, d => d.Code == "planar_mesh_constraint_element_unknown");
    }

    [Fact]
    public void Validate_AcceptsCompletePointCurveAndRegionMapping()
    {
        var snapshot = Snapshot(
            mappings:
            [
                new()
                {
                    ConstraintObjectId = "constraint-1",
                    PointNodeIndices = [0],
                    OrderedCurveEdges = [new(0, 1)],
                    CurveElementIndices = [0],
                    RegionNodeIndices = [0, 1, 2],
                    RegionElementIndices = [0],
                    EntityProvenance = [new("constraint-1", 2, 17, 3001, "constraint:constraint-1:region")]
                }
            ]);

        var diagnostics = PlanarMeshSnapshotValidator.Validate(snapshot);

        Assert.DoesNotContain(diagnostics, d => d.IsError);
    }

    static PlanarMeshSnapshot Snapshot(IReadOnlyList<PlanarConstraintMeshMapping> mappings) => new()
    {
        Nodes =
        [
            new(0, 0, 0, 0, 0, 0),
            new(1, 1, 0, 1, 0, 0),
            new(2, 0, 1, 0, 1, 0)
        ],
        Elements = [new(0, PlanarMeshElementKind.Triangle3, [0, 1, 2])],
        ConstraintMappings = mappings
    };
}
