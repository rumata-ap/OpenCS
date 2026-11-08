using CSfea.CScoreBridge.Structural;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>
/// Теория второго порядка «только P-δ» (срез 1а спеки второго порядка фрагмента, <see cref="RcLocalPDelta"/>,
/// <see cref="StructuralMesh.SetLocalSecondOrder"/>): K_σ вертикальных КЭ по перемещениям без жёсткого сдвига участка
/// между уровнями. Сверки: колонна с заданными концами (LocalOnly = Full по перемещениям, реакции сдвига — на P·Δ/L);
/// двухэтажная колонна с пружиной в узле перекрытия против функций устойчивости; шарнирная колонна с поперечной
/// нагрузкой (LocalOnly = Full = точное решение); крен гасится; уровни стержней и стен, отказ без линии уровня.
/// </summary>
[HarnessChecks]
public class LocalPDeltaTests
{
    private const double E = 30e9, Side = 0.4;
    private static readonly double Inertia = Math.Pow(Side, 4) / 12, Ei = E * Inertia, Area = Side * Side;
    private static readonly double[] Dx = [1.0, 0.0, 0.0];
    private const int Fixed = 0x3F, PinnedBase = 0b100111, PinnedTop = 0b100011;

    private static readonly RcBeamSection Section = new("колонна")
    {
        Elastic = new BeamSection(E, Area, Inertia, Inertia, 2 * Inertia),
    };

    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("Колонна с заданными концами: LocalOnly = Full, ΔRx = P·Δ/L");
        RunBothEndsPrescribed();

        TestHarness.Section("Двухэтажная колонна с пружиной в узле перекрытия");
        RunTwoStoreySpring();

        TestHarness.Section("Шарнирная колонна с поперечной нагрузкой: LocalOnly = Full");
        RunPinnedTransverse();

        TestHarness.Section("Крен в LocalOnly гасится");
        RunSwayImperfection();

