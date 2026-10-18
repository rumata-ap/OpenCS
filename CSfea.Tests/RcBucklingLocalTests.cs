using CSfea.CScoreBridge.Structural;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>
/// Локальная задача устойчивости элемента (<see cref="StructuralMesh.SolveLocalBuckling"/>,
/// <see cref="RcBucklingAnalysis.SolveLocal"/>) и её место в назначении расчётных длин (<see cref="RcBucklingLengths"/>):
/// элемент — вся схема (α = λ₁), шарнирная стойка на поперечной пружине (N_cr = min(k·l, π²EI/l²)), рама
/// Бритвина–Айзенбергера (колонна 5: μ = 0,786 при P → 0 … 1,002 при P = 8·10⁶ Н), качалка и неучастник.
/// </summary>
[HarnessChecks]
public class RcBucklingLocalTests
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
        RunWholeStructure();
        RunSpringColumn();
        RunBritvinFrame();
        RunAssign();
    }

    private static RcBeamSection Section(double a, double iy, double iz) =>
        new($"A{a} Iy{iy} Iz{iz}") { Elastic = new BeamSection(E, a, iy, iz) };

    /// <summary>
    /// Стойка — вся схема: локальная задача со схемой совпадает с общей (α₁ = λ₁); без смещения консоль становится
    /// стойкой «заделка — шарнир» (μ = 0,699), шарнирная не меняется (μ = 1).
    /// </summary>
    private static void RunWholeStructure()
    {
        TestHarness.Section("Локальная задача: элемент — вся схема");
        const double l = 4.0, i = 4.8711e-4;
        double pe = Math.PI * Math.PI * E * i / (l * l);
        foreach (var (name, bottom, top, braced) in new[]
        {
            ("шарнир — шарнир", Pin, XY, 1.0),
            ("консоль", Fixed, 0, 0.6992),
            ("заделка — шарнир", Fixed, XY, 0.6992),
        })
        {
            var m = new RcStructuralModel();
            m.Nodes.Add(new RcNode(1, 0, 0, 0));
            m.Nodes.Add(new RcNode(2, 0, 0, l));
            m.Beams.Add(new RcBeam(101, 1, 2, Section(2.3695e-2, i, i), new[] { 1.0, 0.0, 0.0 }));
            m.Supports.Add(new RcSupport(1, bottom | (1 << 5)));
            m.Supports.Add(new RcSupport(2, top));
            var lc = new RcLoadCase(1, "P");
            lc.Nodal.Add(new RcNodalLoad(2, new[] { 0.0, 0, -1000, 0, 0, 0 }));
            m.LoadCases.Add(lc);

            var r = RcBucklingAnalysis.Run(m, P, [[101]], new RcBucklingOptions(Modes: 2));
            var loc = RcBucklingAnalysis.SolveLocal(r, [[101]])[0]!;
            TestHarness.CheckRel($"{name}: α₁ = λ₁", loc.Sway.Alpha[0], r.Lambda[0], 1e-6);
            TestHarness.CheckRel($"{name}: α₂ = λ₂ (вторая плоскость)", loc.Sway.Alpha[1], r.Lambda[1], 1e-6);
            TestHarness.CheckRel($"{name}: без смещения — μ", Math.Sqrt(pe / (loc.Braced.Alpha[0] * 1000)), braced, 3e-3);
            var e = loc.Sway.Energy[0];
            TestHarness.Check($"{name}: форма — одной плоскости", Math.Min(e.Wxy, e.Wxz) < 1e-9 * Math.Max(e.Wxy, e.Wxz), $"{e}");
            TestHarness.CheckRel($"{name}: U = α·W", e.Uxy + e.Uxz, loc.Sway.Alpha[0] * (e.Wxy + e.Wxz), 1e-6);
        }
    }

    /// <summary>
    /// Шарнирная стойка, верх держит поперечная пружина k (вторая плоскость закреплена): с остальной схемой —
    /// поворот как жёсткого тела, N_cr = k·l (k·l = 0,3·P_E); при k·l = 3·P_E — изгиб между концами, N_cr = P_E;
    /// без смещения — P_E в обоих случаях.
    /// </summary>
    private static void RunSpringColumn()
    {
        TestHarness.Section("Локальная задача: стойка на поперечной пружине");
        const double l = 4.0, i = 1e-4;
        double pe = Math.PI * Math.PI * E * i / (l * l);
        foreach (var (name, kl, sway) in new[] { ("слабая пружина", 0.3, 0.3), ("жёсткая пружина", 3.0, 1.0) })
        {
            var m = new RcStructuralModel();
            m.Nodes.Add(new RcNode(1, 0, 0, 0));
            m.Nodes.Add(new RcNode(2, 0, 0, l));
            m.Beams.Add(new RcBeam(101, 1, 2, Section(1e-2, i, i), new[] { 1.0, 0.0, 0.0 }));
            m.Supports.Add(new RcSupport(1, Pin | (1 << 5)));
            m.Supports.Add(new RcSupport(2, 0b000010));
            m.Springs.Add(new RcSpring(2, 0, kl * pe / l));
            var lc = new RcLoadCase(1, "P");
            lc.Nodal.Add(new RcNodalLoad(2, new[] { 0.0, 0, -1000, 0, 0, 0 }));
            m.LoadCases.Add(lc);

            var r = RcBucklingAnalysis.Run(m, P, [[101]], new RcBucklingOptions(Modes: 2));
            var loc = RcBucklingAnalysis.SolveLocal(r, [[101]])[0]!;
            TestHarness.CheckRel($"{name}: N_cr со схемой", loc.Sway.Alpha[0] * 1000, sway * pe, 3e-3);
            TestHarness.CheckRel($"{name}: N_cr без смещения = P_E", loc.Braced.Alpha[0] * 1000, pe, 3e-3);
        }
    }

    /// <summary>
    /// Рама Бритвина–Айзенбергера (IJCCSE 21(4), 2025, рис. 5–8, табл. 2): колонна 5 (средняя нижнего этажа)
    /// по локальной задаче при уровне нагрузки P (остальная схема — с K_σ при P): μ = 0,786 … 1,002; P_cr = 1,005·10⁷ Н.
    /// </summary>
    private static void RunBritvinFrame()
    {
        TestHarness.Section("Локальная задача: рама Бритвина, колонна 5");
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
        // Колонна 5 — стержень 103; 8 КЭ на стержень: ошибка дискретизации N_cr около 0,05 %.
        var model = RcBucklingAnalysis.Prepare(m, units, new RcBucklingOptions(MinSubdivisions: 8));
        // P → 0: в статье α = 3,977 (μ = 0,790), но её же жёсткости остальной схемы в узле B (R_BΔ = 4,507·10⁷,
        // M_BΔ = 6,993·10⁶, M_Bφ = 1,316·10⁸ — совпадают с нашими) в определителе (10) с точными функциями
        // устойчивости дают α = 3,9966, μ = 0,7861; остальные уровни — табл. 2 (округление до 0,001).
        foreach (var (p, mu, tol) in new[] { (1.0, 0.7861, 5e-4), (2.5e6, 0.827, 2e-3), (5e6, 0.884, 2e-3), (8e6, 1.002, 2e-3) })
        {
            var r = RcBucklingAnalysis.Solve(model, [(1, p)], new RcBucklingOptions(Modes: 1));
            if (p > 1) TestHarness.CheckRel($"P = {p:g3} Н: P_cr", r.Lambda[0] * p, 1.005e7, 5e-3);
            var loc = RcBucklingAnalysis.SolveLocal(r, units, [2])[2]!;
            double n = model.Axial(103, r.URef).Min();
            double ncr = -loc.Sway.Alpha[0] * n;
            TestHarness.CheckRel($"P = {p:g3} Н: μ колонны 5", Math.PI / 3 * Math.Sqrt(E * 4.8711e-4 / ncr), mu, tol);
        }
    }

    /// <summary>
    /// Назначение с локальными задачами: качалка — N_cr без смещения (шарнирная, μ = 1), консоль — по форме (μ = 2,695,
    /// локальная без смещения выше); неучастник — N_cr со схемой (μ = 1); правило min — на заданной локальной форме
    /// без смещения ниже λ·N.
    /// </summary>
    private static void RunAssign()
    {
        TestHarness.Section("Локальная задача в назначении расчётных длин");
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
        var x = RcBucklingLengths.Assign(r, units, local: RcBucklingAnalysis.SolveLocal(r, units));
        TestHarness.Check("консоль: форма 0", x[0]!.AboutZ is { Source: RcBucklingLengthSource.Mode, Mode: 0, LocalBraced: true }, $"{x[0]!.AboutZ}");
        TestHarness.CheckRel("консоль: μ = π/u, tg u = 2u", x[0]!.AboutZ.Mu, Math.PI / 1.165561, 5e-3);
        TestHarness.Check("консоль: без смещения выше λ·N", x[0]!.AboutZ.LocalNcr > x[0]!.AboutZ.Ncr, $"{x[0]!.AboutZ}");
        TestHarness.Check("качалка: качание", x[1]!.AboutZ is { Source: RcBucklingLengthSource.Rocking, Mode: 0 }, $"{x[1]!.AboutZ}");
        TestHarness.CheckRel("качалка: N_cr — без смещения", x[1]!.AboutZ.Ncr, x[1]!.AboutZ.LocalNcr, 1e-12);
        TestHarness.CheckRel("качалка: μ = 1 по локальной задаче", x[1]!.AboutZ.Mu, 1.0, 3e-3);

        // Две несвязанные шарнирные стойки: по одной форме 102 не участвует — N_cr со схемой, μ = 1.
        const double l = 3.0;
        m = new RcStructuralModel();
        foreach (var (id, nx, z) in new[] { (1, 0.0, 0.0), (2, 0.0, l), (3, 5.0, 0.0), (4, 5.0, l) })
            m.Nodes.Add(new RcNode(id, nx, 0, z));
        m.Beams.Add(new RcBeam(101, 1, 2, Section(1e-2, 1e-4, 1e-4), new[] { 1.0, 0.0, 0.0 }));
        m.Beams.Add(new RcBeam(102, 3, 4, Section(1e-2, 1e-3, 1e-3), new[] { 1.0, 0.0, 0.0 }));
        foreach (int nd in new[] { 1, 3 }) m.Supports.Add(new RcSupport(nd, Pin | (1 << 5)));
        foreach (int nd in new[] { 2, 4 }) m.Supports.Add(new RcSupport(nd, XY));
        lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(2, new[] { 0.0, 0, -1000, 0, 0, 0 }));
        lc.Nodal.Add(new RcNodalLoad(4, new[] { 0.0, 0, -1000, 0, 0, 0 }));
        m.LoadCases.Add(lc);
        units = [[101], [102]];
        r = RcBucklingAnalysis.Run(m, P, units, new RcBucklingOptions(Modes: 1));
        x = RcBucklingLengths.Assign(r, units, local: RcBucklingAnalysis.SolveLocal(r, units, [1]));
        TestHarness.Check("102: локальная со схемой", x[1]!.AboutZ is { Source: RcBucklingLengthSource.Local, Mode: -1, LocalBraced: false }, $"{x[1]!.AboutZ}");
        TestHarness.CheckRel("102: μ = 1", x[1]!.AboutZ.Mu, 1.0, 3e-3);
        TestHarness.CheckRel("102: μ = 1 во второй плоскости", x[1]!.AboutY.Mu, 1.0, 3e-3);

        // Правило min: шарнирная стойка, Iy = 2·Iz (вокруг z — форма 0, вокруг y — форма 1); заданная локальная форма
        // без смещения в плоскости xy с α = λ₀/2.
        m = new RcStructuralModel();
        m.Nodes.Add(new RcNode(1, 0, 0, 0));
        m.Nodes.Add(new RcNode(2, 0, 0, l));
        m.Beams.Add(new RcBeam(101, 1, 2, Section(1e-2, 2e-4, 1e-4), new[] { 1.0, 0.0, 0.0 }));
        m.Supports.Add(new RcSupport(1, Pin | (1 << 5)));
        m.Supports.Add(new RcSupport(2, XY));
        lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(2, new[] { 0.0, 0, -1000, 0, 0, 0 }));
        m.LoadCases.Add(lc);
        r = RcBucklingAnalysis.Run(m, P, [[101]], new RcBucklingOptions(Modes: 2));
        var half = new StructuralLocalModes([0.5 * r.Lambda[0]], [new BeamModeEnergy(1, 1, 0, 0)]);
        x = RcBucklingLengths.Assign(r, [[101]], local: [new StructuralLocalBuckling(half, half)]);
        TestHarness.Check("min: вокруг z — локальная без смещения", x[0]!.AboutZ is { Source: RcBucklingLengthSource.Local, Mode: 0, LocalBraced: true }, $"{x[0]!.AboutZ}");
        TestHarness.CheckRel("min: N_cr = α·N", x[0]!.AboutZ.Ncr, 0.5 * r.Lambda[0] * 1000, 1e-9);
        TestHarness.Check("min: вокруг y — по форме 1", x[0]!.AboutY is { Source: RcBucklingLengthSource.Mode, Mode: 1 }, $"{x[0]!.AboutY}");    }
}
