using System.Globalization;
using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit.Abstractions;

namespace OpenCS.Gmsh.Tests;

/// <summary>
/// Линейный CSfea на своей схеме после «Построить сетку схемы» (CSfea 4г, срез 6): реальный Gmsh → сетка схемы в БД →
/// <see cref="FemCsfeaInputBuilder"/> → <see cref="FemRcModelAdapter"/> → линейное решение.
/// </summary>
public sealed class CsfeaOwnSchemaLinearTests(ITestOutputHelper output)
{
    const string Gmsh = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe";
    const double Q = -5e3;   // Па, вниз

    /// <summary>Плита 4×4 м на четырёх точечных опорах в углах (закрепления узлов схемы): ΣRz = −ΣFz.</summary>
    [Fact]
    public async Task SlabOnPointSupports_Equilibrium()
    {
        await WithSchema(4.0, 0.2, 0.5, async (db, schema, analysis) =>
        {
            var (model, build, f, u) = Solve(db, schema, analysis);
            var r = build.Mesh.ComputeReactions(u, build.Bc, fExternal: f);
            double fz = f.Where((_, i) => i % 6 == 2).Sum();
            double rz = build.NodeIds.Sum(id => r[build.Dof(id, 2)]);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"ΣFz {fz / 1e3:0.###} кН, ΣRz {rz / 1e3:0.###} кН, опорных узлов {model.Supports.Count}"));
            Assert.Equal(Q * 16, fz, 1e-6 * Math.Abs(Q * 16));
            Assert.Equal(-fz, rz, 1e-6 * Math.Abs(fz));
            Assert.Equal(4, model.Supports.Count);
        }, cornerMask: 0b000111);
    }

    /// <summary>
    /// Шарнирно опёртая квадратная плита 6×6 м, h = 0,12 м (h/a = 1/50) под равномерной нагрузкой — прогиб в центре
    /// против ряда Навье (Кирхгоф) ±2 %; E бетона — из материала каталога (кПа). Опоры — закрепления uz по всем узлам контура сетки (сеточный уровень).
    /// </summary>
    [Fact]
    public async Task SimplySupportedSquare_NavierDeflection()
    {
        const double a = 6.0, h = 0.12;
        await WithSchema(a, h, 0.25, async (db, schema, analysis) =>
        {
            var tol = 1e-6;
            foreach (var n in db.GetFemMeshNodes(schema.Id))
            {
                bool edge = Math.Abs(n.X) < tol || Math.Abs(n.X - a) < tol || Math.Abs(n.Y) < tol || Math.Abs(n.Y - a) < tol;
                bool corner = Math.Abs(n.X) < tol && Math.Abs(n.Y) < tol;
                bool cornerX = Math.Abs(n.X - a) < tol && Math.Abs(n.Y) < tol;
                // uz по контуру; в плоскости — минимум против смещения как жёсткого целого.
                int mask = !edge ? -1 : corner ? 0b000111 : cornerX ? 0b000110 : 0b000100;
                if (mask >= 0) db.SetFemMeshNodeBoundary(schema.Id, n.NodeTag, mask, new double[6]);
            }

            var (model, build, f, u) = Solve(db, schema, analysis);
            var nodes = model.Nodes.ToDictionary(n => n.Id);
            var center = model.Nodes.MinBy(n => Math.Pow(n.X - a / 2, 2) + Math.Pow(n.Y - a / 2, 2))!;
            Assert.True(Math.Abs(center.X - a / 2) < 1e-6 && Math.Abs(center.Y - a / 2) < 1e-6, "нет узла в центре плиты");
            double w = u[build.Dof(center.Id, 2)];

            var concreteId = db.PlateSections.Where(p => p.Tag == "П").Select(p => p.ConcreteMaterialId).Distinct().Single();
            var concrete = db.Materials.First(m => m.Id == concreteId);
            const double nu = 0.2;   // PlateSectionMaterials.Nu по умолчанию
            double ePa = concrete.E * 1e3;   // Material.E — кПа
            double d = ePa * h * h * h / (12 * (1 - nu * nu));
            double navier = NavierCenter(Q, a, d);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"w CSfea {w * 1e3:0.####} мм, Навье {navier * 1e3:0.####} мм, " +
                $"отношение {w / navier:0.####}; узлов {model.Nodes.Count}, оболочек {model.Shells.Count}"));
            Assert.InRange(w / navier, 0.98, 1.02);
        });
    }

    /// <summary>Ряд Навье: прогиб центра шарнирной квадратной плиты под равномерной нагрузкой q.</summary>
    static double NavierCenter(double q, double a, double d)
    {
        double sum = 0;
        for (int m = 1; m < 200; m += 2)
            for (int n = 1; n < 200; n += 2)
            {
                double sign = ((m + n) / 2 - 1) % 2 == 0 ? 1 : -1;   // sin(mπ/2)·sin(nπ/2)
                sum += sign / (m * n * Math.Pow(m * m + n * n, 2));
            }
        return 16 * q * Math.Pow(a, 4) / (Math.Pow(Math.PI, 6) * d) * sum;
    }

    static (RcStructuralModel Model, RcStructuralMeshBuild Build, double[] F, double[] U) Solve(
        DatabaseService db, FemSchema schema, FemAnalysisStage stage)
    {
        var input = FemCsfeaInputBuilder.Build(db, schema.Id, new FemCsfeaSetup { Stages = [stage] });
        Assert.DoesNotContain(input.Diagnostics, x => x.IsError);
        var adapted = FemRcModelAdapter.Adapt(input);
        Assert.DoesNotContain(adapted.Diagnostics, x => x.IsError);
        var build = RcStructuralMeshBuilder.Build(adapted.Model, new LinearRcSectionFactory());
        var f = build.Combination(adapted.Model.Stages[0].Loads);
        return (adapted.Model, build, f, build.Mesh.SolveLinear(f, build.Bc));
    }

    /// <summary>
    /// Схема: квадратная плита a×a на отметке 0 (своя область, сечение с материалами каталога), узлы схемы в углах и центре
    /// (с закреплением <paramref name="cornerMask"/>), загружение с равномерной нагрузкой <see cref="Q"/> на плиту,
    /// сетка схемы построена и сохранена.
    /// </summary>
    static async Task WithSchema(double a, double h, double meshSize,
        Func<DatabaseService, FemSchema, FemAnalysisStage, Task> body, int cornerMask = 0)
    {
        var root = Path.Combine(Path.GetTempPath(), $"opencs-csfea-own-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var schema = new FemSchema { Tag = "slab" };
            db.SaveFemSchema(schema);
            var concrete = MaterialCatalog.CreateHeavyConcrete("B25", CatalogDirectory())!;
            var rebar = MaterialCatalog.CreateRebar("А500С", CatalogDirectory())!;
            db.AddMaterial(concrete);
            db.AddMaterial(rebar);
            double z = h / 2 - 0.03;
            var section = new PlateSection
            {
                Tag = "П", H = h, ConcreteMaterialId = concrete.Id, RebarMaterialId = rebar.Id,
                RebarLayers =
                [
                    new PlateRebarLayer { Name = "низ", InputMode = "direct", Asx = 5e-4, Asy = 5e-4, Zsx = -z, Zsy = -z, Face = RebarFace.MinusN },
                    new PlateRebarLayer { Name = "верх", InputMode = "direct", Asx = 5e-4, Asy = 5e-4, Zsx = z, Zsy = z, Face = RebarFace.PlusN },
                ],
            };
            db.SavePlateSection(section);

            var nodes = new List<FemNode>
            {
                new() { SchemaId = schema.Id, NodeTag = "1", X = 0, Y = 0, DofMask = cornerMask },
                new() { SchemaId = schema.Id, NodeTag = "2", X = a, Y = 0, DofMask = cornerMask },
                new() { SchemaId = schema.Id, NodeTag = "3", X = a, Y = a, DofMask = cornerMask },
                new() { SchemaId = schema.Id, NodeTag = "4", X = 0, Y = a, DofMask = cornerMask },
                // Свободный узел в центре — встраивается точкой: в нём меряется прогиб.
                new() { SchemaId = schema.Id, NodeTag = "5", X = a / 2, Y = a / 2 },
            };
            var lc = new FemLoadCase { SchemaId = schema.Id, Tag = "q", SelfWeightFactor = 0 };
            db.SaveFemSchemaEdit(schema.Id, nodes, [], [], [lc], []);
            lc = db.GetFemLoadCases(schema.Id).Single();

            var frame = new Frame3D(new PlanarVector3(0, 0, 0), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 1, 0), new PlanarVector3(0, 0, 1));
            var region = PlanarRegion.CreateFromContour(new Contour { X = [0, a, a, 0], Y = [0, 0, a, a] }, frame: frame, tag: "P1");
            region.MeshMaxElementSizeM = meshSize;
            db.AddPlanarRegion(region, schema.Id);
            db.SaveFemMember(new FemMember
            {
                SchemaId = schema.Id, ElemTag = "P1", ElemType = "shell", PlanarRegionId = region.Id, PlateSectionId = section.Id,
            });

            var service = new FemSchemaMeshService(db, new GmshSettings
            {
                ExecutablePath = Gmsh, ArtifactsPath = Path.Combine(root, "gmsh"), KeepArtifacts = false,
                ElementMode = PlanarMeshElementMode.Quads, Algorithm = 8,
            });
            var built = await service.BuildAsync(schema.Id, nodes, db.GetFemMembers(schema.Id), null, null, CancellationToken.None);
            Assert.False(built.HasErrors, string.Join("\n", built.Diagnostics));
            service.Save(schema.Id, built.Mesh!);

            var load = new FemElementLoad
            {
                SchemaId = schema.Id, LoadCaseId = lc.Id, TargetKind = FemLoadTargetKinds.Members,
                LoadKind = FemElementLoadKinds.Uniform, Axis = "z",
            };
            load.SetTargetTags(["P1"]);
            load.SetValues([Q]);
            db.SaveFemLoadCasesAndMeshLoads(schema.Id, [lc], [load], []);
            db.LoadAll();

            var stage = new FemAnalysisStage
            {
                Tag = "q", LoadFactorStep = 1, MaxLoadFactor = 1,
                LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [lc.Id] }.ToJson(),
            };
            await body(db, schema, stage);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    static string CatalogDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCS.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "OpenCS", "DataSource");
    }
}
