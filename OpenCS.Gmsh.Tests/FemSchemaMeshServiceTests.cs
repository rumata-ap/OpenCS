using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Planar;
using OpenCS.Gmsh.Runtime;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Gmsh.Tests;

/// <summary>«Построить сетку схемы» на реальном Gmsh (CSfea 4г, срез 3): плита 4×4 м на четырёх колоннах и балка по
/// кромке y = 0 — общие узлы, дробление балки, переиспользование снимка, свободный узел на плите.</summary>
public sealed class FemSchemaMeshServiceTests
{
    const string Gmsh = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe";
    const double H = 3.0;

    [Fact]
    public async Task SlabOnColumns_BuildReuseAndFreeNode()
    {
        var root = Path.Combine(Path.GetTempPath(), $"opencs-schema-mesh-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "test.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "slab" };
            db.SaveFemSchema(schema);
            var nodes = new List<FemNode>
            {
                Node(schema, "1", 0, 0, 0), Node(schema, "2", 4, 0, 0), Node(schema, "3", 4, 4, 0), Node(schema, "4", 0, 4, 0),
                Node(schema, "5", 0, 0, H), Node(schema, "6", 4, 0, H), Node(schema, "7", 4, 4, H), Node(schema, "8", 0, 4, H),
            };
            var members = new List<FemMember>
            {
                Bar(schema, "C1", 1, 5), Bar(schema, "C2", 2, 6), Bar(schema, "C3", 3, 7), Bar(schema, "C4", 4, 8), Bar(schema, "B1", 5, 6),
            };
            db.SaveFemSchemaEdit(schema.Id, nodes, members, [], [], []);
            var section = new PlateSection { H = 0.2, Tag = "П200" };
            db.SavePlateSection(section);
            var frame = new Frame3D(new PlanarVector3(0, 0, H), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 1, 0), new PlanarVector3(0, 0, 1));
            var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] }, frame: frame, tag: "P1");
            region.MeshMaxElementSizeM = 1.0;
            db.AddPlanarRegion(region, schema.Id);
            var plate = new FemMember
            {
                SchemaId = schema.Id, ElemTag = "P1", ElemType = "shell", PlanarRegionId = region.Id, PlateSectionId = section.Id,
            };
            db.SaveFemMember(plate);

            var gmsh = new GmshSettings
            {
                ExecutablePath = Gmsh, ArtifactsPath = Path.Combine(root, "gmsh"), KeepArtifacts = false,
                ElementMode = PlanarMeshElementMode.Quads,
            };
            var service = new FemSchemaMeshService(db, gmsh);

            // 1. Первая сборка: Gmsh, общие узлы колонн, балка по кромке разделена.
            var first = await Build();
            Assert.False(first.HasErrors, string.Join("\n", first.Diagnostics));
            Assert.Equal(1, first.RebuiltRegionCount);
            var mesh = first.Mesh!;
            var shells = mesh.Elements.Where(e => e.ElemType == "shell").ToList();
            Assert.NotEmpty(shells);
            Assert.All(shells, s => Assert.Equal(0.2, s.ThicknessM));
            var shellNodes = shells.SelectMany(Ids).ToHashSet();
            Assert.All(new[] { 5, 6, 7, 8 }, tag => Assert.Contains(tag, shellNodes));
            var edge = mesh.Elements.Where(e => e.SourceMemberTag == "B1").ToList();
            Assert.True(edge.Count >= 4, $"балка по кромке: {edge.Count} КЭ");
            Assert.All(edge.SelectMany(Ids), tag => Assert.Contains(tag, shellNodes));
            service.Save(schema.Id, mesh);

            // 2. Повтор без изменений: снимок переиспользован, сетка та же, проверка — актуальна.
            var second = await Build();
            Assert.Equal(0, second.RebuiltRegionCount);
            Assert.True(service.IsSameAsStored(schema.Id, second.Mesh!));
            var check = await service.CheckAsync(schema.Id, nodes, db.GetFemMembers(schema.Id), null, CancellationToken.None);
            Assert.True(check.IsCurrent);

            // Диалог области видит тот же вход: отпечаток сохранённого снимка совпадает.
            var constraints = service.RegionConstraints(schema.Id, region);
            var version = await GmshProcessRunner.ReadVersionAsync(Gmsh, TimeSpan.FromSeconds(10), CancellationToken.None);
            var fingerprint = PlanarMeshFingerprint.Compute(region,
                new PlanarMeshSettings(1.0, gmsh.Algorithm, gmsh.ElementMode),
                new PlanarMeshProvenance(version, GmshPlanarMesher.GeneratorVersion), constraints.SourceFingerprint);
            Assert.Equal(fingerprint, db.GetPlanarMeshSnapshots(region.Id).Last().InputFingerprint);

            // 3. Свободный узел на плите (опора/сила): сетка устарела, после сборки узел пластины ссылается на него.
            nodes.Add(Node(schema, "9", 1.5, 2.5, H));
            db.SaveFemSchemaEdit(schema.Id, nodes, db.GetFemMembers(schema.Id), [], [], []);
            var stale = await service.CheckAsync(schema.Id, nodes, db.GetFemMembers(schema.Id), null, CancellationToken.None);
            Assert.False(stale.IsCurrent);
            var third = await Build();
            Assert.False(third.HasErrors, string.Join("\n", third.Diagnostics));
            Assert.Equal(1, third.RebuiltRegionCount);
            var free = Assert.Single(third.Mesh!.Nodes, n => n.SourceNodeTag == "9");
            Assert.Contains(int.Parse(free.NodeTag), third.Mesh.Elements.Where(e => e.ElemType == "shell").SelectMany(Ids));

            Task<FemSchemaMeshBuildResult> Build() =>
                service.BuildAsync(schema.Id, nodes, db.GetFemMembers(schema.Id), null, null, CancellationToken.None);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    /// <summary>Стыки областей (срез 4): обе стороны стыка без висячих узлов при любом порядке построения.</summary>
    [Theory]
    [InlineData("wall-under-slab-edge", 0.3, 0.5)]
    [InlineData("wall-under-slab-edge", 0.7, 0.5)]
    [InlineData("wall-through-slab", 0.4, 0.5)]
    [InlineData("wall-through-slab", 0.6, 0.5)]
    [InlineData("wall-under-slab-middle", 0.35, 0.5)]
    [InlineData("corner-walls", 0.4, 0.5)]
    [InlineData("adjacent-slabs", 0.3, 0.5)]
    public async Task Junctions_NoHangingNodes(string layout, double sizeA, double sizeB)
    {
        var root = Path.Combine(Path.GetTempPath(), $"opencs-junction-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var schema = new FemSchema { Tag = layout };
            db.SaveFemSchema(schema);
            var section = new PlateSection { H = 0.2, Tag = "П200" };
            db.SavePlateSection(section);
            // Закрепления — свободными узлами в углах, чтобы схема не была пустой по стержням.
            db.SaveFemSchemaEdit(schema.Id, [Node(schema, "1", 0, 0, 0)], [], [], [], []);

            static Frame3D Horizontal(double z, double x0 = 0) =>
                new(new PlanarVector3(x0, 0, z), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 1, 0), new PlanarVector3(0, 0, 1));
            static Frame3D WallY(double y) =>
                new(new PlanarVector3(0, y, 0), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 0, 1), new PlanarVector3(0, -1, 0));
            static Frame3D WallX(double x) =>
                new(new PlanarVector3(x, 0, 0), new PlanarVector3(0, 1, 0), new PlanarVector3(0, 0, 1), new PlanarVector3(1, 0, 0));
            var regions = layout switch
            {
                "wall-under-slab-edge" => new[] { ("A", WallY(0), 6.0, 3.0, sizeA), ("B", Horizontal(3), 6.0, 4.0, sizeB) },
                "wall-through-slab" => [("A", WallY(2), 6.0, 6.0, sizeA), ("B", Horizontal(3), 6.0, 4.0, sizeB)],
                "wall-under-slab-middle" => [("A", WallY(2), 6.0, 3.0, sizeA), ("B", Horizontal(3), 6.0, 4.0, sizeB)],
                "corner-walls" => [("A", WallY(0), 6.0, 3.0, sizeA), ("B", WallX(6), 4.0, 3.0, sizeB)],
                "adjacent-slabs" => [("A", Horizontal(3), 3.0, 4.0, sizeA), ("B", Horizontal(3, 3), 3.0, 4.0, sizeB)],
                _ => throw new ArgumentOutOfRangeException(nameof(layout)),
            };
            foreach (var (tag, frame, width, height, size) in regions)
            {
                var region = PlanarRegion.CreateFromContour(new Contour { X = [0, width, width, 0], Y = [0, 0, height, height] },
                    frame: frame, tag: tag);
                region.MeshMaxElementSizeM = size;
                db.AddPlanarRegion(region, schema.Id);
                db.SaveFemMember(new FemMember
                {
                    SchemaId = schema.Id, ElemTag = tag, ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = region.Id,
                    PlateSectionId = section.Id,
                });
            }

            var service = new FemSchemaMeshService(db, new GmshSettings
            {
                ExecutablePath = Gmsh, ArtifactsPath = Path.Combine(root, "gmsh"), KeepArtifacts = false,
                ElementMode = PlanarMeshElementMode.Quads,
            });
            var result = await service.BuildAsync(schema.Id, db.GetFemNodes(schema.Id), db.GetFemMembers(schema.Id), null, null,
                CancellationToken.None);

            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.Equal(2, result.RegionCount);
            Assert.True(result.Mesh!.SharedNodeCount >= 3, $"общих узлов {result.Mesh.SharedNodeCount}");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    static FemNode Node(FemSchema schema, string tag, double x, double y, double z) =>
        new() { SchemaId = schema.Id, NodeTag = tag, X = x, Y = y, Z = z };

    static FemMember Bar(FemSchema schema, string tag, int a, int b) =>
        new() { SchemaId = schema.Id, ElemTag = tag, ElemType = "beam", NodeIdsJson = $"[{a},{b}]" };

    static int[] Ids(FemElement e) => JsonSerializer.Deserialize<int[]>(e.NodeIdsJson)!;
}
