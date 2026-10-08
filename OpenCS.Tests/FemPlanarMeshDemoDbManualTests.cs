using CScore;
using CScore.Fem;
using CScore.ParametricRc;
using CScore.Planar;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Тестовый проект для ручной проверки «Построить сетку схемы» (CSfea 4г). Запуск:
/// <c>OPENCS_PLANAR_DEMO_DB=путь\к\файлу.db dotnet test OpenCS.Tests -p:AllowUnsafeBlocks=true
/// --filter FullyQualifiedName~FemPlanarMeshDemoDbManualTests</c>; без переменной тест ничего не делает.
/// Схемы: 1 — плита на колоннах и балка по кромке; 2 — колонна на кромке, балки крест-накрест без узла в пересечении,
/// точечная опора на плите; 3 — стена под кромкой плиты (стык областей, срез 4); 4 — две соседние плиты с разным
/// шагом сетки. Сетки не построены — их строит проверяемая команда.
/// </summary>
public sealed class FemPlanarMeshDemoDbManualTests(Xunit.Abstractions.ITestOutputHelper output)
{
    const double H = 3.0;

    /// <summary>Сборка сетки всех схем копии тестовой базы (OPENCS_PLANAR_DEMO_CHECK — путь к базе) — сводка в вывод.</summary>
    [Fact]
    public async Task BuildAllSchemas()
    {
        var source = Environment.GetEnvironmentVariable("OPENCS_PLANAR_DEMO_CHECK");
        if (string.IsNullOrWhiteSpace(source)) return;
        var root = Path.Combine(Path.GetTempPath(), $"opencs-planar-demo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "demo.db");
        File.Copy(source, path);
        using var db = new DatabaseService(path);
        db.LoadAll();
        var gmsh = new GmshSettings
        {
            ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe", ArtifactsPath = Path.Combine(root, "gmsh"),
        };
        var service = new FemSchemaMeshService(db, gmsh);
        foreach (var schema in db.FemSchemas.ToList())
        {
            var result = await service.BuildAsync(schema.Id, db.GetFemNodes(schema.Id), db.GetFemMembers(schema.Id), null,
                line => output.WriteLine("  " + line), CancellationToken.None);
            var mesh = result.Mesh;
            output.WriteLine($"{schema.Tag}: ошибки {result.HasErrors}, узлов {mesh?.Nodes.Count}, стержней " +
                $"{mesh?.Elements.Count(e => e.ElemType == "beam")}, пластин {mesh?.Elements.Count(e => e.ElemType == "shell")}, " +
                $"общих {mesh?.SharedNodeCount}, разделено стержней {mesh?.SplitBeamCount}");
            foreach (var d in result.Diagnostics) output.WriteLine($"  {(d.IsError ? "ОШИБКА" : "предупр.")} {d.Code}: {d.Message}");
        }
    }

    [Fact]
    public void CreateDemoDatabase()
    {
        var path = Environment.GetEnvironmentVariable("OPENCS_PLANAR_DEMO_DB");
        if (string.IsNullOrWhiteSpace(path)) return;
        if (File.Exists(path)) File.Delete(path);

        using var db = new DatabaseService(path);
        var catalog = CatalogDirectory();
        var concrete = MaterialCatalog.CreateHeavyConcrete("B25", catalog) ?? throw new InvalidOperationException("B25");
        var rebar = MaterialCatalog.CreateRebar("A500", catalog) ?? throw new InvalidOperationException("A500");
        db.AddMaterial(concrete);
        db.AddMaterial(rebar);

        var column = RcSection(db, "К 400×400", 0.4, 0.4, concrete, rebar);
        var beam = RcSection(db, "Б 300×600", 0.3, 0.6, concrete, rebar);
        var slab = Plate(db, "П 200", 0.2, concrete, rebar);
        var wall = Plate(db, "С 200", 0.2, concrete, rebar);

        // 1. Плита 6×4 м на четырёх угловых колоннах + балка по кромке y = 0.
        {
            var s = Schema(db, "4г-1 Плита на колоннах, балка по кромке");
            var nodes = Columns(s, (0, 0), (6, 0), (6, 4), (0, 4));
            var members = ColumnMembers(s, nodes, column);
            members.Add(Bar(s, "Б1", Top(nodes, 0, 0), Top(nodes, 6, 0), beam));
            db.SaveFemSchemaEdit(s.Id, nodes, members, [], [], []);
            Slab(db, s, "П1", 0, 0, 6, 4, 0.5, slab);
        }

        // 2. Плита 6×6: угловые колонны + колонна в середине кромки y = 0; балки в плоскости плиты от кромки до
        // кромки крест-накрест (пересечение (2; 2) без узла); точечная опора UZ на плите в (4,5; 4,5).
        {
            var s = Schema(db, "4г-2 Колонна на кромке, балки крест-накрест, опора на плите");
            var nodes = Columns(s, (0, 0), (3, 0), (6, 0), (6, 6), (0, 6));
            var members = ColumnMembers(s, nodes, column);
            int next = nodes.Count + 1;
            FemNode Add(double x, double y, int mask = 0)
            {
                var n = new FemNode { SchemaId = s.Id, NodeTag = (next++).ToString(), X = x, Y = y, Z = H, DofMask = mask };
                nodes.Add(n);
                return n;
            }
            var a = Add(0, 2); var b = Add(6, 2); var c = Add(2, 0); var d = Add(2, 6);
            Add(4.5, 4.5, mask: 4);
            members.Add(Bar(s, "Б1", a, b, beam));
            members.Add(Bar(s, "Б2", c, d, beam));
            db.SaveFemSchemaEdit(s.Id, nodes, members, [], [], []);
            Slab(db, s, "П1", 0, 0, 6, 6, 0.5, slab);
        }

        // 3. Плита 6×4 на стене по кромке y = 0 (сетка стены мельче) и двух колоннах у дальней кромки; низ стены —
        // закреплённые узлы в углах.
        {
            var s = Schema(db, "4г-3 Стена под кромкой плиты (стык, срез 4)");
            var nodes = Columns(s, (6, 4), (0, 4));
            var members = ColumnMembers(s, nodes, column);
            nodes.Add(new FemNode { SchemaId = s.Id, NodeTag = "10", X = 0, Y = 0, Z = 0, DofMask = 63 });
            nodes.Add(new FemNode { SchemaId = s.Id, NodeTag = "11", X = 6, Y = 0, Z = 0, DofMask = 63 });
            db.SaveFemSchemaEdit(s.Id, nodes, members, [], [], []);
            Slab(db, s, "П1", 0, 0, 6, 4, 0.5, slab);
            var frame = new Frame3D(new PlanarVector3(0, 0, 0), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 0, 1), new PlanarVector3(0, -1, 0));
            Region(db, s, "С1", frame, 0, 0, 6, H, 0.3, wall, "wall");
        }

        // 4. Две соседние плиты 3×4 м (шаг 0,3 и 0,5 м) на шести колоннах.
        {
            var s = Schema(db, "4г-4 Две соседние плиты разного шага");
            var nodes = Columns(s, (0, 0), (3, 0), (6, 0), (6, 4), (3, 4), (0, 4));
            var members = ColumnMembers(s, nodes, column);
            db.SaveFemSchemaEdit(s.Id, nodes, members, [], [], []);
            Slab(db, s, "П1", 0, 0, 3, 4, 0.3, slab);
            Slab(db, s, "П2", 3, 0, 3, 4, 0.5, slab);
        }
    }

