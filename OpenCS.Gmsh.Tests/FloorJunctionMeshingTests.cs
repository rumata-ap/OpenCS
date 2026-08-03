using CScore;
using CScore.Fem;
using CScore.Planar;
using OpenCS.Gmsh;
using Xunit;

namespace OpenCS.Gmsh.Tests;

public sealed class FloorJunctionMeshingTests
{
    [Fact]
    public async Task BuildAsync_ConformingPartitionAddsTwoRequestLocalConstraintsAndAcceptsExactPairs()
    {
        var plate = RegionA(10);
        var wall = RegionB(20);
        var connection = Connection(PlanarConnectionMeshMode.ConformingPartition);
        var mesher = new CapturingMesher();

        var result = await new PlanarConnectionMeshingWorkflow(mesher).BuildAsync(
            connection,
            plate,
            new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed),
            wall,
            new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed),
            sourceFingerprintA: "fem-plate",
            sourceFingerprintB: "fem-wall");

        Assert.True(result.IsCalculable, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(2, mesher.Requests.Count);
        Assert.Empty(plate.ConstraintObjects);
        Assert.Empty(wall.ConstraintObjects);
        Assert.Collection(
            mesher.Requests,
            request => AssertRequest(request, 10, "fem-plate"),
            request => AssertRequest(request, 20, "fem-wall"));

        var mapping = Assert.IsType<PlanarConnectionMeshMapping>(result.Mapping);
        Assert.Equal(PlanarConnectionMeshMode.ConformingPartition, mapping.MeshMode);
        Assert.Equal(2, mapping.ExactNodePairs.Count);
        Assert.All(mapping.ExactNodePairs, pair => Assert.True(pair.DistanceM < connection.MatchingToleranceM));
    }

    [Fact]
    public async Task BuildAsync_ConformingPartitionRejectsDifferentNodeCounts()
    {
        var connection = Connection(PlanarConnectionMeshMode.ConformingPartition);
        var mesher = new CapturingMesher { WallNodeCount = 3 };

        var result = await new PlanarConnectionMeshingWorkflow(mesher).BuildAsync(
            connection,
            RegionA(10),
            new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed),
            RegionB(20),
            new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed));

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, item => item.Code == "planar_connection_conforming_partition_mismatch");
    }

    [Fact]
    public async Task BuildAsync_ConformingPartitionRejectsNonMatchingEndpointCoordinates()
    {
        var connection = Connection(PlanarConnectionMeshMode.ConformingPartition);
        var mesher = new CapturingMesher { WallShiftV = 0.01 };

        var result = await new PlanarConnectionMeshingWorkflow(mesher).BuildAsync(
            connection,
            RegionA(10),
            new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed),
            RegionB(20),
            new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed));

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, item => item.Code == "planar_connection_orientation_ambiguous");
    }

    [Fact]
    public async Task BuildAsync_RejectsNonCalculableSnapshot()
    {
        var connection = Connection(PlanarConnectionMeshMode.ConformingPartition);
        var mesher = new CapturingMesher { Calculable = false };

        var result = await new PlanarConnectionMeshingWorkflow(mesher).BuildAsync(
            connection,
            RegionA(10),
            new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed),
            RegionB(20),
            new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed));

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, item => item.Code == "planar_connection_snapshot_not_calculable");
    }

    [Fact]
    public async Task ValidateCurrent_RejectsStaleMappingAfterSnapshotFingerprintChange()
    {
        var connection = Connection(PlanarConnectionMeshMode.ConformingPartition);
        var plate = RegionA(10);
        var wall = RegionB(20);
        var mesher = new CapturingMesher();
        var workflow = new PlanarConnectionMeshingWorkflow(mesher);
        var built = await workflow.BuildAsync(
            connection, plate, Settings(), wall, Settings(),
            sourceFingerprintA: "fem-a", sourceFingerprintB: "fem-b");

        var changedPlate = CopySnapshotWithFingerprint(
            mesher.Snapshots[0], "changed-fingerprint");
        var diagnostics = PlanarConnectionMapper.ValidateCurrent(
            connection, plate, built.Mapping!, changedPlate, wall, mesher.Snapshots[1]);

        Assert.Contains(diagnostics, item => item.Code == "planar_connection_fingerprint_stale");
    }

    [Fact]
    public async Task BuildAsync_RealGmshBuildsTwoConformingSnapshotsForPerpendicularPlateAndWall()
    {
        // Одинаковые PlanarMeshSettings обеих сторон + явные Frame3D — единственный способ
        // получить согласованное разбиение независимых embedded curves (см. Risks #5).
        var plate = RegionA(10);
        var wall = RegionB(20);
        var connection = Connection(PlanarConnectionMeshMode.ConformingPartition);
        var root = Path.Combine(Path.GetTempPath(), "opencs-gmsh-floor-junction", Guid.NewGuid().ToString("N"));

        try
        {
            var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
            {
                ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
                ArtifactRoot = root
            });
            var result = await new PlanarConnectionMeshingWorkflow(mesher).BuildAsync(
                connection,
                plate,
                new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed),
                wall,
                new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed),
                sourceFingerprintA: "fem-plate",
                sourceFingerprintB: "fem-wall");

            Assert.True(result.IsCalculable,
                "ConformingPartition на реальном Gmsh: " +
                string.Join(Environment.NewLine, result.Diagnostics));
            Assert.Equal("msh41", result.SideA!.MeshFormatVersion);
            Assert.Equal("msh41", result.SideB!.MeshFormatVersion);
            Assert.Contains(result.SideA.ConstraintMappings,
                mapping => mapping.ConstraintObjectId == "connection:7:region:10");
            Assert.Contains(result.SideB.ConstraintMappings,
                mapping => mapping.ConstraintObjectId == "connection:7:region:20");

            var mapping = Assert.IsType<PlanarConnectionMeshMapping>(result.Mapping);
            Assert.NotEmpty(mapping.ExactNodePairs);
            // Позиции пар совпадают в глобальном пространстве; ориентация цепочек нормализована.
            Assert.All(mapping.ExactNodePairs, pair =>
            {
                var a = result.SideA!.Nodes.First(n => n.Index == pair.SideANodeIndex);
                var b = result.SideB!.Nodes.First(n => n.Index == pair.SideBNodeIndex);
                double distance = Math.Sqrt(
                    Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));
                Assert.True(distance <= connection.MatchingToleranceM,
                    $"Пара {pair.SideANodeIndex}->{pair.SideBNodeIndex} не совпадает: {distance:G6} м");
            });
            Assert.NotEmpty(result.SideA.EntityProvenance);
            Assert.NotEmpty(result.SideB.EntityProvenance);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static PlanarMeshSnapshot CopySnapshotWithFingerprint(
        PlanarMeshSnapshot source, string inputFingerprint) => new()
    {
        Id = source.Id,
        RegionId = source.RegionId,
        InputFingerprint = inputFingerprint,
        IsCalculable = source.IsCalculable,
        Settings = source.Settings,
        Provenance = source.Provenance,
        Diagnostics = source.Diagnostics,
        Nodes = source.Nodes,
        Elements = source.Elements,
        BoundaryMappings = source.BoundaryMappings,
        MeshFormatVersion = source.MeshFormatVersion,
        EntityProvenance = source.EntityProvenance,
        ConstraintMappings = source.ConstraintMappings
    };

    static void AssertRequest(PlanarMeshingRequest request, int regionId, string sourceFingerprint)
    {
        Assert.Equal(regionId, request.Region.Id);
        var constraint = Assert.Single(request.EffectiveConstraintObjects);
        Assert.Equal($"connection:7:region:{regionId}", constraint.Id);
        Assert.Equal(PlanarConstraintGeometryKind.Curve, constraint.Geometry.Kind);
        Assert.Equal(PlanarMeshKind.ConformingPartition, constraint.MeshFacet.Kind);
        Assert.Equal(sourceFingerprint,
            Assert.IsType<string>(request.ConstraintSourceFingerprint).Split('|')[0]);
    }

    static PlanarMeshSettings Settings() => new(0.5, 6, PlanarMeshElementMode.Mixed);

    static PlanarConnection Connection(PlanarConnectionMeshMode mode) => new()
    {
        Id = 7,
        MeshMode = mode,
        MatchingToleranceM = 1e-8,
        SideA = new ConnectionLocus(10, [new(2, 1), new(2, 3)]),
        SideB = new ConnectionLocus(20, [new(1, 0), new(3, 0)])
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
                new(2, 0, 0), new(0, 1, 0), new(0, 0, 1), new(1, 0, 0)));
        region.Id = id;
        return region;
    }

    sealed class CapturingMesher : IPlanarMesher
    {
        public List<PlanarMeshingRequest> Requests { get; } = [];
        public List<PlanarMeshSnapshot> Snapshots { get; } = [];
        public int WallNodeCount { get; init; } = 2;
        public double WallShiftV { get; init; }
        public bool Calculable { get; init; } = true;

        public Task<PlanarMeshSnapshot> BuildAsync(
            PlanarMeshingRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (!Calculable)
            {
                var failed = new PlanarMeshSnapshot
                {
                    RegionId = request.Region.Id,
                    InputFingerprint = "failed",
                    IsCalculable = false,
                    Diagnostics = [new FemValidationDiagnostic("fake_mesh_error", "Fake mesh failure.")]
                };
                Snapshots.Add(failed);
                return Task.FromResult(failed);
            }

            bool isPlate = request.Region.Id == 10;
            PlanarMeshNode[] nodes;
            if (isPlate)
            {
                nodes =
                [
                    new(0, 0, 0, 0, 0, 0), new(1, 4, 0, 4, 0, 0),
                    new(2, 4, 4, 4, 4, 0), new(3, 0, 4, 0, 4, 0),
                    new(4, 2, 1, 2, 1, 0), new(5, 2, 3, 2, 3, 0)
                ];
            }
            else
            {
                // Wall frame: Origin(2,0,0), LocalX(0,1,0), LocalY(0,0,1), LocalZ(1,0,0)
                // (U,V) -> (2, U, V). Connection line: (U=1..3, V=0) -> global (2,1..3, 0).
                var list = new List<PlanarMeshNode>
                {
                    new(0, 0, 0, 2, 0, 0), new(1, 4, 0, 2, 4, 0),
                    new(2, 4, 3, 2, 4, 3), new(3, 0, 3, 2, 0, 3)
                };
                if (WallNodeCount == 2)
                {
                    list.Add(new(4, 1, WallShiftV, 2, 1, WallShiftV));
                    list.Add(new(5, 3, WallShiftV, 2, 3, WallShiftV));
                }
                else
                {
                    list.Add(new(4, 1, WallShiftV, 2, 1, WallShiftV));
                    list.Add(new(5, 2, WallShiftV, 2, 2, WallShiftV));
                    list.Add(new(6, 3, WallShiftV, 2, 3, WallShiftV));
                }
                nodes = list.ToArray();
            }

            var elements = new List<PlanarMeshElement>
            {
                new(0, PlanarMeshElementKind.Quadrangle4, [0, 4, 5, 3]),
                new(1, PlanarMeshElementKind.Quadrangle4, [4, 1, 2, 5])
            };
            var constraintEdges = isPlate || WallNodeCount == 2
                ? new List<PlanarMeshEdge> { new(4, 5) }
                : new List<PlanarMeshEdge> { new(4, 5), new(5, 6) };

            var snapshot = new PlanarMeshSnapshot
            {
                Id = request.Region.Id + 100,
                RegionId = request.Region.Id,
                InputFingerprint = request.Region.Id == 10 ? "snapshot-a" : "snapshot-b",
                IsCalculable = true,
                Nodes = nodes,
                Elements = elements,
                ConstraintMappings = request.EffectiveConstraintObjects
                    .Select(constraint => new PlanarConstraintMeshMapping
                    {
                        ConstraintObjectId = constraint.Id,
                        OrderedCurveEdges = constraintEdges
                    })
                    .ToList()
            };
            Snapshots.Add(snapshot);
            return Task.FromResult(snapshot);
        }
    }
}
