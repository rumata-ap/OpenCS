using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using CScore.Fem.Loads;
using CScore.Submodel;
using Xunit;
using static CScore.Tests.Fem.Fragment.Masks.FragmentMaskFixtures;

namespace CScore.Tests.Fem.Fragment.Masks;

public sealed class ImportedParentResultTests
{
    static readonly Dictionary<int, int> Map = new() { [1] = 101 };
    static readonly FragmentMaskKey K = FragmentMaskKey.OfLoadCase(101);

    static Dictionary<string, FemMeshNode> Nodes(params (string Tag, double X, double Y, double Z)[] p) =>
        p.ToDictionary(n => n.Tag, n => new FemMeshNode { NodeTag = n.Tag, X = n.X, Y = n.Y, Z = n.Z }, StringComparer.Ordinal);

    /// <summary>
    /// Консоль вдоль X длиной L (оси программы: Z1 вверх, Y1 = +Y — поворот −90° от конвенции OpenCS), заделка в узле 1,
    /// на конце сила P вниз. Знаки ЛИРЫ: My = −P·(L − x) (растянут верх),
    /// Qz = dMy/dx = +P. Сила узла на КЭ: у конца (0, 0, −P), у заделки (0, 0, +P) и момент Ry = −P·L (равновесие КЭ).
    /// </summary>
    [Fact]
    public void Cantilever_LiraSigns()
    {
        const double L = 4, P = 10e3;
        var nodes = Nodes(("1", 0, 0, 0), ("2", L, 0, 0));
        var elements = new List<FemElement> { new() { ElemTag = "7", ElemType = "beam", NodeIdsJson = "[1,2]", BeamRotationDeg = -90 } };
        var snapshot = new ImportedResultSnapshot
        {
            Program = ImportedResultSnapshot.ProgramLira,
            Loads = [new ImportedResultLoad { Num = 1, BarNums = [7], BarForces = [0, 0, P, 0, -P * L, 0, 0, 0, P, 0, 0, 0] }],
        };
        var r = new ImportedParentResult(snapshot, Map, nodes, elements);

        Assert.True(r.TryBeamEndForces(K, "7", out var f));
        Assert.Equal(P, f.I.Z, 9);
        Assert.Equal(-P * L, f.I.Ry, 9);
        Assert.Equal(-P, f.J.Z, 9);
        Assert.Equal(0, f.J.Ry, 9);
        Assert.Empty(r.BeamLoads(K, "7"));
        var (ry, rz, bars) = r.ShearConsistency();
        Assert.Null(ry);
        Assert.Equal(1, rz!.Value, 9);
        Assert.Equal(1, bars);
    }

    /// <summary>Балка под равномерной нагрузкой q вниз, шарниры: нагрузка КЭ восстанавливается.</summary>
    [Fact]
    public void SimpleBeam_UniformLoadRecovered()
    {
        const double L = 6, q = 5e3;
        var nodes = Nodes(("1", 0, 0, 0), ("2", L, 0, 0));
        var elements = new List<FemElement> { new() { ElemTag = "3", ElemType = "beam", NodeIdsJson = "[1,2]", BeamRotationDeg = -90 } };
        // Знаки ЛИРЫ: My(x) = q·x·(L − x)/2 ≥ 0 (растянут низ), Qz = dMy/dx: +qL/2 у узла 1, −qL/2 у узла 2.
        var snapshot = new ImportedResultSnapshot
        {
            Program = ImportedResultSnapshot.ProgramScad,
            Loads = [new ImportedResultLoad { Num = 1, BarNums = [3], BarForces = [0, 0, q * L / 2, 0, 0, 0, 0, 0, -q * L / 2, 0, 0, 0] }],
        };
        var r = new ImportedParentResult(snapshot, Map, nodes, elements);

        var load = Assert.Single(r.BeamLoads(K, "3"));
        Assert.Equal(-q, load.QA.Z, 9);
        Assert.Equal(-q, load.QB.Z, 9);
        // Симметрия: средняя Q и градиент момента нулевые — согласованность не определена.
        Assert.Null(r.ShearConsistency().Rz);
    }

