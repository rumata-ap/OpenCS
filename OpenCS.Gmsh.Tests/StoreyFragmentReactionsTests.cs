using System.Globalization;
using CScore;
using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Planar;
using CScore.PlateRebar;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit.Abstractions;

namespace OpenCS.Gmsh.Tests;

/// <summary>
/// Фрагмент этажа в линейном CSfea (спека 2026-10-23, разд. 7): родитель — только сетка (как после импорта), фрагмент по
/// отметкам 1,5…4,5 м → сечения КонЭ → «Построить сетку» (Gmsh) → заделка узлов сетки на отметках рассечения → линейное
/// решение по каждому загружению. Сумма реакций равна сумме нагрузок полосы, посчитанной вручную по нагрузкам родителя.
/// </summary>
public sealed class StoreyFragmentReactionsTests(ITestOutputHelper output)
{
    const string Gmsh = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe";
    const double Z1 = 1.5, Z2 = 4.5, Tol = 1e-6;

    static string S(int v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Узел плиты z = 3 (сетка 1 м, x, y = 0…6).</summary>
    static int Slab(int i, int j) => 1000 + j * 7 + i;

    /// <summary>
    /// Родитель-сетка: плита 6×6 м на z = 3 (КЭ 1×1 м), колонны z 0…6 в (0, 0), (6, 0), (3, 3) по 1,5 м (узел z = 3 —
    /// общий с плитой), стена по y = 6, z 0…6 (КЭ 1×1 м, ряд z = 3 — общий с плитой).
    /// </summary>
    static (List<FemMeshNode> Nodes, List<FemElement> Elements, List<string> SlabWest, List<string> SlabEast,
        List<string> Wall, List<string> Column1) ParentMesh()
    {
        var nodes = new List<FemMeshNode>();
        var elements = new List<FemElement>();
        List<string> west = [], east = [], wall = [], column1 = [];
        for (int j = 0; j <= 6; j++)
            for (int i = 0; i <= 6; i++)
                nodes.Add(new FemMeshNode { NodeTag = S(Slab(i, j)), X = i, Y = j, Z = 3 });
        int e = 1;
        for (int j = 0; j < 6; j++)
            for (int i = 0; i < 6; i++, e++)
            {
                elements.Add(Shell(e, Slab(i, j), Slab(i + 1, j), Slab(i, j + 1), Slab(i + 1, j + 1)));
                (i < 3 ? west : east).Add(S(e));
            }

        int WallNode(int i, int k) => k == 3 ? Slab(i, 6) : 3000 + k * 7 + i;
        for (int k = 0; k <= 6; k++)
            if (k != 3)
                for (int i = 0; i <= 6; i++)
                    nodes.Add(new FemMeshNode { NodeTag = S(WallNode(i, k)), X = i, Y = 6, Z = k });
        e = 101;
        for (int k = 0; k < 6; k++)
            for (int i = 0; i < 6; i++, e++)
            {
                elements.Add(Shell(e, WallNode(i, k), WallNode(i + 1, k), WallNode(i, k + 1), WallNode(i + 1, k + 1)));
                wall.Add(S(e));
            }

        (int I, int J)[] columns = [(0, 0), (6, 0), (3, 3)];
        double[] zs = [0, 1.5, 3, 4.5, 6];
        for (int c = 0; c < columns.Length; c++)
        {
            var (ci, cj) = columns[c];
            int Node(int k) => k == 2 ? Slab(ci, cj) : 2000 + c * 10 + k;
            for (int k = 0; k < zs.Length; k++)
                if (k != 2) nodes.Add(new FemMeshNode { NodeTag = S(Node(k)), X = ci, Y = cj, Z = zs[k] });
            for (int k = 0; k < zs.Length - 1; k++)
            {
                string tag = S(201 + c * 10 + k);
                elements.Add(new FemElement
                {
                    ElemTag = tag, ElemType = "beam", SectionTag = "1", NodeIdsJson = $"[{Node(k)},{Node(k + 1)}]",
                });
                if (c == 0) column1.Add(tag);
            }
        }
        return (nodes, elements, west, east, wall, column1);
    }

    static FemElement Shell(int tag, int n00, int n10, int n01, int n11) => new()
    {
        ElemTag = S(tag), ElemType = "shell", ThicknessM = 0.2, NodeIdsJson = $"[{n00},{n10},{n01},{n11}]",
    };

    static FemElementLoad Uniform(int lc, double q, IEnumerable<string> elements)
    {
        var load = new FemElementLoad
        {
            LoadCaseId = lc, LoadKind = FemElementLoadKinds.Uniform, Axis = "z", CoordinateSystem = "global",
            TargetKind = FemLoadTargetKinds.Elements,
        };
        load.SetTargetTags(elements.ToArray());
        load.SetValues([q]);
        return load;
    }

    [Fact]
    public async Task ImportedStorey_Fragment_ReactionsBalanceBandLoads()
    {
        var root = Path.Combine(Path.GetTempPath(), $"opencs-fragment-reactions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var parent = new FemSchema { Tag = "Каркас", SourceType = "lira" };
            db.SaveFemSchema(parent);
            var mesh = ParentMesh();
            db.SaveFemMeshSnapshot(parent.Id, mesh.Nodes, mesh.Elements);

            // «Пост.»: одно q на всю плиту (поднимается на КонЭ) + сила в узле плиты. «Полезная»: q по половинам плиты
            // (нагрузка-поле), равномерная по колонне 1 (в полосе — 3 м из 6), q на стене (в полосе — 6 × 3 м), сила по x.
            var loadCases = new List<FemLoadCase>
            {
                new() { Id = -1, Tag = "Пост.", Sp20Type = "permanent", SelfWeightFactor = 0 },
                new() { Id = -2, Tag = "Полезная", SelfWeightFactor = 0 },
            };
            var elementLoads = new List<FemElementLoad>
            {
                Uniform(-1, -5000, mesh.SlabWest.Concat(mesh.SlabEast)),
                Uniform(-2, -2000, mesh.SlabWest),
                Uniform(-2, -4000, mesh.SlabEast),
                Uniform(-2, -1000, mesh.Column1),
                Uniform(-2, -1000, mesh.Wall),
            };
            var meshNodeLoads = new List<FemMeshNodeLoad>
            {
                new() { LoadCaseId = -1, MeshNodeTag = S(Slab(1, 1)), Fz = -3000 },
                new() { LoadCaseId = -2, MeshNodeTag = S(Slab(1, 1)), Fx = 2000 },
            };
            db.SaveFemSchemaEdit(parent.Id, [], [], [], loadCases, [], [], [], [], elementLoads, meshNodeLoads);
            var expected = new Dictionary<string, (double Fx, double Fy, double Fz)>
            {
                ["Пост."] = (0, 0, -5000 * 36 - 3000),
                ["Полезная"] = (2000, 0, -2000 * 18 - 4000 * 18 - 1000 * 3 - 1000 * 6 * 3),
            };

            var built = StoreyFragmentService.Build(db, parent.Id, new StoreyFragmentRequest(Z1, Z2, "Этаж 2"));
            foreach (var d in built.Draft.Diagnostics) output.WriteLine($"{(d.IsError ? "ОШИБКА" : "предупр.")} {d.Code}: {d.Message}");
            var fragment = Assert.IsType<FemSchema>(built.Schema);
            int sid = fragment.Id;

            // Сечения КонЭ — как назначил бы пользователь (у КонЭ из импортной сетки их нет).
            var concrete = MaterialCatalog.CreateHeavyConcrete("B25", CatalogDirectory())!;
            var rebar = MaterialCatalog.CreateRebar("А500С", CatalogDirectory())!;
            db.AddMaterial(concrete);
            db.AddMaterial(rebar);
            var plate = new PlateSection
            {
                Tag = "П200", H = 0.2, ConcreteMaterialId = concrete.Id, RebarMaterialId = rebar.Id,
                RebarLayers =
                [
                    new PlateRebarLayer { Name = "низ", InputMode = "direct", Asx = 5e-4, Asy = 5e-4, Zsx = -0.07, Zsy = -0.07, Face = RebarFace.MinusN },
                    new PlateRebarLayer { Name = "верх", InputMode = "direct", Asx = 5e-4, Asy = 5e-4, Zsx = 0.07, Zsy = 0.07, Face = RebarFace.PlusN },
                ],
            };
            db.SavePlateSection(plate);
            var column = Square(9001, 0.4, concrete);
            db.CrossSections.Add(column);
            var members = db.GetFemMembers(sid);
            foreach (var m in members)
            {
                if (m.ElemType == "beam") m.CrossSectionId = 9001;
                else m.PlateSectionId = plate.Id;
                db.SaveFemMember(m);
            }
            Assert.Contains(members, m => m.ElemType == "beam");
            Assert.Contains(members, m => m.ElemType == "shell" && m.Kind == "wall");
            Assert.Contains(members, m => m.ElemType == "shell" && m.Kind == "plate");

            fragment.MeshPlateStepM = 0.5;
            fragment.MeshBarStepM = 0.5;
            db.UpdateFemSchemaMeshSteps(fragment);
            var service = new FemSchemaMeshService(db, new GmshSettings
            {
                ExecutablePath = Gmsh, ArtifactsPath = Path.Combine(root, "gmsh"), KeepArtifacts = false,
                ElementMode = PlanarMeshElementMode.Quads, Algorithm = 8,
            });
            var meshed = await service.BuildAsync(sid, db.GetFemNodes(sid), db.GetFemMembers(sid), 0.5, null, CancellationToken.None);
            Assert.False(meshed.HasErrors, string.Join("\n", meshed.Diagnostics));
            service.Save(sid, meshed.Mesh!);

            // Временные ГУ до подпроекта D: заделка всех узлов сетки фрагмента на отметках рассечения.
            int fixedNodes = 0;
            foreach (var n in db.GetFemMeshNodes(sid).Where(n => Math.Abs(n.Z - Z1) < Tol || Math.Abs(n.Z - Z2) < Tol))
            {
                db.SetFemMeshNodeBoundary(sid, n.NodeTag, 63, new double[6]);
                fixedNodes++;
            }
            db.LoadAll();
            // Сечение колонн — только в памяти (как в соседних тестах сетки): в cross_sections его нет, LoadAll его сбрасывает.
            if (!db.CrossSections.Contains(column)) db.CrossSections.Add(column);
            output.WriteLine($"Сетка фрагмента: узлов {meshed.Mesh!.Nodes.Count}, КЭ {meshed.Mesh.Elements.Count}, заделано узлов {fixedNodes}");

            foreach (var lc in db.GetFemLoadCases(sid))
            {
                var stage = new FemAnalysisStage
                {
                    Tag = lc.Tag, LoadFactorStep = 1, MaxLoadFactor = 1,
                    LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [lc.Id] }.ToJson(),
                };
                var input = FemCsfeaInputBuilder.Build(db, sid, new FemCsfeaSetup { Stages = [stage] });
                Assert.DoesNotContain(input.Diagnostics, x => x.IsError);
                var adapted = FemRcModelAdapter.Adapt(input);
                Assert.DoesNotContain(adapted.Diagnostics, x => x.IsError);
                var build = RcStructuralMeshBuilder.Build(adapted.Model, new LinearRcSectionFactory());
                var f = build.Combination(adapted.Model.Stages[0].Loads);
                var u = build.Mesh.SolveLinear(f, build.Bc);
                var r = build.Mesh.ComputeReactions(u, build.Bc, fExternal: f);

                double F(int dof) => build.NodeIds.Sum(id => f[build.Dof(id, dof)]);
                double R(int dof) => build.NodeIds.Sum(id => r[build.Dof(id, dof)]);
                var (ex, ey, ez) = expected[lc.Tag];
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{lc.Tag}: ожидаемо Fz {ez / 1e3:0.###} кН, Fx {ex / 1e3:0.###} кН; " +
                    $"модель Fz {F(2) / 1e3:0.###}, Fx {F(0) / 1e3:0.###}, Fy {F(1) / 1e3:0.###} кН; " +
                    $"реакции Rz {R(2) / 1e3:0.###}, Rx {R(0) / 1e3:0.###}, Ry {R(1) / 1e3:0.###} кН"));

                // Нагрузка-поле: КЭ фрагмента берёт q КЭ родителя по своему центру, а свободная сетка Gmsh пересекает
                // границу половин плиты x = 3 — отсюда расхождение в сотые доли процента; допуск — порог проверки
                // равнодействующих построителя (0,5 %). Равновесие реакций — точное.
                double scale = Math.Abs(ez);
                Assert.Equal(ez, F(2), 0.005 * scale);
                Assert.Equal(ex, F(0), 0.005 * scale);
                Assert.Equal(ey, F(1), 0.005 * scale);
                for (int dof = 0; dof < 3; dof++)
                    Assert.Equal(-F(dof), R(dof), 1e-6 * scale);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    static CrossSection Square(int id, double a, Material concrete) => new()
    {
        Id = id, Tag = "К400",
        Areas =
        [
            new MaterialArea
            {
                Tag = "concrete", Category = AreaCategory.Region, MaterialId = concrete.Id,
                Hull = new Contour(new[] { -a / 2, a / 2, a / 2, -a / 2, -a / 2 }, new[] { -a / 2, -a / 2, a / 2, a / 2, -a / 2 }, "outer"),
            },
        ],
    };

    static string CatalogDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCS.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "OpenCS.App", "DataSource");
    }
}