        TestHarness.Section("Уровни: ригель, стена-трапеция, стена с заданными торцами");
        RunLevels();
        RunWallNoLevelLine();
        RunWallBothEndsPrescribed();
    }

    // ---------------- общее ----------------

    private static RcSecantOptions Options(bool local, Action<string>? log = null) => new()
    {
        PhysicallyLinear = true, LocalPDelta = local,
        Solver = new SecantPicardOptions { Geometric = true, GeometricSecondOrder = true, TolDisplacement = 1e-11, Log = log },
    };

    private static RcSecantStepFields? Solve(string name, RcStructuralModel m, bool local)
    {
        var run = RcSecantAnalysis.Run(m, Options(local));
        TestHarness.Check($"{name}: расчёт сошёлся", run.Result.Completed, run.Result.Message ?? "");
        return run.Result.Completed ? run.LastConvergedFields() : null;
    }

    private static double Disp(RcSecantStepFields f, int node, int c)
        => f.Displacements[RcSecantStepFields.NodeComponents * Array.IndexOf(f.NodeIds, node) + c];

    private static double Reaction(RcSecantStepFields f, int node, int c)
    {
        int k = Array.IndexOf(f.ReactionNodeIds, node);
        return k < 0 ? 0.0 : f.Reactions[6 * k + c];
    }

    private static double[] Moments(RcSecantStepFields f)
        => Enumerable.Range(0, f.BeamIds.Length)
            .SelectMany(e => new[] { 4, 5, 10, 11 }.Select(c => f.BeamForces[e * RcSecantStepFields.BeamForceComponents + c]))
            .ToArray();

    private static double Diff(double[] a, double[] b, double? scale = null)
    {
        double s = scale ?? b.Max(Math.Abs);
        return a.Zip(b, (x, y) => Math.Abs(x - y)).Max() / Math.Max(s, 1e-300);
    }

    /// <summary>Вертикальная колонна из <paramref name="n"/> КЭ высотой <paramref name="h"/> (узлы 0…n снизу вверх).</summary>
    private static RcStructuralModel Column(int n, double h)
    {
        var m = new RcStructuralModel();
        for (int i = 0; i <= n; i++) m.Nodes.Add(new RcNode(i, 0, 0, h * i / n));
        for (int i = 0; i < n; i++) m.Beams.Add(new RcBeam(100 + i, i, i + 1, Section, Dx));
        return m;
    }

    // ---------------- проверки ----------------

    /// <summary>
    /// Колонна 8 КЭ, L = 6 м, низ заделан, верх: ux = Δ = L/100 и укорочение δ = P·L/EA заданы, прочие DOF закреплены
    /// (P = 0,5·π²EI/L²). Участок один, оба конца заданы: перемещения и моменты LocalOnly = Full до 1e−6; реакция Rx верха
    /// LocalOnly − Full = P·Δ/L, заделки — −P·Δ/L; моменты заделки равны.
    /// </summary>
    private static void RunBothEndsPrescribed()
    {
        const int n = 8;
        const double l = 6.0;
        double p = 0.5 * Math.PI * Math.PI * Ei / (l * l), delta = l / 100;
        RcStructuralModel Model()
        {
            var m = Column(n, l);
            m.Supports.Add(new RcSupport(0, Fixed));
            m.Supports.Add(new RcSupport(n, 0b111010));
            var lc = new RcLoadCase(1, "Δ, δ");
            lc.Prescribed.Add(new RcPrescribed(n, 0, delta));
            lc.Prescribed.Add(new RcPrescribed(n, 2, -p * l / (E * Area)));
            m.LoadCases.Add(lc);
            m.Stages.Add(new RcStage("Δ", [(1, 1.0)], 3));
            return m;
        }
        var full = Solve("Full", Model(), local: false);
        var loc = Solve("LocalOnly", Model(), local: true);
        if (full == null || loc == null) return;
        TestHarness.CheckLess("перемещения LocalOnly = Full", Diff(loc.Displacements, full.Displacements), 1e-6);
        TestHarness.CheckLess("моменты концов КЭ LocalOnly = Full", Diff(Moments(loc), Moments(full)), 1e-6);
        double pd = p * delta / l;
        TestHarness.CheckRel("Rx верха: LocalOnly − Full = P·Δ/L", Reaction(loc, n, 0) - Reaction(full, n, 0), pd, 1e-6);
        TestHarness.CheckRel("Rx заделки: LocalOnly − Full = −P·Δ/L", Reaction(loc, 0, 0) - Reaction(full, 0, 0), -pd, 1e-6);
        TestHarness.CheckRel("My заделки: LocalOnly = Full", Reaction(loc, 0, 4), Reaction(full, 0, 4), 1e-6);
        TestHarness.CheckRel("Rz заделки = P", Reaction(loc, 0, 2), p, 1e-6);
    }

    /// <summary>
    /// Две колонны-этажа по 8 КЭ, h = 3 м: низ заделан, верх закреплён (кроме uz — заданное укорочение, сжатие P =
    /// 0,5·π²EI/h²), узел перекрытия посередине свободен, пружина k = 12EI/h³ по x, сила H по x. Поворот узла по
    /// симметрии 0 — каждый этаж «заделка — ползун»: S(P) = EI·k³·sin kh/(2(1 − cos kh) − kh·sin kh) (с членом −P/h).
    /// LocalOnly: Δ = H/(k + 2·(S + P/h)); Full: Δ = H/(k + 2·S) — до 0,5 %.
    /// </summary>
    private static void RunTwoStoreySpring()
    {
        const int n = 8;
        const double h = 3.0;
        double p = 0.5 * Math.PI * Math.PI * Ei / (h * h), kSpring = 12 * Ei / (h * h * h), force = 100e3;
        RcStructuralModel Model()
        {
            var m = Column(2 * n, 2 * h);
            m.Supports.Add(new RcSupport(0, Fixed));
            m.Supports.Add(new RcSupport(2 * n, 0b111011));
            m.Springs.Add(new RcSpring(n, 0, kSpring));
            var lc = new RcLoadCase(1, "P, H");
            lc.Prescribed.Add(new RcPrescribed(2 * n, 2, -p * 2 * h / (E * Area)));
            lc.Nodal.Add(new RcNodalLoad(n, [force, 0, 0, 0, 0, 0]));
            m.LoadCases.Add(lc);
            m.Stages.Add(new RcStage("P", [(1, 1.0)], 3));
            return m;
        }
        double kh = Math.Sqrt(p / Ei) * h, kk = kh / h;
        double s = Ei * kk * kk * kk * Math.Sin(kh) / (2 * (1 - Math.Cos(kh)) - kh * Math.Sin(kh));
        var full = Solve("Full", Model(), local: false);
        var loc = Solve("LocalOnly", Model(), local: true);
        if (full == null || loc == null) return;
        TestHarness.CheckRel("Full: Δ = H/(k + 2S)", Disp(full, n, 0), force / (kSpring + 2 * s), 0.005);
        TestHarness.CheckRel("LocalOnly: Δ = H/(k + 2(S + P/h))", Disp(loc, n, 0), force / (kSpring + 2 * (s + p / h)), 0.005);
        TestHarness.CheckLess("поворот узла перекрытия ≈ 0 (доля Δ/h)", Math.Abs(Disp(loc, n, 4)) / (Disp(loc, n, 0) / h), 1e-6);
    }

    /// <summary>
    /// Шарнирная колонна 8 КЭ, L = 6 м, сжатие P = 0,5·P_cr силой на верху, поперечная q по x. Концы не смещаются:
    /// LocalOnly = Full (перемещения и реакции до 1e−6); прогиб середины — точное решение
    /// 5qL⁴/(384EI)·12(2 sec u − 2 − u²)/(5u⁴), u = kL/2, — до 0,5 %.
    /// </summary>
    private static void RunPinnedTransverse()
    {
        const int n = 8;
        const double l = 6.0, q = 20e3;
        double p = 0.5 * Math.PI * Math.PI * Ei / (l * l);
        RcStructuralModel Model()
        {
            var m = Column(n, l);
            m.Supports.Add(new RcSupport(0, PinnedBase));
            m.Supports.Add(new RcSupport(n, PinnedTop));
            var lc = new RcLoadCase(1, "P, q");
            lc.Nodal.Add(new RcNodalLoad(n, [0, 0, -p, 0, 0, 0]));
            foreach (var b in m.Beams) lc.Beams.Add(new RcBeamLoad(b.Id, [q, 0, 0]));
            m.LoadCases.Add(lc);
            m.Stages.Add(new RcStage("P", [(1, 1.0)], 3));
            return m;
        }
        var full = Solve("Full", Model(), local: false);
        var loc = Solve("LocalOnly", Model(), local: true);
        if (full == null || loc == null) return;
        TestHarness.CheckLess("перемещения LocalOnly = Full", Diff(loc.Displacements, full.Displacements), 1e-6);
        var rl = new[] { 0, n }.SelectMany(k => Enumerable.Range(0, 6).Select(c => Reaction(loc, k, c))).ToArray();
        var rf = new[] { 0, n }.SelectMany(k => Enumerable.Range(0, 6).Select(c => Reaction(full, k, c))).ToArray();
        TestHarness.CheckLess("реакции LocalOnly = Full (доля qL)", Diff(rl, rf, q * l), 1e-6);
        double u = Math.Sqrt(p / Ei) * l / 2;
        double y = 5 * q * Math.Pow(l, 4) / (384 * Ei) * 12 * (2 / Math.Cos(u) - 2 - u * u) / (5 * Math.Pow(u, 4));
        TestHarness.CheckRel("прогиб середины = точное решение", Disp(loc, n / 2, 0), y, 0.005);
    }

    /// <summary>
    /// Шарнирная колонна, сжатие P, крен φ = 1/200 по x без поперечной нагрузки: Full — реакции Rx концов ±P·φ (P-Δ крена),
    /// LocalOnly — 0 (крен линеен по высоте участка и проекцией гасится).
    /// </summary>
    private static void RunSwayImperfection()
    {
        const int n = 8;
        const double l = 6.0, phi = 1.0 / 200;
        double p = 0.3 * Math.PI * Math.PI * Ei / (l * l);
        RcStructuralModel Model()
        {
            var m = Column(n, l);
            m.Supports.Add(new RcSupport(0, PinnedBase));
            m.Supports.Add(new RcSupport(n, PinnedTop));
            var lc = new RcLoadCase(1, "P");
            lc.Nodal.Add(new RcNodalLoad(n, [0, 0, -p, 0, 0, 0]));
            m.LoadCases.Add(lc);
            m.Stages.Add(new RcStage("P", [(1, 1.0)], 2));
            m.Imperfections.Add(new RcSway(phi, Dx));
            return m;
        }
        var full = Solve("крен, Full", Model(), local: false);
        var loc = Solve("крен, LocalOnly", Model(), local: true);
        if (full == null || loc == null) return;
        TestHarness.CheckRel("Full: |Rx заделки| = P·φ", Math.Abs(Reaction(full, 0, 0)), p * phi, 1e-6);
        TestHarness.CheckLess("LocalOnly: |Rx| концов ≈ 0 (доля P·φ)",
            Math.Max(Math.Abs(Reaction(loc, 0, 0)), Math.Abs(Reaction(loc, n, 0))) / (p * phi), 1e-6);
    }

    /// <summary>Колонна 8 КЭ с ригелем в среднем узле: два участка; ригель без хорды (без K_σ).</summary>
    private static void RunLevels()
    {
        var m = Column(8, 6.0);
        m.Nodes.Add(new RcNode(50, 3.0, 0, 3.0));
        m.Beams.Add(new RcBeam(200, 4, 50, Section, [0, 0, 1]));
        var r = RcLocalPDelta.Analyze(m);
        TestHarness.Check("ригель: участков 2", r.BarSegments == 2, $"{r.BarSegments}");
        TestHarness.Check("ригель: вертикальных стержней 8", r.Bars == 8, $"{r.Bars}");
        TestHarness.Check("ригель без хорды", r.Chords[^1] == null, "есть");
        var c = r.Chords[0]!;
        TestHarness.Check("нижний КЭ: хорда 0 → 4", c.Bottom[0].Nodes[0] == 0 && c.Top[0].Nodes[0] == 4,
            $"{c.Bottom[0].Nodes[0]} → {c.Top[0].Nodes[0]}");
        TestHarness.CheckRel("нижний КЭ: ζ конца J = 1/4", c.Zeta[1], 0.25, 1e-12);
    }

    // Стена в плоскости xz (Q4, nx × nz), узлы Id(i, k); ширина ряда k — от x0(k) до x1(k).
    private static RcStructuralModel Wall(int nx, int nz, double hgt, Func<int, (double X0, double X1)> row, RcShellSection sec)
    {
        var m = new RcStructuralModel();
        int Id(int i, int k) => k * (nx + 1) + i;
        for (int k = 0; k <= nz; k++)
        {
            var (x0, x1) = row(k);
            for (int i = 0; i <= nx; i++) m.Nodes.Add(new RcNode(Id(i, k), x0 + (x1 - x0) * i / nx, 0, hgt * k / nz));
        }
        for (int k = 0; k < nz; k++)
            for (int i = 0; i < nx; i++)
                m.Shells.Add(new RcShell(k * nx + i + 1, [Id(i, k), Id(i + 1, k), Id(i + 1, k + 1), Id(i, k + 1)], sec));
        return m;
    }

    private static readonly RcShellSection WallSection = new("стена")
    {
        Elastic = new Laminate([new Ply(new OrthotropicMaterial(E, E, 0.2, E / 2.4), 0, 0.2)]),
    };

    /// <summary>
    /// Стена-трапеция (низ 0…1 м, верх 0,25…0,75 м): у узлов нижних рядов у края нет точки верхней линии уровня — отказ
    /// (<see cref="RcLocalPDeltaResult.NoLineShells"/>, <see cref="InvalidOperationException"/> расчёта).
    /// </summary>
    private static void RunWallNoLevelLine()
    {
        const int nx = 4, nz = 6;
        var m = Wall(nx, nz, 3.0, k => (0.25 * k / nz, 1 - 0.25 * k / nz), WallSection);
        var r = RcLocalPDelta.Analyze(m);
        TestHarness.Check("трапеция: есть оболочки без линии уровня", r.HasErrors, "нет");
        for (int i = 0; i <= nx; i++) m.Supports.Add(new RcSupport(i, Fixed));
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(nz * (nx + 1) + nx / 2, [0, 0, -1e3, 0, 0, 0]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("P", [(1, 1.0)], 1));
        bool thrown = false;
        try { RcSecantAnalysis.Run(m, Options(local: true)); }
        catch (InvalidOperationException ex) { thrown = ex.Message.Contains("линии уровня", StringComparison.Ordinal); }
        TestHarness.Check("трапеция: расчёт отказан «нет линии уровня»", thrown, "без отказа");
    }

    /// <summary>
    /// Стена-пилон 1 × 6 м (Q4 4 × 24) в плоскости xz: низ заделан, uy закреплены; верху всех узлов заданы ux = Δ = H/200,
    /// укорочение (сжатие P = 0,5·P_cr консоли) и повороты 0. Один участок, оба конца заданы: перемещения LocalOnly ≈ Full
    /// (1e−5 — N стены у заделки неравномерны; факт 1,2e−6), ΣRx верха LocalOnly − Full ≈ P·Δ/L (0,5 %; факт 0,015 %).
    /// </summary>
    private static void RunWallBothEndsPrescribed()
    {
        const int nx = 4, nz = 24;
        const double b = 1.0, t = 0.2, l = 6.0;
        double pcr = Math.PI * Math.PI * E * t * b * b * b / 12 / (4 * l * l), p = 0.5 * pcr, delta = l / 200;
        int Id(int i, int k) => k * (nx + 1) + i;
        RcStructuralModel Model()
        {
            var m = Wall(nx, nz, l, _ => (0, b), WallSection);
            for (int i = 0; i <= nx; i++) m.Supports.Add(new RcSupport(Id(i, 0), Fixed));
            for (int k = 1; k < nz; k++)
                for (int i = 0; i <= nx; i++) m.Supports.Add(new RcSupport(Id(i, k), 0b000010));
            var lc = new RcLoadCase(1, "Δ, δ");
            for (int i = 0; i <= nx; i++)
            {
                m.Supports.Add(new RcSupport(Id(i, nz), 0b111010));
                lc.Prescribed.Add(new RcPrescribed(Id(i, nz), 0, delta));
                lc.Prescribed.Add(new RcPrescribed(Id(i, nz), 2, -p * l / (E * t * b)));
            }
            m.LoadCases.Add(lc);
            m.Stages.Add(new RcStage("Δ", [(1, 1.0)], 3));
            return m;
        }
        var full = Solve("стена, Full", Model(), local: false);
        var loc = Solve("стена, LocalOnly", Model(), local: true);
        if (full == null || loc == null) return;
        TestHarness.CheckLess("стена: перемещения LocalOnly ≈ Full", Diff(loc.Displacements, full.Displacements), 1e-5);
        double TopRx(RcSecantStepFields f) => Enumerable.Range(0, nx + 1).Sum(i => Reaction(f, Id(i, nz), 0));
        double TopRz(RcSecantStepFields f) => -Enumerable.Range(0, nx + 1).Sum(i => Reaction(f, Id(i, nz), 2));
        double pActual = TopRz(loc);
        TestHarness.CheckRel("стена: ΣRx LocalOnly − Full = P·Δ/L", TopRx(loc) - TopRx(full), pActual * delta / l, 0.005);
    }
}
