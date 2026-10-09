using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
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

    /// <summary>Проверка пластин по КЭ своей сетки (срез 5): КЭ из «Построить сетку схемы» входят в цель КонЭ,
    /// раскладка OpenCS ложится по зонам области, оси выдачи усилий — оси области (без предупреждений об осях).</summary>
    [Fact]
    public async Task SlabOnColumns_PlateCheckByElementsWithLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), $"opencs-schema-check-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var schema = new FemSchema { Tag = "slab" };
            db.SaveFemSchema(schema);
            var nodes = new List<FemNode>
            {
                Node(schema, "1", 0, 0, 0), Node(schema, "2", 4, 0, 0), Node(schema, "3", 4, 4, 0), Node(schema, "4", 0, 4, 0),
                Node(schema, "5", 0, 0, H), Node(schema, "6", 4, 0, H), Node(schema, "7", 4, 4, H), Node(schema, "8", 0, 4, H),
            };
            db.SaveFemSchemaEdit(schema.Id, nodes,
                [Bar(schema, "C1", 1, 5), Bar(schema, "C2", 2, 6), Bar(schema, "C3", 3, 7), Bar(schema, "C4", 4, 8)], [], [], []);

            const double z = 0.065;
            static PlateRebarLayer Layer(string name, double zs, RebarFace face, double a = 565e-6) => new()
            {
                Name = name, InputMode = "direct", Asx = a, Asy = a, Zsx = zs, Zsy = zs, DiameterX = 0.012, DiameterY = 0.012, Face = face,
            };
            var section = new PlateSection
            {
                Tag = "П200", H = 0.2, NLayers = 40, PlateModel = "layered", ConcreteDiagramType = DiagrammType.L3,
                RebarLayers = [Layer("низ", -z, RebarFace.MinusN), Layer("верх", z, RebarFace.PlusN)],
            };
            db.SavePlateSection(section);
            var frame = new Frame3D(new PlanarVector3(0, 0, H), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 1, 0), new PlanarVector3(0, 0, 1));
            var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] }, frame: frame, tag: "P1");
            region.MeshMaxElementSizeM = 0.5;
            // Усиление верха над колонной C1 — квадрат 0…1 × 0…1 в осях области.
            region.RebarZones.Add(new RebarZone
            {
                Name = "Над колонной", Face = RebarFace.PlusN, Operation = RebarZoneOperation.Add, Priority = 1,
                Polygon = [new() { U = 0, V = 0 }, new() { U = 1, V = 0 }, new() { U = 1, V = 1 }, new() { U = 0, V = 1 }],
                Layout = Layer("", z, RebarFace.PlusN, 20.1e-4),
            });
            db.AddPlanarRegion(region, schema.Id);
            var plate = new FemMember
            {
                SchemaId = schema.Id, ElemTag = "P1", ElemType = "shell", PlanarRegionId = region.Id, PlateSectionId = section.Id,
            };
            db.SaveFemMember(plate);

            var service = new FemSchemaMeshService(db, new GmshSettings
            {
                ExecutablePath = Gmsh, ArtifactsPath = Path.Combine(root, "gmsh"), KeepArtifacts = false,
                ElementMode = PlanarMeshElementMode.Quads,
            });
            var build = await service.BuildAsync(schema.Id, nodes, db.GetFemMembers(schema.Id), null, null, CancellationToken.None);
            Assert.False(build.HasErrors, string.Join("\n", build.Diagnostics));
            service.Save(schema.Id, build.Mesh!);

            // Цель проверки — КонЭ плиты: все её КЭ из сетки схемы, с номерами.
            var data = FemCheckSchemaData.Load(db, schema.Id);
            var target = data.Members.Single(m => m.ElemTag == "P1");
            var scope = data.Scope(target);
            var shells = data.Mesh.Where(e => e.ElemType == "shell").ToList();
            Assert.Equal(shells.Count, scope.Elements.Count);
            Assert.All(scope.Elements, e => Assert.NotNull(e.ElemNum));

            // Раскладка: зона — только у КЭ над колонной, оси известны и совпадают, без зеркала.
            var resolver = data.LayoutResolver(db.PlateSections);
            var nodeByTag = data.MeshNodes.ToDictionary(n => n.NodeTag);
            int inZone = 0;
            foreach (var e in scope.Elements)
            {
                var r = resolver.Resolve(e.Element.ElemTag);
                Assert.NotNull(r.Layers);
                Assert.True(r.AxesKnown);
                Assert.Equal(0, r.ForceAngleDeg);
                Assert.False(r.Mirrored);
                var pts = Ids(e.Element).Distinct().Select(t => nodeByTag[t.ToString()]).ToList();
                bool centroidInZone = pts.Average(p => p.X) < 1 && pts.Average(p => p.Y) < 1;
                Assert.Equal(centroidInZone ? "Раскладка: фон + Над колонной" : "Раскладка: фон", r.Label);
                if (centroidInZone) inZone++;
            }
            Assert.True(inZone > 0, "нет КЭ в зоне усиления");

            // Проверка по КЭ целиком: строка на каждый КЭ, предупреждений об осях нет.
            var check = new FemCheck
            {
                NormCode = "rc_plate_check", Tag = "плита",
                ParamsJson = new PlateCheckParams { Kind = "shell_layered", CheckGroup = "uls", RebarSources = [FemCheckRebarSource.Layout] }.ToJson(),
            };
            var forces = new ForceSet
            {
                Id = 1, Kind = "shell", Tag = "РСН (C)", SourceType = "fea", SourceSchemaId = schema.Id,
                ShellItems = [.. scope.Elements.Select(e => new ShellLoadItem { Label = $"э.{e.ElemNum}", Mx = 60, SourceElementNum = e.ElemNum })],
            };
            var result = FemCheckRunner.RunPerElement(check, target, scope, [forces], new FemPerElementInputs
            {
                PlateTemplate = section, ConcreteMat = Concrete(), RebarMat = Rebar(),
                PlateSources = [new LayoutPlateSectionSource(section, resolver)],
            }, (_, _, _) => throw new InvalidOperationException("стержневой исполнитель не нужен"));

            using var doc = JsonDocument.Parse(result.DataJson);
            var elements = doc.RootElement.GetProperty("elements").EnumerateArray().ToList();
            Assert.Equal(shells.Count, elements.Count);
            Assert.All(elements, e => Assert.Contains(e.GetProperty("status").GetString(), new[] { "ok", "failed" }));
            var warnings = doc.RootElement.GetProperty("summary").GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToList();
            Assert.DoesNotContain(warnings, w => w.Contains("оси выдачи"));
            Assert.DoesNotContain(warnings, w => w.Contains("зеркально"));
            // Момент растягивает верх: над колонной усиление держит, в пролёте ⌀12 шаг 200 — нет.
            var statusByNum = elements.ToDictionary(e => e.GetProperty("elemNum").GetInt32(), e => e.GetProperty("status").GetString());
            foreach (var e in scope.Elements)
            {
                bool zone = resolver.Resolve(e.Element.ElemTag).Label.Contains("Над колонной");
                Assert.Equal(zone ? "ok" : "failed", statusByNum[e.ElemNum!.Value]);
            }
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    static MaterialChars ConcreteChars(CalcType ct, double rb, double rbt) => new(ct)
    {
        Type = MatType.Concrete, E = 30_000_000.0, Fc = -rb, Ft = rbt,
        Ec0 = -0.002, Ec1 = -0.6 * rb / 30_000_000.0, Ec2 = -0.0035, Ec1Red = -0.0015,
        Et0 = 0.0001, Et1 = 0.6 * rbt / 30_000_000.0, Et2 = 0.00015, Et1Red = 0.00008,
    };

    static MaterialChars RebarChars(CalcType ct, double rs) => new(ct)
    {
        Type = MatType.ReSteelF, E = 200_000_000.0, Fc = -rs, Ft = rs, Ec2 = -0.025, Et2 = 0.025,
    };

    static Material Concrete()
    {
        var m = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0 };
        m.C = ConcreteChars(CalcType.C, 14_500.0, 1_050.0);
        m.CL = ConcreteChars(CalcType.CL, 14_500.0, 1_050.0);
        m.N = ConcreteChars(CalcType.N, 18_500.0, 1_550.0);
        m.NL = ConcreteChars(CalcType.NL, 18_500.0, 1_550.0);
        return m;
    }

    static Material Rebar()
    {
        var m = new Material { Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000.0 };
        m.C = RebarChars(CalcType.C, 435_000.0);
        m.CL = RebarChars(CalcType.CL, 435_000.0);
        m.N = RebarChars(CalcType.N, 500_000.0);
        m.NL = RebarChars(CalcType.NL, 500_000.0);
        return m;
    }

    static FemNode Node(FemSchema schema, string tag, double x, double y, double z) =>
        new() { SchemaId = schema.Id, NodeTag = tag, X = x, Y = y, Z = z };

    static FemMember Bar(FemSchema schema, string tag, int a, int b) =>
        new() { SchemaId = schema.Id, ElemTag = tag, ElemType = "beam", NodeIdsJson = $"[{a},{b}]" };

    static int[] Ids(FemElement e) => JsonSerializer.Deserialize<int[]>(e.NodeIdsJson)!;
}
