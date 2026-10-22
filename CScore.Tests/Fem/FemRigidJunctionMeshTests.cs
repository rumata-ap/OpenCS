using CScore.Fem;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Следы АЖТ в сетке плиты: ограничения области и отпечаток (срез 3).</summary>
public sealed class FemRigidJunctionMeshTests
{
    static readonly FemPlanarMaterializationResult Empty = new();

    static readonly Dictionary<int, IReadOnlyList<(double Y, double Z)>> Outlines = new()
    {
        [1] = [(-0.2, -0.2), (0.2, -0.2), (0.2, 0.2), (-0.2, 0.2)],
        [2] = [(-0.15, -0.15), (0.15, -0.15), (0.15, 0.15), (-0.15, 0.15)],
    };

    /// <summary>Плита 6×4 на отметке 3 м.</summary>
    static PlanarRegion Slab()
    {
        var slab = PlanarRegion.CreateFromContour(new Contour { X = [0, 6, 6, 0], Y = [0, 0, 4, 4] },
            frame: new Frame3D(new PlanarVector3(0, 0, 3), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 1, 0), new PlanarVector3(0, 0, 1)));
        slab.Id = 1;
        return slab;
    }

    static FemNode[] Nodes() =>
    [
        new() { NodeTag = "1", X = 3, Y = 2, Z = 0 },
        new() { NodeTag = "2", X = 3, Y = 2, Z = 3 },
    ];

    static FemMember[] Members(int section = 1, int? mask = null) =>
    [
        new() { Id = 1, ElemTag = "П", ElemType = "shell", PlanarRegionId = 1, NodeIdsJson = "[]" },
        new() { Id = 2, ElemTag = "К1", ElemType = "beam", NodeIdsJson = "[1,2]", CrossSectionId = section, RigidJunctionMask = mask },
    ];

    static FemPlanarRegionConstraints Constraints(FemMember[] members, bool junctions = true)
    {
        var region = Slab();
        var traces = junctions ? FemRigidJunctions.Build(region, Nodes(), members, id => Outlines.GetValueOrDefault(id)) : null;
        return FemPlanarMeshPlan.Constraints(1, Nodes(), members, Empty, region, junctions: traces);
    }

    [Fact]
    public void Column_AddsEmbeddedRigidRegion_NodePointKept()
    {
        var result = Constraints(Members());

        var region = Assert.Single(result.Constraints, c => c.Id == "rigid-junction:2#0");
        Assert.Equal(PlanarConstraintGeometryKind.Region, region.Geometry.Kind);
        Assert.Equal(PlanarMeshKind.EmbeddedRegion, region.MeshFacet.Kind);
        Assert.Equal(PlanarStructuralKind.RigidBody, region.StructuralFacet.Kind);
        Assert.Equal(RigidJunctionTraceSet.Provenance, region.Provenance);
        Assert.Equal(1, result.RigidJunctionCount);
        // Точка узла колонны остаётся отдельным ограничением — узел сетки в центре следа.
        Assert.Contains(result.Constraints, c => c.Geometry.Kind == PlanarConstraintGeometryKind.Point &&
                                                 c.Geometry.Points.Any(p => Math.Abs(p.U - 3) < 1e-9 && Math.Abs(p.V - 2) < 1e-9));
    }

    [Fact]
    public void Fingerprint_ChangesWithTraceGeometry_NotWithMask()
    {
        var baseline = Constraints(Members()).SourceFingerprint;

        Assert.NotEqual(Constraints(Members(), junctions: false).SourceFingerprint, baseline);
        Assert.NotEqual(Constraints(Members(section: 2)).SourceFingerprint, baseline);
        Assert.Equal(Constraints(Members(mask: 7)).SourceFingerprint, baseline);
    }
}
