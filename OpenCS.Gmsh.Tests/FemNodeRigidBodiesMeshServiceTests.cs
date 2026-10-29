using CScore;
using CScore.Fem;
using CScore.Planar;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Gmsh.Tests;

/// <summary>Тела на узлах схемы (АЖТ фрагмента «по точкам») на реальном Gmsh: колонна у стены — ведущий на оси колонны
/// (узел встроен в стержень), ведомые — свободные узлы внутри стены и на её кромке (встроены в сетку стены); тела
/// записаны с происхождением «auto:node_points», повтор их не меняет, проверка видит их актуальными.</summary>
public sealed class FemNodeRigidBodiesMeshServiceTests
{
    const string Gmsh = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe";
    const double Tol = 1e-6;

    [Fact]
    public async Task ColumnAtWall_PointsEmbedded_BodyMaterialized_StableOnRebuild()
    {
        var root = Path.Combine(Path.GetTempPath(), $"opencs-node-rigid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var schema = new FemSchema { Tag = "column-at-wall" };
            db.SaveFemSchema(schema);
            var nodes = new List<FemNode>
            {
                new() { SchemaId = schema.Id, NodeTag = "1", X = 0, Y = 0, Z = 0, DofMask = 63 },
                new() { SchemaId = schema.Id, NodeTag = "2", X = 0, Y = 0, Z = 6 },
                new() { SchemaId = schema.Id, NodeTag = "3", X = 0, Y = 0, Z = 2 },        // на оси колонны
                new() { SchemaId = schema.Id, NodeTag = "4", X = 0, Y = 0.4, Z = 2 },      // в стене
                new() { SchemaId = schema.Id, NodeTag = "5", X = 0.3, Y = 0.4, Z = 2.45 }, // в стене
                new() { SchemaId = schema.Id, NodeTag = "6", X = 1, Y = 0.4, Z = 3 },      // на кромке стены
            };
            var bars = new List<FemMember> { new() { SchemaId = schema.Id, ElemTag = "C1", ElemType = "beam", NodeIdsJson = "[1,2]" } };
            db.SaveFemSchemaEdit(schema.Id, nodes, bars, [], [], []);
            var section = new PlateSection { H = 0.2, Tag = "С200" };
            db.SavePlateSection(section);
            var frame = new Frame3D(new PlanarVector3(-1, 0.4, 0), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 0, 1), new PlanarVector3(0, -1, 0));
            var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 2, 2, 0], Y = [0, 0, 6, 6] }, frame: frame, tag: "W1");
            region.MeshMaxElementSizeM = 0.5;
            db.AddPlanarRegion(region, schema.Id);
            db.SaveFemMember(new FemMember
            {
                SchemaId = schema.Id, ElemTag = "W1", ElemType = "shell", PlanarRegionId = region.Id, PlateSectionId = section.Id, Kind = "wall",
            });
            var body = new FemRigidBody { MasterNodeTag = "3", Origin = "import:scad", SourceElemTag = "125938" };
            body.SetSlaveNodeTags(["4", "5", "6"]);
            db.ReplaceFemNodeRigidBodies(schema.Id, [body]);

            var service = new FemSchemaMeshService(db, new GmshSettings
            {
                ExecutablePath = Gmsh, ArtifactsPath = Path.Combine(root, "gmsh"), KeepArtifacts = false,
                ElementMode = PlanarMeshElementMode.Quads,
            });
            var members = db.GetFemMembers(schema.Id);
            var result = await service.BuildAsync(schema.Id, nodes, members, 0.5, null, CancellationToken.None);
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.DoesNotContain(result.Diagnostics, d => d.Code.StartsWith("node_rigid", StringComparison.Ordinal));
            service.Save(schema.Id, result.Mesh!);

            var mesh = result.Mesh!.Nodes.ToDictionary(n => n.NodeTag);
            var stored = Assert.Single(db.GetFemRigidBodies(schema.Id), b => b.Origin == FemNodeRigidBodies.Provenance);
            Assert.Equal("125938", stored.SourceElemTag);
            var master = mesh[stored.MasterNodeTag];
            Assert.Equal((0.0, 0.0, 2.0), (master.X, master.Y, master.Z));
            // Ведущий — узел стержня колонны.
            Assert.Contains(result.Mesh.Elements, e => e.ElemType == "beam" && e.NodeIdsJson.Contains($"{stored.MasterNodeTag}"));
            var slaves = stored.SlaveNodeTags.Select(t => mesh[t]).OrderBy(n => n.X).ToList();
            Assert.Equal(3, slaves.Count);
            Assert.True(Math.Abs(slaves[0].X) < Tol && Math.Abs(slaves[0].Z - 2) < Tol);
            Assert.True(Math.Abs(slaves[1].X - 0.3) < Tol && Math.Abs(slaves[1].Z - 2.45) < Tol);
            Assert.True(Math.Abs(slaves[2].X - 1) < Tol && Math.Abs(slaves[2].Z - 3) < Tol);
            // Ведомые — узлы КЭ стены.
            var wallNodes = result.Mesh.Elements.Where(e => e.SourceMemberTag == "W1").SelectMany(e => FemMeshTopology.ReadNodeTags(e) ?? []).ToHashSet();
            Assert.All(stored.SlaveNodeTags, t => Assert.Contains(t, wallNodes));

            var check = await service.CheckAsync(schema.Id, nodes, members, 0.5, CancellationToken.None);
            Assert.True(check.IsCurrent, string.Join("\n", check.Diagnostics));
            var again = await service.BuildAsync(schema.Id, nodes, members, 0.5, null, CancellationToken.None);
            Assert.False(service.DiffersFromStored(schema.Id, again.Mesh!));
            service.Save(schema.Id, again.Mesh!);
            Assert.Single(db.GetFemRigidBodies(schema.Id), b => b.Origin == FemNodeRigidBodies.Provenance);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
