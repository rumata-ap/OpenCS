using CScore;
using CScore.Fem;
using CScore.Planar;
using Xunit;

namespace OpenCS.Gmsh.Tests;

public sealed class PlanarConstraintMeshingTests
{
    [Fact]
    public async Task BuildAsync_MapsPointCurveAndConformingRegionOnMsh41()
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 5, 5, 0], Y = [0, 0, 5, 5] },
            [new Contour { X = [2, 3, 3, 2], Y = [2, 2, 3, 3] }]);
        region.ConstraintObjects =
        [
            PlanarConstraintObject.Point("point-1", new(0.5, 0.5),
                new PlanarStructuralFacet(PlanarStructuralKind.PointMpc, new PlanarMasterReference("test", "master")),
                new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint)),
            PlanarConstraintObject.Curve("curve-1", [new(0.5, 1), new(1.5, 1)],
                new PlanarStructuralFacet(PlanarStructuralKind.Tie),
                new PlanarMeshFacet(PlanarMeshKind.EmbeddedCurve)),
            PlanarConstraintObject.Region("region-1", [new(3.5, 0.5), new(4.5, 0.5), new(4.5, 1.5), new(3.5, 1.5)],
                new PlanarStructuralFacet(PlanarStructuralKind.RigidBody, new PlanarMasterReference("test", "rigid")),
                new PlanarMeshFacet(PlanarMeshKind.ConformingPartition))
        ];
        var root = Path.Combine(Path.GetTempPath(), "opencs-gmsh-constraints", Guid.NewGuid().ToString("N"));

        try
        {
            var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
            {
                ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
                ArtifactRoot = root
            });

            var snapshot = await mesher.BuildAsync(new PlanarMeshingRequest(region,
                new PlanarMeshSettings(0.4, 6, PlanarMeshElementMode.Mixed)));

            Assert.True(snapshot.IsCalculable, string.Join(Environment.NewLine, snapshot.Diagnostics));
            Assert.Equal("msh41", snapshot.MeshFormatVersion);
            Assert.Equal(3, snapshot.ConstraintMappings.Count);
            Assert.Single(snapshot.ConstraintMappings, mapping => mapping.ConstraintObjectId == "point-1");
            Assert.NotEmpty(Assert.Single(snapshot.ConstraintMappings, mapping => mapping.ConstraintObjectId == "curve-1").OrderedCurveEdges);
            Assert.NotEmpty(Assert.Single(snapshot.ConstraintMappings, mapping => mapping.ConstraintObjectId == "region-1").RegionElementIndices);
            Assert.NotEmpty(snapshot.EntityProvenance);
            Assert.All(snapshot.Nodes, node => Assert.Equal(0, node.Z, 10));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsync_MeshesDerivedBarPointAndWallIntersectionCurve()
    {
        var region = PlanarRegion.CreateFromContour(new Contour
        {
            X = [0, 5, 5, 0],
            Y = [0, 0, 5, 5]
        });
        region.Id = 77;
        var topology = new FemSchemaTopology(
            1,
            [
                Node(1, 2, 2, -1), Node(2, 2, 2, 1),
                Node(3, 1, 2, -1), Node(4, 4, 2, -1), Node(5, 4, 2, 1), Node(6, 1, 2, 1)
            ],
            [
                HostMember(region.Id),
                new FemMember { Id = 10, SchemaId = 1, ElemTag = "bar", ElemType = "beam", NodeIdsJson = "[1,2]" },
                new FemMember { Id = 20, SchemaId = 1, ElemTag = "wall", ElemType = "shell", NodeIdsJson = "[]" }
            ],
            [new FemElement { Id = 201, SchemaId = 1, ElemTag = "wall-e1", ElemType = "shell", SourceMemberTag = "wall", NodeIdsJson = "[3,4,5,6]" }]);
        var derived = PlanarConstraintDeriver.Derive(topology, region, new());
        Assert.True(derived.IsCalculable, string.Join(Environment.NewLine, derived.Diagnostics));
        Assert.Contains(derived.Constraints, constraint => constraint.Geometry.Kind == PlanarConstraintGeometryKind.Point);
        Assert.Contains(derived.Constraints, constraint => constraint.Geometry.Kind == PlanarConstraintGeometryKind.Curve);
        var root = Path.Combine(Path.GetTempPath(), "opencs-gmsh-derived", Guid.NewGuid().ToString("N"));

        try
        {
            var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
            {
                ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
                ArtifactRoot = root
            });

            var snapshot = await mesher.BuildAsync(new PlanarMeshingRequest(
                region,
                new PlanarMeshSettings(0.4, 6, PlanarMeshElementMode.Mixed),
                derived.Constraints,
                derived.SourceFingerprint));

            Assert.True(snapshot.IsCalculable, string.Join(Environment.NewLine, snapshot.Diagnostics));
            Assert.Contains(snapshot.ConstraintMappings, mapping => mapping.EntityProvenance.Any(entity => entity.PhysicalName.EndsWith(":point", StringComparison.Ordinal)));
            var curveMapping = Assert.Single(snapshot.ConstraintMappings, mapping => mapping.ConstraintObjectId.Contains(":curve:", StringComparison.Ordinal));
            Assert.NotEmpty(curveMapping.OrderedCurveEdges);
            Assert.Equal([201], curveMapping.SourceReferences.Single().ElementIds);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static FemNode Node(int id, double x, double y, double z) => new()
    {
        Id = id, SchemaId = 1, NodeTag = id.ToString(), X = x, Y = y, Z = z
    };

    static FemMember HostMember(int regionId) => new()
    {
        Id = 100, SchemaId = 1, ElemTag = "host", ElemType = "shell", PlanarRegionId = regionId
    };
}
