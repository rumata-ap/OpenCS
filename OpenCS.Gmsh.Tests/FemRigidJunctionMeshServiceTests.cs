using CScore;
using CScore.Fem;
using CScore.Planar;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Gmsh.Tests;

/// <summary>Авто-АЖТ примыканий на реальном Gmsh (срез 3): плита 4×4 м на колоннах 0,4×0,4 в углах и в центре —
/// контур следа вшит в сетку, тела записаны с происхождением «auto:junction», повтор переиспользует снимок, смена
/// сечения пересобирает сетку, выключенный флажок удаляет тела.</summary>
public sealed class FemRigidJunctionMeshServiceTests
{
    const string Gmsh = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe";
    const double H = 3.0, Tol = 1e-6;

    static CrossSection Square(int id, double a) => new()
    {
        Id = id,
        Areas =
        [
            new MaterialArea
            {
                Tag = "concrete", Category = AreaCategory.Region,
                Hull = new Contour(new[] { -a / 2, a / 2, a / 2, -a / 2, -a / 2 }, new[] { -a / 2, -a / 2, a / 2, a / 2, -a / 2 }, "outer"),
            },
        ],
    };

    [Fact]
    public async Task SlabOnColumns_TraceStitched_BodiesWritten_ReuseRebuildAndSwitchOff()
    {
        var root = Path.Combine(Path.GetTempPath(), $"opencs-rigid-junction-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var schema = new FemSchema { Tag = "slab" };
            db.SaveFemSchema(schema);
            db.CrossSections.Add(Square(9001, 0.4));
            db.CrossSections.Add(Square(9002, 0.6));
            var columns = new (double X, double Y)[] { (0, 0), (4, 0), (4, 4), (0, 4), (2, 2) };
            var nodes = new List<FemNode>();
            var bars = new List<FemMember>();
            for (int i = 0; i < columns.Length; i++)
            {
                var (x, y) = columns[i];
                nodes.Add(new FemNode { SchemaId = schema.Id, NodeTag = $"{i + 1}", X = x, Y = y, Z = 0 });
                nodes.Add(new FemNode { SchemaId = schema.Id, NodeTag = $"{i + 11}", X = x, Y = y, Z = H });
                bars.Add(new FemMember { SchemaId = schema.Id, ElemTag = $"C{i + 1}", ElemType = "beam", NodeIdsJson = $"[{i + 1},{i + 11}]" });
            }
            db.SaveFemSchemaEdit(schema.Id, nodes, bars, [], [], []);
            var section = new PlateSection { H = 0.2, Tag = "П200" };
            db.SavePlateSection(section);
            var frame = new Frame3D(new PlanarVector3(0, 0, H), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 1, 0), new PlanarVector3(0, 0, 1));
            var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] }, frame: frame, tag: "P1");
            region.MeshMaxElementSizeM = 0.5;
            db.AddPlanarRegion(region, schema.Id);
            db.SaveFemMember(new FemMember
            {
                SchemaId = schema.Id, ElemTag = "P1", ElemType = "shell", PlanarRegionId = region.Id, PlateSectionId = section.Id,
            });

            var gmsh = new GmshSettings
            {
                ExecutablePath = Gmsh, ArtifactsPath = Path.Combine(root, "gmsh"), KeepArtifacts = false,
                ElementMode = PlanarMeshElementMode.Quads,
            };
            var service = new FemSchemaMeshService(db, gmsh);
            int centerSection = 9001;

            // Сечения — только в памяти (как сессия редактора): в cross_sections их нет.
            List<FemMember> Members()
            {
                var list = db.GetFemMembers(schema.Id);
                foreach (var m in list.Where(m => m.ElemType == "beam"))
                    m.CrossSectionId = m.ElemTag == "C5" ? centerSection : 9001;
                return list;
            }
            async Task<FemSchemaMeshBuildResult> BuildAndSave()
            {
                var result = await service.BuildAsync(schema.Id, nodes, Members(), null, null, CancellationToken.None);
                Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
                service.Save(schema.Id, result.Mesh!);
                return result;
            }
            Dictionary<string, HashSet<string>> Stored() => db.GetFemRigidBodies(schema.Id)
                .Where(b => b.Origin == RigidJunctionTraceSet.Provenance)
                .ToDictionary(b => b.MasterNodeTag, b => b.SlaveNodeTags.ToHashSet());

            // 1. Первая сборка: 5 тел; ведомые — узлы плиты в (обрезанном кромкой) квадрате следа, углы следа — узлы сетки.
            var first = await BuildAndSave();
            Assert.Equal(1, first.RebuiltRegionCount);
            var mesh = first.Mesh!;
            var bodies = Stored();
            Assert.Equal(5, bodies.Count);
            for (int i = 0; i < columns.Length; i++)
            {
                var (cx, cy) = columns[i];
                var master = mesh.Nodes.Single(n => n.SourceNodeTag == $"{i + 11}").NodeTag;
                var slaves = bodies[master];
                Assert.NotEmpty(slaves);
                var expected = mesh.Nodes
                    .Where(n => Math.Abs(n.Z - H) < Tol && Math.Abs(n.X - cx) <= 0.2 + Tol && Math.Abs(n.Y - cy) <= 0.2 + Tol &&
                                n.NodeTag != master)
                    .Select(n => n.NodeTag).ToHashSet();
                Assert.Equal(expected, slaves);
            }
            var center = mesh.Nodes.Single(n => n.SourceNodeTag == "15").NodeTag;
            foreach (var (x, y) in new[] { (1.8, 1.8), (2.2, 1.8), (2.2, 2.2), (1.8, 2.2) })
                Assert.Contains(mesh.Nodes, n => Math.Abs(n.X - x) < Tol && Math.Abs(n.Y - y) < Tol && bodies[center].Contains(n.NodeTag));

            // 2. Повтор без изменений: снимок переиспользован, тела те же.
            var second = await BuildAndSave();
            Assert.Equal(0, second.RebuiltRegionCount);
            Assert.Equal(bodies.Count, Stored().Count);
            Assert.All(Stored(), kv => Assert.Equal(bodies[kv.Key], kv.Value));

            // 3. Сечение центральной колонны 0,6×0,6: сетка пересобрана, след шире.
            centerSection = 9002;
            var third = await BuildAndSave();
            Assert.Equal(1, third.RebuiltRegionCount);
            var wide = third.Mesh!;
            var wideCenter = wide.Nodes.Single(n => n.SourceNodeTag == "15").NodeTag;
            Assert.Contains(wide.Nodes, n => Math.Abs(n.X - 2.3) < Tol && Math.Abs(n.Y - 2.3) < Tol && Stored()[wideCenter].Contains(n.NodeTag));

            // 4. Флажок схемы выключен: следов в сетке нет, авто-тела удалены.
            schema.AutoRigidJunctions = false;
            db.UpdateFemSchemaRigidJunctions(schema);
            var fourth = await BuildAndSave();
            Assert.Equal(1, fourth.RebuiltRegionCount);
            Assert.Empty(fourth.Mesh!.RigidBodies);
            Assert.Empty(Stored());
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }
}