    /// <summary>Перевёрнутые знаки Q в снимке дают согласованность −1.</summary>
    [Fact]
    public void ShearConsistency_FlippedShear()
    {
        const double L = 4, P = 10e3;
        var nodes = Nodes(("1", 0, 0, 0), ("2", L, 0, 0));
        var elements = new List<FemElement> { new() { ElemTag = "7", ElemType = "beam", NodeIdsJson = "[1,2]", BeamRotationDeg = -90 } };
        var snapshot = new ImportedResultSnapshot
        {
            Program = ImportedResultSnapshot.ProgramLira,
            Loads = [new ImportedResultLoad { Num = 1, BarNums = [7], BarForces = [0, -P, -P, 0, -P * L, -P * L, 0, -P, -P, 0, 0, 0] }],
        };
        var (ry, rz, _) = new ImportedParentResult(snapshot, Map, nodes, elements).ShearConsistency();
        Assert.Equal(-1, ry!.Value, 9);
        Assert.Equal(-1, rz!.Value, 9);
    }

    /// <summary>Оси выдачи пластины: угол 90° — X выдачи вдоль Y1 КЭ; моменты инвертируются, N и Q — нет.</summary>
    [Fact]
    public void Shell_AxesAndSigns()
    {
        var (nodes, elements) = Wall(1, 1, 1, 1);
        elements[0].LocalAxisAngleDeg = 90;
        var snapshot = new ImportedResultSnapshot
        {
            Program = ImportedResultSnapshot.ProgramLira,
            Loads = [new ImportedResultLoad { Num = 1, ShellNums = [1], ShellForces = [1, 2, 3, 4, 5, 6, 7, 8] }],
        };
        var r = new ImportedParentResult(snapshot, Map, nodes, elements);

        Assert.True(r.TryShellForces(K, "1", out var s));
        var (_, y1, z1) = FemElementGeometry.Of(elements[0], nodes)!.ShellFrame();
        Assert.True((s.AxisX - y1).Length < 1e-12);
        Assert.True((s.AxisX.Cross(s.AxisY) - z1).Length < 1e-12);
        Assert.Equal([1, 2, 3, -4, -5, -6, 7, 8], s.Values);
    }

    /// <summary>Нет пары загружения, угла пластины, поворота стержня, узла — данных нет (false), а не ноль.</summary>
    [Fact]
    public void MissingData_IsAbsent()
    {
        var nodes = Nodes(("1", 0, 0, 0), ("2", 0, 0, 3));
        var (wallNodes, wall) = Wall(1, 1, 1, 1, tagBase: 10);
        foreach (var (tag, n) in wallNodes) nodes[tag] = n;
        var elements = new List<FemElement>(wall) { new() { ElemTag = "5", ElemType = "beam", NodeIdsJson = "[1,2]" } };
        var snapshot = new ImportedResultSnapshot
        {
            Program = ImportedResultSnapshot.ProgramScad,
            Loads =
            [
                new ImportedResultLoad
                {
                    Num = 1, NodeNums = [1], Displacements = [0, 0, -1e-3, 0, 0, 0],
                    BarNums = [5], BarForces = new double[12], ShellNums = [11], ShellForces = new double[8],
                },
                new ImportedResultLoad { Num = 2 },
            ],
        };
        var r = new ImportedParentResult(snapshot, Map, nodes, elements);

        Assert.Equal([K], r.Keys);
        Assert.Equal([2], r.UnmatchedLoads);
        Assert.Equal(["5"], r.BarsWithoutAxes);
        Assert.Equal(["11"], r.ShellsWithoutAxes);
        Assert.True(r.TryNodeDisplacement(K, "1", out var u));
        Assert.Equal(-1e-3, u.Z);
        Assert.False(r.TryNodeDisplacement(K, "2", out _));
        Assert.False(r.TryNodeDisplacement(FragmentMaskKey.OfLoadCase(102), "1", out _));
        Assert.False(r.TryBeamEndForces(K, "5", out _));
        Assert.False(r.TryShellForces(K, "11", out _));
    }

