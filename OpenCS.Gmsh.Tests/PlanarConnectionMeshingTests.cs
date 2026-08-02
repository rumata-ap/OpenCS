using CScore;
using CScore.Fem;
using CScore.Planar;
using OpenCS.Gmsh;
using Xunit;

namespace OpenCS.Gmsh.Tests;

public sealed class PlanarConnectionMeshingTests
{
    [Fact]
    public async Task BuildAsync_CreatesTwoRequestLocalConnectionConstraintsWithoutMutatingRegions()
    {
        var sideA = RegionA(10);
        var sideB = RegionB(20);
        var connection = Connection(PlanarConnectionMeshMode.IndependentMpc);
        var mesher = new CapturingMesher();

        var result = await new PlanarConnectionMeshingWorkflow(mesher).BuildAsync(
            connection,
            sideA,
            new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed),
            sideB,
            new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed),
            sourceFingerprintA: "fem-a",
            sourceFingerprintB: "fem-b");

        Assert.True(result.IsCalculable, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(2, mesher.Requests.Count);
        Assert.Empty(sideA.ConstraintObjects);
        Assert.Empty(sideB.ConstraintObjects);
        Assert.Collection(
            mesher.Requests,
            request => AssertRequest(request, 10, PlanarMeshKind.EmbeddedCurve, "fem-a"),
            request => AssertRequest(request, 20, PlanarMeshKind.EmbeddedCurve, "fem-b"));
    }

    [Fact]
    public async Task BuildAsync_RejectsInvalidConnectionBeforeCallingMesher()
    {
        var sideA = RegionA(10);
        var sideB = RegionB(20);
        var connection = Connection(PlanarConnectionMeshMode.EmbeddedLocus);
        connection.Id = 0;
        connection.SideB = new ConnectionLocus(10, connection.SideA.Points);
        var mesher = new CapturingMesher();

        var result = await new PlanarConnectionMeshingWorkflow(mesher).BuildAsync(
            connection,
            sideA,
            new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed),
            sideB,
            new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed));

        Assert.False(result.IsCalculable);
        Assert.Empty(mesher.Requests);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "planar_connection_id_invalid");
    }

    static void AssertRequest(
        PlanarMeshingRequest request,
        int regionId,
        PlanarMeshKind meshKind,
        string sourceFingerprint)
    {
        Assert.Equal(regionId, request.Region.Id);
        var constraint = Assert.Single(request.EffectiveConstraintObjects);
        Assert.Equal($"connection:7:region:{regionId}", constraint.Id);
        Assert.Equal(PlanarConstraintGeometryKind.Curve, constraint.Geometry.Kind);
        Assert.Equal(meshKind, constraint.MeshFacet.Kind);
        Assert.Equal(sourceFingerprint, Assert.IsType<string>(request.ConstraintSourceFingerprint).Split('|')[0]);
    }

    static PlanarConnection Connection(PlanarConnectionMeshMode mode) => new()
    {
        Id = 7,
        MeshMode = mode,
        SideA = new ConnectionLocus(10, [new(2, 1), new(2, 3)]),
        SideB = new ConnectionLocus(20, [new(3, 0), new(1, 0)])
    };

    static PlanarRegion RegionA(int id)
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] });
        region.Id = id;
        return region;
    }

    static PlanarRegion RegionB(int id)
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 4, 4, 0], Y = [-1, -1, 1, 1] },
            frame: new Frame3D(
                new(2, 0, 0),
                new(0, 1, 0),
                new(0, 0, 1),
                new(1, 0, 0)));
        region.Id = id;
        return region;
    }

    sealed class CapturingMesher : IPlanarMesher
    {
        public List<PlanarMeshingRequest> Requests { get; } = [];

        public Task<PlanarMeshSnapshot> BuildAsync(
            PlanarMeshingRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var constraint = Assert.Single(request.EffectiveConstraintObjects);
            var isA = request.Region.Id == 10;
            var nodes = isA
                ? new[]
                {
                    new PlanarMeshNode(0, 2, 1, 2, 1, 0),
                    new PlanarMeshNode(1, 2, 3, 2, 3, 0)
                }
                : new[]
                {
                    new PlanarMeshNode(0, 3, 0, 2, 3, 0),
                    new PlanarMeshNode(1, 1, 0, 2, 1, 0)
                };
            return Task.FromResult(new PlanarMeshSnapshot
            {
                Id = request.Region.Id + 100,
                RegionId = request.Region.Id,
                InputFingerprint = request.Region.Id == 10 ? "snapshot-a" : "snapshot-b",
                IsCalculable = true,
                Nodes = nodes,
                ConstraintMappings =
                [
                    new()
                    {
                        ConstraintObjectId = constraint.Id,
                        OrderedCurveEdges = [new(0, 1)]
                    }
                ]
            });
        }
    }
}