    static FemSchema Schema(DatabaseService db, string tag)
    {
        var schema = new FemSchema { Tag = tag };
        db.SaveFemSchema(schema);
        return schema;
    }

    /// <summary>Колонны высотой H: низ закреплён (теги 1…n), верх — теги n+1…2n.</summary>
    static List<FemNode> Columns(FemSchema s, params (double X, double Y)[] points)
    {
        var nodes = new List<FemNode>();
        for (int i = 0; i < points.Length; i++)
            nodes.Add(new FemNode { SchemaId = s.Id, NodeTag = (i + 1).ToString(), X = points[i].X, Y = points[i].Y, Z = 0, DofMask = 63 });
        for (int i = 0; i < points.Length; i++)
            nodes.Add(new FemNode { SchemaId = s.Id, NodeTag = (points.Length + i + 1).ToString(), X = points[i].X, Y = points[i].Y, Z = H });
        return nodes;
    }

    static List<FemMember> ColumnMembers(FemSchema s, List<FemNode> nodes, CrossSection section)
    {
        int n = nodes.Count / 2;
        return [.. Enumerable.Range(0, n).Select(i => Bar(s, $"К{i + 1}", nodes[i], nodes[n + i], section))];
    }

    static FemNode Top(List<FemNode> nodes, double x, double y) => nodes.Single(n => n.X == x && n.Y == y && n.Z == H);

