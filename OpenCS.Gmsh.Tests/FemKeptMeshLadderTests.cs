using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Planar;
using OpenCS.Gmsh.Runtime;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Gmsh.Tests;

/// <summary>Лестница перемешивания сохранённой сетки фрагмента (срез 4) на реальном Gmsh: виновный КонЭ теряет замок,
/// сетка связна, перенос ГУ и нагрузок пишется вместе с сеткой, повторная проверка — актуальна.</summary>
public sealed class FemKeptMeshLadderTests
{
    const string Gmsh = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe";
    const double H = 3.0;

    /// <summary>Сохранённая балка по кромке сохранённой плиты КЭ по 2 м (узлы плиты через 1 м): шаг 1 снимает замок с
    /// балки, она делится по узлам плиты; плита остаётся с тегами родителя.</summary>
    [Fact]
    public async Task KeptBeamOverKeptSlabNodes_Step1RemeshesBeamOnly()
    {
        await WithFragment(new Options(Beam: true), async (db, schema, service) =>
        {
            var result = await Build(db, schema, service);

            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.Equal(1, result.LadderStep);
            Assert.Equal(["B1"], result.Unlock!.UnlockedMemberTags);
            Assert.Contains(result.Diagnostics, d => d.Code == "fragment_mesh_unlock_step1" && !d.IsError);
            var mesh = result.Mesh!;
            var beam = mesh.Elements.Where(e => e.SourceMemberTag == "B1").ToList();
            Assert.Equal(4, beam.Count);
            Assert.All(beam, e => Assert.Equal(FemMember.MeshSourceGenerated, e.Origin));
            Assert.Equal(Enumerable.Range(1001, 5), beam.SelectMany(Ids).Distinct().Order());
            var slab = mesh.Elements.Where(e => e.SourceMemberTag == "P1").Select(e => e.ElemTag).Order(StringComparer.Ordinal);
            Assert.Equal(Enumerable.Range(2001, 16).Select(t => t.ToString()), slab);

            service.Save(schema.Id, mesh, result.Unlock);

            Assert.Equal(FemMember.MeshSourceGenerated, db.GetFemMembers(schema.Id).Single(m => m.ElemTag == "B1").MeshSource);
            Assert.Equal(16, db.GetFemMeshElements(schema.Id).Count(e => e.Origin == FemMember.MeshSourceImported));
            var check = await service.CheckAsync(schema.Id, db.GetFemNodes(schema.Id), db.GetFemMembers(schema.Id), null, CancellationToken.None);
            Assert.False(check.HasErrors, string.Join("\n", check.Diagnostics));
            Assert.True(check.IsCurrent);
        });
    }

