using CSfea.CScoreBridge.Structural;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>
/// Модель устойчивости из схемы (<see cref="RcBucklingAnalysis"/>): дробление расчётных элементов, перенос шарниров
/// и пролётных нагрузок на подэлементы, рама Бритвина–Айзенбергера с одним КЭ на стержень.
/// </summary>
[HarnessChecks]
public class RcBucklingAnalysisTests
{
    private const double E = 2.0594e11;

    // Местные DOF конца: My, Mz.
    private const int My = 1 << 4, Mz = 1 << 5;

    // Маски опор: X Y Z RX RY RZ — биты 0–5.
    private const int Pin = 0b000111, Fixed = 0b111111, XY = 0b000011;

    private static readonly (int, double)[] P = [(1, 1.0)];

    [Fact]
    public static void RunAll()
    {
        RunDivisions();
        RunSubdivideLoads();
        RunSingleElementColumn();
        RunReleasedSingleElement();
        RunBritvinFrame();
    }

    private static RcBeamSection Section(double a, double iy, double iz) =>
        new($"A{a} Iy{iy} Iz{iz}") { Elastic = new BeamSection(E, a, iy, iz) };

    /// <summary>Стойка длиной <paramref name="l"/> из одного КЭ 101, сила −1 Н по z вверху.</summary>
    private static RcStructuralModel Column(double l, RcBeamSection s, int bottom, int top)
    {
        var m = new RcStructuralModel();
        m.Nodes.Add(new RcNode(1, 0, 0, 0));
        m.Nodes.Add(new RcNode(2, 0, 0, l));
        m.Beams.Add(new RcBeam(101, 1, 2, s, new[] { 1.0, 0.0, 0.0 }));
        m.Supports.Add(new RcSupport(1, bottom | (1 << 5)));
        m.Supports.Add(new RcSupport(2, top));
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(2, new[] { 0.0, 0, -1, 0, 0, 0 }));
        m.LoadCases.Add(lc);
        return m;
    }

    private static void RunDivisions()
    {
        TestHarness.Section("Дробление: число частей стержней");
        var d = RcBucklingAnalysis.Divisions([[1], [2, 3], [3, 4, 5, 6, 7]], 4);
        TestHarness.Check("1 КЭ → 4", d[1] == 4, $"{d[1]}");
        TestHarness.Check("2 КЭ → по 2", d[2] == 2, $"{d[2]}");
        TestHarness.Check("общий стержень — наибольшее", d[3] == 2, $"{d[3]}");
        TestHarness.Check("5 КЭ ≥ 4 — не дробится", d[7] == 1, $"{d[7]}");
    }

    /// <summary>
    /// Шарнирная балка 6 м: погонная нагрузка на каждом подэлементе даёт точный прогиб середины 5qL⁴/384EI; концевые
    /// силы пролётной нагрузки переносятся на концы крайних подэлементов с сохранением равнодействующей.
    /// </summary>
    private static void RunSubdivideLoads()
    {
        TestHarness.Section("Дробление: шарниры и пролётные нагрузки");
        const double l = 6.0, q = -1.0e4, i = 2.1959e-4;
        var m = new RcStructuralModel();
        m.Nodes.Add(new RcNode(1, 0, 0, 0));
        m.Nodes.Add(new RcNode(2, l, 0, 0));
        m.Beams.Add(new RcBeam(7, 1, 2, Section(1.6521e-2, i, i), new[] { 0.0, 1.0, 0.0 }, My, Mz));
        m.Supports.Add(new RcSupport(1, Fixed));
        m.Supports.Add(new RcSupport(2, 0b111110));
        var lc = new RcLoadCase(1, "q");
        lc.Beams.Add(new RcBeamLoad(7, new[] { 0.0, 0, q }));
        lc.BeamEnds.Add(new RcBeamEndLoad(7, new[] { 0.0, 0, -100, 0, 50, 0, 0, 0, -300, 0, -70, 0 }));
        m.LoadCases.Add(lc);

        var (sub, map) = RcBucklingAnalysis.Subdivide(m, new Dictionary<int, int> { [7] = 4 });
        TestHarness.Check("4 подэлемента от I к J", map[7].SequenceEqual([8, 9, 10, 11]), string.Join(",", map[7]));
        TestHarness.Check("узлы 3–5 на долях пролёта",
            sub.Nodes.Where(n => n.Id > 2).Select(n => n.X).SequenceEqual([1.5, 3.0, 4.5]));
        TestHarness.Check("шарниры только на крайних",
            sub.Beams[0].ReleaseI == My && sub.Beams[0].ReleaseJ == 0 && sub.Beams[3].ReleaseI == 0
            && sub.Beams[3].ReleaseJ == Mz && sub.Beams[1].ReleaseI + sub.Beams[1].ReleaseJ == 0);
        TestHarness.Check("погонная — на каждом", sub.LoadCases[0].Beams.Count == 4);
        var ends = sub.LoadCases[0].BeamEnds;
        TestHarness.Check("концевые — на крайних", ends.Count == 2 && ends[0].BeamId == 8 && ends[1].BeamId == 11
            && ends[0].Forces[2] == -100 && ends[0].Forces[8] == 0 && ends[1].Forces[8] == -300 && ends[1].Forces[2] == 0);

        // Прогиб середины от одной погонной нагрузки; изгиб в плоскости xz — шарниры по My на обоих концах.
        lc.BeamEnds.Clear();
        m.Beams[0] = m.Beams[0] with { ReleaseJ = My };
        (sub, _) = RcBucklingAnalysis.Subdivide(m, new Dictionary<int, int> { [7] = 4 });
        var build = RcStructuralMeshBuilder.Build(sub, new LinearRcSectionFactory());
        var u = build.Mesh.SolveLinear(build.Combination(P), build.Bc);
        double exact = 5 * q * Math.Pow(l, 4) / (384 * E * i);
        TestHarness.CheckRel("прогиб середины = 5qL⁴/384EI", u[build.Dof(4, 2)], exact, 1e-9);
    }

    private static void RunSingleElementColumn()
    {
        TestHarness.Section("Устойчивость: стойка из одного КЭ, дробление на 4");
        const double l = 4.0, i = 4.8711e-4;
        double pe = Math.PI * Math.PI * E * i / (l * l);
        foreach (var (name, bottom, top, mu) in new[]
        {
            ("шарнир — шарнир", Pin, XY, 1.0),
            ("консоль", Fixed, 0, 2.0),
            ("заделка — шарнир", Fixed, XY, 0.6992),
        })
        {
            var r = RcBucklingAnalysis.Run(Column(l, Section(2.3695e-2, i, i), bottom, top), P, [[101]],
                new RcBucklingOptions(Modes: 1));
            TestHarness.Check($"{name}: сошлось", r.Converged && r.Lambda.Length == 1);
            TestHarness.Check($"{name}: 4 подэлемента", r.SubBeams[101].Length == 4);
            TestHarness.CheckRel($"{name}: μ", Math.Sqrt(pe / r.Lambda[0]), mu, 3e-3);
        }
    }

    /// <summary>
    /// Шарниры моделями в одном КЭ (узлы закреплены полностью, КЭ освобождён по My, Mz с обоих концов): без дробления
    /// изгиб КЭ исключён целиком — форм нет; с дроблением шарниры остаются на концах, μ ≈ 1.
    /// </summary>
    private static void RunReleasedSingleElement()
    {
        TestHarness.Section("Устойчивость: один КЭ с шарнирами по концам");
        const double l = 5.0, i = 2.1959e-4;
        var m = Column(l, Section(1.6521e-2, i, i), Fixed, 0b111011);
        m.Beams[0] = m.Beams[0] with { ReleaseI = My | Mz, ReleaseJ = My | Mz };
        double pe = Math.PI * Math.PI * E * i / (l * l);
        var whole = RcBucklingAnalysis.Run(m, P, [], new RcBucklingOptions(Modes: 1));
        TestHarness.Check("без дробления форм нет", whole.Lambda.Length == 0, $"{whole.Lambda.Length}");
        var r = RcBucklingAnalysis.Run(m, P, [[101]], new RcBucklingOptions(Modes: 1));
        TestHarness.Check("с дроблением есть форма", r.Lambda.Length == 1);
        TestHarness.CheckRel("μ", Math.Sqrt(pe / r.Lambda[0]), 1.0, 1e-2);
    }

    /// <summary>
    /// Рама Бритвина–Айзенбергера (IJCCSE 21(4), 2025, рис. 5) с одним КЭ на стержень: расчётные элементы — стержни,
    /// дробление на 4 восстанавливает P_cr = 1,005·10⁷ Н.
    /// </summary>
    private static void RunBritvinFrame()
    {
        TestHarness.Section("Устойчивость: рама Бритвина, 1 КЭ на стержень");
        var col = Section(2.3695e-2, 4.8711e-4, 4.8711e-4);
        var beam = Section(1.6521e-2, 2.1959e-4, 2.1959e-4);
        var m = new RcStructuralModel();
        var ids = new Dictionary<(double, double), int>();
        int Node(double x, double z)
        {
            if (ids.TryGetValue((x, z), out int id)) return id;
            id = m.Nodes.Count + 1;
            m.Nodes.Add(new RcNode(id, x, 0, z));
            return ids[(x, z)] = id;
        }
        void Member(double x0, double z0, double x1, double z1, RcBeamSection s) =>
            m.Beams.Add(new RcBeam(101 + m.Beams.Count, Node(x0, z0), Node(x1, z1), s, new[] { 0.0, 1.0, 0.0 }));
        foreach (double x in new[] { 0.0, 6, 12 })
        {
            Member(x, 0, x, 3, col);
            Member(x, 3, x, 6, col);
        }
        foreach (double z in new[] { 3.0, 6 })
        {
            Member(0, z, 6, z, beam);
            Member(6, z, 12, z, beam);
        }
        foreach (var node in m.Nodes)
            m.Supports.Add(new RcSupport(node.Id, node.Z == 0 ? Fixed : 0b101010));
        var lc = new RcLoadCase(1, "P");
        foreach (double z in new[] { 3.0, 6 })
        {
            lc.Nodal.Add(new RcNodalLoad(Node(0, z), new[] { 5.0, 0, -1, 0, 0, 0 }));
            lc.Nodal.Add(new RcNodalLoad(Node(6, z), new[] { 0.0, 0, -2, 0, 0, 0 }));
            lc.Nodal.Add(new RcNodalLoad(Node(12, z), new[] { 0.0, 0, -1, 0, 0, 0 }));
        }
        m.LoadCases.Add(lc);
        var units = m.Beams.Select(b => (IReadOnlyList<int>)[b.Id]).ToList();
        var r = RcBucklingAnalysis.Run(m, P, units, new RcBucklingOptions(Modes: 3));
        TestHarness.Check("сошлось", r.Converged);
        TestHarness.Check("каждый стержень — 4 подэлемента", r.SubBeams.Values.All(v => v.Length == 4),
            $"стержней сетки {r.Build.BeamIds.Length}");
        TestHarness.CheckRel("P_cr", r.Lambda[0], 1.005e7, 5e-3);
    }
}