    static FemMember Bar(FemSchema s, string tag, FemNode a, FemNode b, CrossSection section) => new()
    {
        SchemaId = s.Id, ElemTag = tag, ElemType = "beam", NodeIdsJson = $"[{a.NodeTag},{b.NodeTag}]", CrossSectionId = section.Id,
    };

    static void Slab(DatabaseService db, FemSchema s, string tag, double x0, double y0, double width, double length,
        double meshSize, PlateSection section)
    {
        var frame = new Frame3D(new PlanarVector3(x0, y0, H), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 1, 0), new PlanarVector3(0, 0, 1));
        Region(db, s, tag, frame, 0, 0, width, length, meshSize, section, "plate");
    }

    static void Region(DatabaseService db, FemSchema s, string tag, Frame3D frame, double u0, double v0, double width,
        double length, double meshSize, PlateSection section, string kind)
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [u0, u0 + width, u0 + width, u0], Y = [v0, v0, v0 + length, v0 + length] }, frame: frame, tag: tag);
        region.MeshMaxElementSizeM = meshSize;
        db.AddPlanarRegion(region, s.Id);
        db.SaveFemMember(new FemMember
        {
            SchemaId = s.Id, ElemTag = tag, ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = region.Id,
            Kind = kind, KindSource = "manual", PlateSectionId = section.Id,
        });
    }

    static CrossSection RcSection(DatabaseService db, string tag, double b, double h, Material concrete, Material rebar)
    {
        var definition = ParametricRcSectionDefinition.Rectangle(b, h) with
        {
            Tag = tag, ConcreteMaterialId = concrete.Id, LongitudinalMaterialId = rebar.Id,
        };
        var section = new CrossSection { Num = db.CrossSections.Count + 1, Tag = tag };
        var result = new ParametricRcSectionProjectService(db).GenerateAndSave(section, definition);
        Assert.Empty(result.Diagnostics);
        return section;
    }

    /// <summary>Сечение пластины h с сетками Ø12/200 у обеих граней (защитный слой до оси 35 мм).</summary>
    static PlateSection Plate(DatabaseService db, string tag, double h, Material concrete, Material rebar)
    {
        const double area = 5.65e-4;   // Ø12 шаг 200 мм, м²/м
        double z = h / 2 - 0.035;
        var section = new PlateSection
        {
            Num = db.PlateSections.Count + 1, Tag = tag, H = h, NLayers = 20,
            ConcreteMaterialId = concrete.Id, RebarMaterialId = rebar.Id,
            RebarLayers =
            [
                new PlateRebarLayer { Name = "низ", Asx = area, Asy = area, Zsx = -z, Zsy = -z, InputMode = "direct" },
                new PlateRebarLayer { Name = "верх", Asx = area, Asy = area, Zsx = z, Zsy = z, InputMode = "direct" },
            ],
        };
        db.SavePlateSection(section);
        return section;
    }

    static string CatalogDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCS.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "OpenCS", "DataSource");
    }
}