    /// <summary>Стена под сохранённой плитой между рядами её узлов: шаг 1 перестраивает плиту по её области; закрепление
    /// узла плиты переходит в узел схемы, равномерная нагрузка на все КЭ плиты — на КонЭ.</summary>
    [Fact]
    public async Task KeptSlabOffWallLine_Step1RemeshesSlab_TransfersSupportAndLoad()
    {
        await WithFragment(new Options(SlabRegion: true, WallY: 0.5), async (db, schema, service) =>
        {
            db.SetFemMeshNodeBoundary(schema.Id, "1013", 0b111, [0, 0, 0, 0, 0, 0]);
            var lc = new FemLoadCase { Id = -1, Tag = "G" };
            var load = new FemElementLoad { LoadCaseId = -1, LoadKind = FemElementLoadKinds.Uniform, CoordinateSystem = "global", Axis = "z" };
            load.SetTargetTags(Enumerable.Range(2001, 16).Select(t => t.ToString()));
            load.SetValues([-5000]);
            db.SaveFemLoadCasesAndMeshLoads(schema.Id, [lc], [load], []);

            var result = await Build(db, schema, service);

            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.Equal(1, result.LadderStep);
            Assert.Equal(["P1"], result.Unlock!.UnlockedMemberTags);
            var mesh = result.Mesh!;
            Assert.DoesNotContain(mesh.Elements, e => e.Origin == FemMember.MeshSourceImported);
            Assert.Contains(mesh.Nodes, n => Math.Abs(n.X - 2) < 1e-6 && Math.Abs(n.Y - 2) < 1e-6 && Math.Abs(n.Z - H) < 1e-6);

            service.Save(schema.Id, mesh, result.Unlock);

            Assert.False(db.HasImportedMesh(schema.Id));
            Assert.Equal(FemMember.MeshSourceGenerated, db.GetFemMembers(schema.Id).Single(m => m.ElemTag == "P1").MeshSource);
            Assert.DoesNotContain(db.GetFemMeshNodeSupports(schema.Id), s => s.NodeTag == "1013");
            var node = db.GetFemNodes(schema.Id).Single(n => Math.Abs(n.X - 2) < 1e-6 && Math.Abs(n.Y - 2) < 1e-6);
            Assert.Equal(0b111, node.DofMask);
            var lift = Assert.Single(db.GetFemElementLoads(schema.Id));
            Assert.Equal((FemLoadTargetKinds.Members, FemElementLoadKinds.Uniform), (lift.TargetKind, lift.LoadKind));
            Assert.Equal(["P1"], lift.TargetTags);
            Assert.Equal([-5000.0], lift.Values);
            var check = await service.CheckAsync(schema.Id, db.GetFemNodes(schema.Id), db.GetFemMembers(schema.Id), null, CancellationToken.None);
            Assert.False(check.HasErrors, string.Join("\n", check.Diagnostics));
            Assert.True(check.IsCurrent);
        });
    }

    static Task<FemSchemaMeshBuildResult> Build(DatabaseService db, FemSchema schema, FemSchemaMeshService service) =>
        service.BuildAsync(schema.Id, db.GetFemNodes(schema.Id), db.GetFemMembers(schema.Id), null, null, CancellationToken.None);

    /// <param name="Beam">Сохранённая балка B1 по кромке y = 0 двумя КЭ (1001–1003, 1003–1005).</param>
    /// <param name="SlabRegion">У сохранённой плиты P1 есть область (её можно перестроить).</param>
    /// <param name="WallY">Положение новой стены W1 под плитой.</param>
    sealed record Options(bool Beam = false, bool SlabRegion = false, double WallY = 0);

