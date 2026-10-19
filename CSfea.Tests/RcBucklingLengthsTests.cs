using CSfea.CScoreBridge.Structural;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>
/// Энергетический разбор форм и назначение расчётных длин (<see cref="RcBucklingLengths"/>): случаи Эйлера в двух
/// плоскостях, консоль со стойкой-качалкой (μ = 2,695 из tg u = 2u, качалка — μ = 1), элемент вне найденных форм.
/// </summary>
[HarnessChecks]
public class RcBucklingLengthsTests
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
        RunEuler();
        RunLeaningColumn();
        RunNotInvolved();
        RunEnvelope();
    }

    /// <summary>
    /// Шесть консолей h = 4 м с шарнирами вверху, верх связан шарнирными тягами (пример из обсуждения 19.10; у
    /// неразрезного ригеля нагрузка частично перераспределяется, тягами N колонн точны): сочетание 1 — P на
    /// все, сочетание 2 — 1,2P на две крайние. Определяющее для крайних — сочетание 2, где незагруженные держат
    /// загруженные: 2·k(u) + 4·3EI/h³ = 0, k(u) = EI·u³/(h³·(tg u − u)), μ = π/u = 1,1758 — для сочетания 1 (μ = 2) не в
    /// запас. Огибающая (1,2P на крайних, P на остальных одновременно): 2·k(u) + 4·k(u/√1,2) = 0 — крайние μ = 1,8857,
    /// остальные 2,0657; без огибающей сочетание 1 само по себе — μ = 2.
    /// </summary>
    private static void RunEnvelope()
    {
        TestHarness.Section("Расчётные длины: огибающая сжатия против определяющего сочетания");
        const double h = 4.0, p = 1000;
        var col = Section(1e-2, 1e-4, 1e-4);
        var m = new RcStructuralModel();
        for (int i = 0; i < 6; i++)
        {
            m.Nodes.Add(new RcNode(10 + i, 5.0 * i, 0, 0));
            m.Nodes.Add(new RcNode(20 + i, 5.0 * i, 0, h));
            m.Beams.Add(new RcBeam(100 + i, 10 + i, 20 + i, col, new[] { 1.0, 0.0, 0.0 }, 0, My | Mz));
            m.Supports.Add(new RcSupport(10 + i, Fixed));
            // Верх: из плоскости и повороты закреплены (колонна и тяги к узлу шарнирны).
            m.Supports.Add(new RcSupport(20 + i, 0b111010));
            if (i > 0)
                m.Beams.Add(new RcBeam(200 + i, 19 + i, 20 + i, Section(1e-1, 1e-3, 1e-3), new[] { 0.0, 1.0, 0.0 }, My | Mz, My | Mz));
        }
        var all = new RcLoadCase(1, "все");
        for (int i = 0; i < 6; i++) all.Nodal.Add(new RcNodalLoad(20 + i, new[] { 0.0, 0, -p, 0, 0, 0 }));
        var two = new RcLoadCase(2, "две");
        for (int i = 0; i < 2; i++) two.Nodal.Add(new RcNodalLoad(20 + i, new[] { 0.0, 0, -1.2 * p, 0, 0, 0 }));
        m.LoadCases.Add(all);
        m.LoadCases.Add(two);

        IReadOnlyList<int>[] units = Enumerable.Range(0, 6).Select(i => (IReadOnlyList<int>)[100 + i]).ToArray();
        IReadOnlyList<(int, double)>[] combinations = [[(1, 1.0)], [(2, 1.0)]];
        var options = new RcBucklingOptions(Modes: 4);
        var model = RcBucklingAnalysis.Prepare(m, units, options);
        var governing = RcBucklingAnalysis.Governing(model, units, combinations);
        TestHarness.Check("крайняя: определяющее — сочетание 2", governing[0].Combination == 1, $"{governing[0]}");
        TestHarness.Check("средняя: определяющее — сочетание 1", governing[3].Combination == 0, $"{governing[3]}");

        var byGoverning = RcBucklingLengths.Assign(RcBucklingAnalysis.Solve(model, combinations[1], options), units);
        TestHarness.CheckRel("по сочетанию 2: крайняя μ = 1,1758 (не в запас для сочетания 1)", byGoverning[0]!.AboutZ.Mu, 1.17575, 3e-3);
        var alone = RcBucklingLengths.Assign(RcBucklingAnalysis.Solve(model, combinations[0], options), units);
        TestHarness.CheckRel("по сочетанию 1: крайняя μ = 2", alone[0]!.AboutZ.Mu, 2.0, 3e-3);

        var r = RcBucklingAnalysis.SolveEnvelope(model, combinations, 0, options);
        TestHarness.CheckRel("огибающая: N крайней = −1,2P", r.Axial(model.SubBeams[100][0]), -1.2 * p, 1e-6);
        TestHarness.CheckRel("огибающая: N средней = −P", r.Axial(model.SubBeams[103][0]), -p, 1e-6);
        var envelope = RcBucklingLengths.Assign(r, units, local: RcBucklingAnalysis.SolveLocal(r, units));
        TestHarness.CheckRel("огибающая: N элемента крайней = −1,2P", envelope[0]!.N, -1.2 * p, 1e-6);
        TestHarness.CheckRel("огибающая: крайняя μ = 1,8857", envelope[0]!.AboutZ.Mu, 1.88574, 3e-3);
        TestHarness.CheckRel("огибающая: средняя μ = 2,0657", envelope[3]!.AboutZ.Mu, 2.06572, 3e-3);
    }

    private static RcBeamSection Section(double a, double iy, double iz) =>
        new($"A{a} Iy{iy} Iz{iz}") { Elastic = new BeamSection(E, a, iy, iz) };

    /// <summary>
    /// Стойки из одного КЭ (дробятся на 4), Iy = 2·Iz: в каждой плоскости — своя форма, μ — по ней; местная y — по X,
    /// изгиб вокруг z — прогиб по X.
    /// </summary>
    private static void RunEuler()
    {
        TestHarness.Section("Расчётные длины: стойки Эйлера в двух плоскостях");
        const double l = 4.0, iz = 4.8711e-4;
        foreach (var (name, bottom, top, mu) in new[]
        {
            ("шарнир — шарнир", Pin, XY, 1.0),
            ("консоль", Fixed, 0, 2.0),
            ("заделка — шарнир", Fixed, XY, 0.6992),
        })
        {
            var m = new RcStructuralModel();
            m.Nodes.Add(new RcNode(1, 0, 0, 0));
            m.Nodes.Add(new RcNode(2, 0, 0, l));
            m.Beams.Add(new RcBeam(101, 1, 2, Section(2.3695e-2, 2 * iz, iz), new[] { 1.0, 0.0, 0.0 }));
            m.Supports.Add(new RcSupport(1, bottom | (1 << 5)));
            m.Supports.Add(new RcSupport(2, top));
            var lc = new RcLoadCase(1, "P");
            lc.Nodal.Add(new RcNodalLoad(2, new[] { 0.0, 0, -1000, 0, 0, 0 }));
            m.LoadCases.Add(lc);

            var r = RcBucklingAnalysis.Run(m, P, [[101]], new RcBucklingOptions(Modes: 2));
            var x = RcBucklingLengths.Assign(r, [[101]])[0]!;
            TestHarness.CheckRel($"{name}: l", x.Length, l, 1e-12);
            TestHarness.CheckRel($"{name}: N", x.N, -1000, 1e-9);
            TestHarness.Check($"{name}: вокруг z — форма 0", x.AboutZ is { Source: RcBucklingLengthSource.Mode, Mode: 0 }, $"{x.AboutZ}");
            TestHarness.Check($"{name}: вокруг y — форма 1", x.AboutY is { Source: RcBucklingLengthSource.Mode, Mode: 1 }, $"{x.AboutY}");
            TestHarness.CheckRel($"{name}: μ вокруг z", x.AboutZ.Mu, mu, 3e-3);
            TestHarness.CheckRel($"{name}: μ вокруг y", x.AboutY.Mu, mu, 3e-3);
            TestHarness.CheckRel($"{name}: доля работы — весь элемент", x.AboutZ.Share, 1.0, 1e-9);
            TestHarness.CheckRel($"{name}: участие — единственный элемент", x.AboutZ.Participation, 1.0, 1e-9);
            TestHarness.CheckRel($"{name}: EIy = 2·EIz", x.AboutY.EI, 2 * x.AboutZ.EI, 1e-12);
            TestHarness.CheckRel($"{name}: ρ = λ (U = λ·W у единственного КЭ)", x.AboutZ.Rho, x.AboutZ.Lambda, 1e-6);
        }
    }

    /// <summary>
    /// Консоль 101 со стойкой-качалкой 102 (шарниры по концам), верхние узлы связаны шарнирной тягой 103, обе нагружены
    /// P: качалка поворачивается как жёсткое тело (U = 0, источник — качание, μ = 1); консоль держит обе —
    /// tg u / u = (1 + Q/P)/(Q/P) = 2, u = 1,16556, μ = π/u = 2,6954.
    /// </summary>
    private static void RunLeaningColumn()
    {
        TestHarness.Section("Расчётные длины: консоль и стойка-качалка");
        const double h = 4.0;
        var col = Section(1e-2, 1e-4, 1e-4);
        var m = new RcStructuralModel();
        foreach (var (id, nx, z) in new[] { (1, 0.0, 0.0), (2, 0.0, h), (3, 5.0, 0.0), (4, 5.0, h) })
            m.Nodes.Add(new RcNode(id, nx, 0, z));
        m.Beams.Add(new RcBeam(101, 1, 2, col, new[] { 1.0, 0.0, 0.0 }));
        m.Beams.Add(new RcBeam(102, 3, 4, col, new[] { 1.0, 0.0, 0.0 }));
        m.Beams.Add(new RcBeam(103, 2, 4, Section(1e-1, 1e-6, 1e-6), new[] { 0.0, 1.0, 0.0 }, My | Mz, My | Mz));
        m.Supports.Add(new RcSupport(1, Fixed));
        m.Supports.Add(new RcSupport(3, 0b101111));
        m.Supports.Add(new RcSupport(2, 0b101010));
        m.Supports.Add(new RcSupport(4, 0b101010));
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(2, new[] { 0.0, 0, -1000, 0, 0, 0 }));
        lc.Nodal.Add(new RcNodalLoad(4, new[] { 0.0, 0, -1000, 0, 0, 0 }));
        m.LoadCases.Add(lc);

        IReadOnlyList<int>[] units = [[101], [102]];
        var r = RcBucklingAnalysis.Run(m, P, units, new RcBucklingOptions(Modes: 3));
        var x = RcBucklingLengths.Assign(r, units);
        TestHarness.Check("консоль: форма 0", x[0]!.AboutZ is { Source: RcBucklingLengthSource.Mode, Mode: 0 }, $"{x[0]!.AboutZ}");
        TestHarness.CheckRel("консоль: μ = π/u, tg u = 2u", x[0]!.AboutZ.Mu, Math.PI / 1.165561, 5e-3);
        TestHarness.Check("консоль держит: ρ > λ", x[0]!.AboutZ.Rho > x[0]!.AboutZ.Lambda, $"{x[0]!.AboutZ}");
        TestHarness.Check("качалка: качание в форме 0", x[1]!.AboutZ is { Source: RcBucklingLengthSource.Rocking, Mode: 0 }, $"{x[1]!.AboutZ}");
        TestHarness.CheckRel("качалка: μ = 1", x[1]!.AboutZ.Mu, 1.0, 1e-12);
        TestHarness.CheckRel("работа формы — консоль и качалка", x[0]!.AboutZ.Share + x[1]!.AboutZ.Share, 1.0, 1e-6);
    }

    /// <summary>
    /// Две несвязанные шарнирные стойки, у 102 — в 10 раз большая жёсткость: по одной форме 102 не участвует (μ не
    /// назначается), с запасом форм (её λ — после 4λ₁ и 9λ₁ стойки 101) — μ = 1 по своей форме; стойка без сжатия — null.
    /// </summary>
    private static void RunNotInvolved()
    {
        TestHarness.Section("Расчётные длины: элемент вне найденных форм");
        const double l = 3.0;
        var m = new RcStructuralModel();
        foreach (var (id, x, z) in new[] { (1, 0.0, 0.0), (2, 0.0, l), (3, 5.0, 0.0), (4, 5.0, l), (5, 9.0, 0.0), (6, 9.0, l) })
            m.Nodes.Add(new RcNode(id, x, 0, z));
        m.Beams.Add(new RcBeam(101, 1, 2, Section(1e-2, 1e-4, 1e-4), new[] { 1.0, 0.0, 0.0 }));
        m.Beams.Add(new RcBeam(102, 3, 4, Section(1e-2, 1e-3, 1e-3), new[] { 1.0, 0.0, 0.0 }));
        m.Beams.Add(new RcBeam(104, 5, 6, Section(1e-2, 1e-4, 1e-4), new[] { 1.0, 0.0, 0.0 }));
        foreach (int n in new[] { 1, 3, 5 }) m.Supports.Add(new RcSupport(n, Pin | (1 << 5)));
        foreach (int n in new[] { 2, 4, 6 }) m.Supports.Add(new RcSupport(n, XY));
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(2, new[] { 0.0, 0, -1000, 0, 0, 0 }));
        lc.Nodal.Add(new RcNodalLoad(4, new[] { 0.0, 0, -1000, 0, 0, 0 }));
        lc.Nodal.Add(new RcNodalLoad(6, new[] { 0.0, 0, 1000, 0, 0, 0 }));
        m.LoadCases.Add(lc);

        IReadOnlyList<int>[] units = [[101], [102], [104]];
        var model = RcBucklingAnalysis.Prepare(m, units);
        var one = RcBucklingLengths.Assign(RcBucklingAnalysis.Solve(model, P, new RcBucklingOptions(Modes: 1)), units);
        TestHarness.Check("101 — форма 0", one[0]!.AboutZ.Source == RcBucklingLengthSource.Mode || one[0]!.AboutY.Source == RcBucklingLengthSource.Mode);
        TestHarness.Check("102 по одной форме не участвует",
            one[1]!.AboutZ.Source == RcBucklingLengthSource.NotInvolved && double.IsNaN(one[1]!.AboutZ.Mu), $"{one[1]!.AboutZ}");
        TestHarness.Check("растянутая 104 — null", one[2] == null);

        // Формы 101: λ₁ (две плоскости), 4λ₁, 9λ₁; 102 — 10λ₁.
        var all = RcBucklingLengths.Assign(RcBucklingAnalysis.Solve(model, P, new RcBucklingOptions(Modes: 8)), units);
        TestHarness.Check("102 с запасом форм — своя форма", all[1]!.AboutZ.Source == RcBucklingLengthSource.Mode, $"{all[1]!.AboutZ}");
        TestHarness.CheckRel("102: μ = 1", all[1]!.AboutZ.Mu, 1.0, 3e-3);
        TestHarness.CheckRel("101: μ = 1", all[0]!.AboutZ.Mu, 1.0, 3e-3);
    }
}