    /// <summary>
    /// Стена со сжатием, изгибом и сдвигом, оси выдачи повёрнуты на 30°: маски из снимка «в знаках программы» совпадают
    /// с масками из тех же полей в конвенции OpenCS.
    /// </summary>
    [Fact]
    public void WallMasks_MatchOpenCsConvention()
    {
        var (nodes, elements) = Wall(3, 3, 6, 6, flip: true);
        foreach (var e in elements) e.LocalAxisAngleDeg = 30;
        var table = new FragmentParentResultTable();
        table.AddKey(K);
        var nodeNums = new List<int>();
        var disp = new List<double>();
        foreach (var (tag, n) in nodes)
        {
            var u = new Dof6(1e-5 * n.Z, 2e-5 * n.X, -1e-4 * n.Z, 1e-6, -2e-6, 3e-6 * n.X);
            table.SetDisplacement(K, tag, u);
            nodeNums.Add(int.Parse(tag));
            disp.AddRange([u.X, u.Y, u.Z, u.Rx, u.Ry, u.Rz]);
        }
        var shellNums = new List<int>();
        var shellF = new List<double>();
        double[] signs = ImportedForceConvention.LiraScad.Shell;
        foreach (var e in elements)
        {
            var g = FemElementGeometry.Of(e, nodes)!;
            var (x1, _, n) = g.ShellFrame();
            double t = Math.PI / 6;
            var x = (x1 * Math.Cos(t) + n.Cross(x1) * Math.Sin(t)).Normalize();
            var y = n.Cross(x).Normalize();
            // Усилия КЭ в его осях выдачи (конвенция OpenCS); снимок — те же числа в знаках программы.
            double cx = g.Centroid.X;
            double[] open = [-500 + 30 * cx, -2000 - 100 * cx, 300, 50, 120 + 10 * cx, -20, 40, -15 + 5 * cx];
            table.SetShellForces(K, e.ElemTag, new FragmentShellForces(x, y, open));
            shellNums.Add(int.Parse(e.ElemTag));
            shellF.AddRange(open.Select((v, k) => v / signs[k]));
        }
        var snapshot = new ImportedResultSnapshot
        {
            Program = ImportedResultSnapshot.ProgramScad,
            Loads = [new ImportedResultLoad
            {
                Num = 1, NodeNums = [.. nodeNums], Displacements = [.. disp], ShellNums = [.. shellNums], ShellForces = [.. shellF],
            }],
        };
        var imported = new ImportedParentResult(snapshot, Map, nodes, elements);
        FragmentMaskCutInput cut = new(new FragmentCut("W", FragmentCutKind.Edge, FragmentCutSide.Bottom, 0.8, null,
            [(V(0, 0, 0.8), V(3, 0, 0.8))]), null, V(0, 1, 0));

        FragmentMaskInput Input(IFragmentParentResult result) => new()
        {
            Cuts = [cut], ParentNodes = nodes, ParentElements = elements, Result = result,
        };
        var a = Assert.Single(FragmentMaskExtractor.Extract(Input(table)).Masks);
        var b = Assert.Single(FragmentMaskExtractor.Extract(Input(imported)).Masks);

        Assert.Equal(a.Samples.Count, b.Samples.Count);
        double scale = a.Samples.Max(p => p.F!.MaxForceAbs);
        for (int i = 0; i < a.Samples.Count; i++)
        {
            Assert.Equal(a.Samples[i].S, b.Samples[i].S, 12);
            for (int k = 0; k < 6; k++)
            {
                Assert.Equal(a.Samples[i].U![k], b.Samples[i].U![k], 15);
                Assert.True(Math.Abs(a.Samples[i].F![k] - b.Samples[i].F![k]) <= 1e-9 * scale);
            }
        }
    }
}