    /// <summary>Схема-фрагмент: сохранённая плита 4×4 м (Q4 1 м, z = H, узлы 1001…1025, КЭ 2001…2016) — КонЭ «P1» с
    /// замком; новая стена «W1» x 0…4, z 0…H шагом 1 м.</summary>
    static async Task WithFragment(Options o, Func<DatabaseService, FemSchema, FemSchemaMeshService, Task> body)
    {
        var root = Path.Combine(Path.GetTempPath(), $"opencs-kept-mesh-ladder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var parent = new FemSchema { Tag = "parent" };
            db.SaveFemSchema(parent);
            var schema = new FemSchema { Tag = "fragment", ParentSchemaId = parent.Id };
            db.SaveFemSchema(schema);

            var meshNodes = new List<FemMeshNode>();
            var elements = new List<FemElement>();
            int Tag(int i, int j) => 1001 + j * 5 + i;
            for (int j = 0; j <= 4; j++)
                for (int i = 0; i <= 4; i++)
                {
                    int tag = Tag(i, j);
                    meshNodes.Add(new FemMeshNode
                    {
                        SchemaId = schema.Id, NodeTag = tag.ToString(), X = i, Y = j, Z = H, Origin = FemMember.MeshSourceImported,
                        SourceMemberTag = "P1", SourceNodeTag = o.Beam && tag is 1001 or 1005 ? tag.ToString() : null,
                    });
                }
            for (int j = 0; j < 4; j++)
                for (int i = 0; i < 4; i++)
                    elements.Add(new FemElement
                    {
                        SchemaId = schema.Id, ElemTag = (2001 + elements.Count).ToString(), ElemType = "shell",
                        SourceMemberTag = "P1", Origin = FemMember.MeshSourceImported, ThicknessM = 0.2,
                        NodeIdsJson = JsonSerializer.Serialize(new[] { Tag(i, j), Tag(i + 1, j), Tag(i, j + 1), Tag(i + 1, j + 1) }),
                    });
            if (o.Beam)
                foreach (var (tag, a, b) in new[] { ("3001", 1001, 1003), ("3002", 1003, 1005) })
                    elements.Add(new FemElement
                    {
                        SchemaId = schema.Id, ElemTag = tag, ElemType = "beam", SourceMemberTag = "B1", Origin = FemMember.MeshSourceImported,
                        NodeIdsJson = JsonSerializer.Serialize(new[] { a, b }),
                    });
            db.SaveFemMeshSnapshot(schema.Id, meshNodes, elements);

            var section = new PlateSection { H = 0.2, Tag = "П200" };
            db.SavePlateSection(section);
            var wallFrame = new Frame3D(new PlanarVector3(0, o.WallY, 0), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 0, 1), new PlanarVector3(0, -1, 0));
            var wall = PlanarRegion.CreateFromContour(new Contour { X = [0, 4, 4, 0], Y = [0, 0, H, H] }, frame: wallFrame, tag: "W1");
            wall.MeshMaxElementSizeM = 1.0;
            db.AddPlanarRegion(wall, schema.Id);
            int? slabRegionId = null;
            if (o.SlabRegion)
            {
                var slabFrame = new Frame3D(new PlanarVector3(0, 0, H), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 1, 0), new PlanarVector3(0, 0, 1));
                var slab = PlanarRegion.CreateFromContour(new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] }, frame: slabFrame, tag: "P1");
                slab.MeshMaxElementSizeM = 1.0;
                db.AddPlanarRegion(slab, schema.Id);
                slabRegionId = slab.Id;
            }

            var nodes = new List<FemNode>();
            var members = new List<FemMember>
            {
                new()
                {
                    SchemaId = schema.Id, ElemTag = "P1", ElemType = "shell", NodeIdsJson = "[]", MeshSource = FemMember.MeshSourceImported,
                    ThicknessM = 0.2, PlateSectionId = section.Id, PlanarRegionId = slabRegionId,
                },
                new() { SchemaId = schema.Id, ElemTag = "W1", ElemType = "shell", PlanarRegionId = wall.Id, PlateSectionId = section.Id },
            };
            if (o.Beam)
            {
                nodes.Add(new FemNode { SchemaId = schema.Id, NodeTag = "1001", X = 0, Y = 0, Z = H });
                nodes.Add(new FemNode { SchemaId = schema.Id, NodeTag = "1005", X = 4, Y = 0, Z = H });
                members.Add(new FemMember
                {
                    SchemaId = schema.Id, ElemTag = "B1", ElemType = "beam", NodeIdsJson = "[1001,1005]", MeshSource = FemMember.MeshSourceImported,
                });
            }
            db.SaveFemSchemaEdit(schema.Id, nodes, members, [], [], []);
            Assert.True(db.HasImportedMesh(schema.Id));
            Assert.True(db.GetFemSchemaFragmentSource(schema.Id).IsFragment);

            var gmsh = new GmshSettings
            {
                ExecutablePath = Gmsh, ArtifactsPath = Path.Combine(root, "gmsh"), KeepArtifacts = false,
                ElementMode = PlanarMeshElementMode.Quads,
            };
            await body(db, schema, new FemSchemaMeshService(db, gmsh));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    static int[] Ids(FemElement e) => JsonSerializer.Deserialize<int[]>(e.NodeIdsJson)!;
}
