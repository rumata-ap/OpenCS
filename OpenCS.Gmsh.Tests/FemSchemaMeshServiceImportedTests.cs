using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Planar;
using OpenCS.Gmsh.Runtime;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Gmsh.Tests;

/// <summary>Сборка сетки фрагмента с сохранённой (импортной) сеткой родителя на реальном Gmsh: новая стена встраивает
/// узлы сохранённой плиты; несогласованный стык — ошибка с виновным КонЭ.</summary>
public sealed class FemSchemaMeshServiceImportedTests
{
    const string Gmsh = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe";
    const double H = 3.0;

    [Fact]
    public async Task ImportedSlab_WallOnEdge_SharesSlabNodes()
    {
        await WithFragment(wallStep: 1.0, async (db, schema, service) =>
        {
            var result = await service.BuildAsync(schema.Id, db.GetFemNodes(schema.Id), db.GetFemMembers(schema.Id), null, null,
                CancellationToken.None);

            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.Empty(result.ImportedMembersAtFault);
            var mesh = result.Mesh!;
            var wallNodes = mesh.Elements.Where(e => e.SourceMemberTag == "W1").SelectMany(Ids).ToHashSet();
            // Узлы плиты на кромке y = 0 (теги 1001…1005) — узлы стены.
            Assert.All(Enumerable.Range(1001, 5), tag => Assert.Contains(tag, wallNodes));
            var byTag = mesh.Nodes.ToDictionary(n => n.NodeTag);
            // Других узлов на верхней кромке стены нет — висящих узлов на стыке нет.
            Assert.All(wallNodes.Where(t => byTag[t.ToString()].Z > H - 1e-6), t => Assert.InRange(t, 1001, 1005));
            // Сохранённые КЭ плиты — те же теги и узлы.
            var slab = mesh.Elements.Where(e => e.SourceMemberTag == "P1").OrderBy(e => e.ElemTag, StringComparer.Ordinal).ToList();
            Assert.Equal(16, slab.Count);
            Assert.All(slab, e => Assert.Equal(FemMember.MeshSourceImported, e.Origin));
        });
    }

    /// <summary>Стена шагом 0,5 м под кромкой плиты с шагом 1 м: кривая стыка с сохранением вершин — Gmsh не добавляет
    /// узлов между узлами плиты, стык связный.</summary>
    [Fact]
    public async Task ImportedSlab_WallFinerOnEdge_StillConforming()
    {
        await WithFragment(wallStep: 0.5, async (db, schema, service) =>
        {
            var result = await service.BuildAsync(schema.Id, db.GetFemNodes(schema.Id), db.GetFemMembers(schema.Id), null, null,
                CancellationToken.None);

            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.Empty(result.ImportedMembersAtFault);
        });
    }

    /// <summary>Стена под плитой по y = 0,5 — между рядами узлов её сетки: узлы стены внутри рёбер КЭ плиты, связи нет —
    /// ошибка фрагмента, виновный КонЭ — плита.</summary>
    [Fact]
    public async Task ImportedSlab_WallOffSlabMeshLine_StrictError()
    {
        await WithFragment(wallStep: 1.0, wallY: 0.5, async (db, schema, service) =>
        {
            var result = await service.BuildAsync(schema.Id, db.GetFemNodes(schema.Id), db.GetFemMembers(schema.Id), null, null,
                CancellationToken.None);

            Assert.Contains(result.Diagnostics, d => d.Code == "planar_materialize_imported_hanging_node" && d.IsError);
            Assert.Equal(["P1"], result.ImportedMembersAtFault);
        });
    }

    /// <summary>Схема-фрагмент: импортная плита 4×4 м (Q4 1 м) на отметке H — КонЭ «P1» с замком; новая стена «W1»
    /// x 0…4, y = <paramref name="wallY"/>, z 0…H с шагом <paramref name="wallStep"/>.</summary>
    static Task WithFragment(double wallStep, Func<DatabaseService, FemSchema, FemSchemaMeshService, Task> body) =>
        WithFragment(wallStep, 0.0, body);

    static async Task WithFragment(double wallStep, double wallY, Func<DatabaseService, FemSchema, FemSchemaMeshService, Task> body)
    {
        var root = Path.Combine(Path.GetTempPath(), $"opencs-schema-mesh-imported-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var parent = new FemSchema { Tag = "parent" };
            db.SaveFemSchema(parent);
            var schema = new FemSchema { Tag = "fragment", ParentSchemaId = parent.Id };
            db.SaveFemSchema(schema);
            Assert.True(db.GetFemSchemaFragmentSource(schema.Id).IsFragment);

            var nodes = new List<FemMeshNode>();
            var elements = new List<FemElement>();
            int Tag(int i, int j) => 1001 + j * 5 + i;
            for (int j = 0; j <= 4; j++)
                for (int i = 0; i <= 4; i++)
                    nodes.Add(new FemMeshNode
                    {
                        SchemaId = schema.Id, NodeTag = Tag(i, j).ToString(), X = i, Y = j, Z = H,
                        Origin = FemMember.MeshSourceImported, SourceMemberTag = "P1",
                    });
            for (int j = 0; j < 4; j++)
                for (int i = 0; i < 4; i++)
                    elements.Add(new FemElement
                    {
                        SchemaId = schema.Id, ElemTag = (2001 + elements.Count).ToString(), ElemType = "shell",
                        SourceMemberTag = "P1", Origin = FemMember.MeshSourceImported, ThicknessM = 0.2,
                        NodeIdsJson = JsonSerializer.Serialize(new[] { Tag(i, j), Tag(i + 1, j), Tag(i, j + 1), Tag(i + 1, j + 1) }),
                    });
            db.SaveFemMeshSnapshot(schema.Id, nodes, elements);
            db.SaveFemMember(new FemMember
            {
                SchemaId = schema.Id, ElemTag = "P1", ElemType = "shell", NodeIdsJson = "[]", MeshSource = FemMember.MeshSourceImported,
                ThicknessM = 0.2,
            });

            var section = new PlateSection { H = 0.2, Tag = "П200" };
            db.SavePlateSection(section);
            var frame = new Frame3D(new PlanarVector3(0, wallY, 0), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 0, 1), new PlanarVector3(0, -1, 0));
            var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 4, 4, 0], Y = [0, 0, H, H] }, frame: frame, tag: "W1");
            region.MeshMaxElementSizeM = wallStep;
            db.AddPlanarRegion(region, schema.Id);
            db.SaveFemMember(new FemMember
            {
                SchemaId = schema.Id, ElemTag = "W1", ElemType = "shell", PlanarRegionId = region.Id, PlateSectionId = section.Id,
            });
            Assert.True(db.HasImportedMesh(schema.Id));

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
