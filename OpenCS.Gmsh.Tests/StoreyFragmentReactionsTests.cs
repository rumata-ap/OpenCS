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
/// отметкам 1,5…4,5 м → сечения КонЭ → «Построить сетку» (Gmsh) → ГУ разрезов пресетом «Изоляция» → линейное
/// решение по каждому загружению. Сумма реакций равна сумме нагрузок полосы, посчитанной вручную по нагрузкам родителя.
/// </summary>
public sealed class StoreyFragmentReactionsTests(ITestOutputHelper output)
{
    const double Z1 = 1.5, Z2 = 4.5, Tol = 1e-6;

    static string S(int v) => StoreyFragmentGmshFixture.S(v);

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
        await StoreyFragmentGmshFixture.InTempRoot("opencs-fragment-reactions", async root =>
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
            var sections = StoreyFragmentGmshFixture.AddSections(db);
            var members = db.GetFemMembers(sid);
            foreach (var m in members)
            {
                if (m.ElemType == "beam") m.CrossSectionId = sections.Column.Id;
                else m.PlateSectionId = sections.Plate.Id;
                db.SaveFemMember(m);
            }
            Assert.Contains(members, m => m.ElemType == "beam");
            Assert.Contains(members, m => m.ElemType == "shell" && m.Kind == "wall");
            Assert.Contains(members, m => m.ElemType == "shell" && m.Kind == "plate");

            var meshed = await StoreyFragmentGmshFixture.Mesh(db, fragment, 0.5, root);

            // ГУ разрезов без масок родителя — пресет «Изоляция»: низ — шарнир, верх — шарнир со свободным Uz.
            db.SetFragmentBcPreset(fragment, "Isolation");
            int cutNodes = db.GetFemMeshNodes(sid).Count(n => Math.Abs(n.Z - Z1) < Tol || Math.Abs(n.Z - Z2) < Tol);
            db.LoadAll();
            StoreyFragmentGmshFixture.Restore(db, sections);
            output.WriteLine($"Сетка фрагмента: узлов {meshed.Mesh!.Nodes.Count}, КЭ {meshed.Mesh.Elements.Count}, узлов на разрезах {cutNodes}");

            foreach (var lc in db.GetFemLoadCases(sid))
            {
                var stage = StoreyFragmentGmshFixture.Single(lc.Tag, lc.Id);
                var input = FemCsfeaInputBuilder.Build(db, sid, new FemCsfeaSetup { Stages = [stage] });
                Assert.DoesNotContain(input.Diagnostics, x => x.IsError);
                Assert.Equal(cutNodes, input.Supports.Count(x => x.Origin == FemCsfeaInputBuilder.FragmentCutOrigin));
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
        });
    }
}
