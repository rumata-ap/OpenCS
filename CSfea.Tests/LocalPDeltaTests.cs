using CSfea.CScoreBridge.Structural;
using CSfea.Core;
using CSfea.Sparse;

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
        RunWallSlabByRigidBodies();
        RunWallSkewedShell();
        RunWallBothEndsPrescribed();

        TestHarness.Section("Поблочные силы хорд: Σ ElementChordForces = ChordForce + ChordImperfectionForce");
        RunElementChordForces();
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
    /// Стена-трапеция (низ 0…1 м, верх 0,4…0,6 м): у узлов нижних рядов у края до верхней линии уровня дальше ширины КЭ —
    /// отказ (<see cref="RcLocalPDeltaResult.NoLineShells"/>, <see cref="InvalidOperationException"/> расчёта). Пологая
    /// трапеция (верх 0,1…0,9 м — выступ меньше ширины КЭ, как парапет «Музея») — без отказа, угловой узел низа берёт конец
    /// верхней линии.
    /// </summary>
    private static void RunWallNoLevelLine()
    {
        const int nx = 4, nz = 6;
        var mild = RcLocalPDelta.Analyze(Wall(nx, nz, 3.0, k => (0.1 * k / nz, 1 - 0.1 * k / nz), WallSection));
        TestHarness.Check("пологая трапеция: без отказа", !mild.HasErrors, $"{mild.NoLineShells.Count}");
        TestHarness.Check("пологая трапеция: узел 0 → конец верхней линии", mild.Chords[0]!.Top[0].Nodes is [var t0] && t0 == nz * (nx + 1),
            string.Join(",", mild.Chords[0]!.Top[0].Nodes));
        var m = Wall(nx, nz, 3.0, k => (0.4 * k / nz, 1 - 0.4 * k / nz), WallSection);
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
    /// Стена 1 × 6 м (Q4 4 × 6) и плита на отметке 3 м в 50 мм от плоскости стены (импорт: «Музей», стена x = 18,35): плита
    /// связана с узлами стены на отметке 3 м АЖТ, общих узлов нет. С АЖТ — уровень 3 м есть (три уровня, хорда нижнего КЭ
    /// 0 → 3 м, предупреждений нет); без АЖТ — хорда через всю высоту 0 → 6 м и предупреждение о пропущенном перекрытии
    /// (<see cref="RcLocalPDeltaResult.SkippedLevelShells"/>) у всех КЭ стены.
    /// </summary>
    private static void RunWallSlabByRigidBodies()
    {
        const int nx = 4, nz = 6;
        RcStructuralModel Model(bool rigid)
        {
            var m = Wall(nx, nz, 6.0, _ => (0, 1), WallSection);
            int slab0 = 1000;
            for (int j = 0; j < 3; j++)
                for (int i = 0; i <= nx; i++) m.Nodes.Add(new RcNode(slab0 + j * (nx + 1) + i, 1.0 * i / nx, 0.05 + 0.5 * j, 3.0));
            for (int j = 0; j < 2; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = slab0 + j * (nx + 1) + i;
                    m.Shells.Add(new RcShell(500 + j * nx + i, [a, a + 1, a + nx + 2, a + nx + 1], WallSection));
                }
            if (rigid)
                for (int i = 0; i <= nx; i++) m.RigidBodies.Add(new RcRigidBody(i + 1, slab0 + i, [3 * (nx + 1) + i]));
            return m;
        }
        var with = RcLocalPDelta.Analyze(Model(rigid: true));
        TestHarness.Check("плита через АЖТ: уровней стены 3", with.WallLevels == 3, $"{with.WallLevels}");
        TestHarness.CheckRel("плита через АЖТ: хорда нижнего КЭ 3 м", with.Chords[0]!.Length, 3.0, 1e-12);
        TestHarness.Check("плита через АЖТ: без предупреждений", with.SkippedLevelShells.Count == 0, $"{with.SkippedLevelShells.Count}");
        var without = RcLocalPDelta.Analyze(Model(rigid: false));
        TestHarness.CheckRel("плита без связи: хорда 6 м", without.Chords[0]!.Length, 6.0, 1e-12);
        TestHarness.Check("плита без связи: предупреждение у всех КЭ стены", without.SkippedLevelShells.Count == nx * nz,
            $"{without.SkippedLevelShells.Count}");
    }

    /// <summary>
    /// Стена 1 × 3 м (Q4 4 × 6), у углового КЭ верхний угловой узел смещён из плоскости так, что нормаль КЭ отклонена на
    /// 1…2° («Музей», 124384: 1,2°): КЭ остаётся в стене (допуск компланарности 2°) — без отказа, хорда на высоту стены.
    /// </summary>
    private static void RunWallSkewedShell()
    {
        const int nx = 4, nz = 6;
        var m = Wall(nx, nz, 3.0, _ => (0, 1), WallSection);
        int corner = nz * (nx + 1) + nx;
        var c = m.Nodes[corner];
        m.Nodes[corner] = c with { Y = 0.012 };
        double[] P(int id) => [m.Nodes[id].X, m.Nodes[id].Y, m.Nodes[id].Z];
        var ids = m.Shells[^1].NodeIds;
        var nrm = new double[3];
        for (int k = 0; k < 4; k++)
        {
            var a = P(ids[k]); var b = P(ids[(k + 1) % 4]);
            nrm[0] += (a[1] - b[1]) * (a[2] + b[2]); nrm[1] += (a[2] - b[2]) * (a[0] + b[0]); nrm[2] += (a[0] - b[0]) * (a[1] + b[1]);
        }
        double tilt = Math.Acos(Math.Abs(nrm[1]) / Math.Sqrt(nrm[0] * nrm[0] + nrm[1] * nrm[1] + nrm[2] * nrm[2])) * 180 / Math.PI;
        TestHarness.Check("перекошенный КЭ: отклонение 1…2°", tilt is > 1 and < 2, $"{tilt:0.###}°");
        var r = RcLocalPDelta.Analyze(m);
        TestHarness.Check("перекошенный КЭ: без отказа", !r.HasErrors, $"{r.NoLineShells.Count}");
        TestHarness.CheckRel("перекошенный КЭ: хорда 3 м", r.Chords[m.Shells.Count - 1]!.Length, 3.0, 1e-12);
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

    /// <summary>
    /// Поблочные силы хорд (<see cref="StructuralMesh.ElementChordForces"/>) по произвольным u: колонна с ригелем (погибь,
    /// крен) и стена (крен). Σ по КЭ без поля = <see cref="StructuralMesh.ChordForce"/>, с полем — плюс
    /// <see cref="StructuralMesh.ChordImperfectionForce"/> (1e−12 от max); у КЭ без хорды пусто; узлы блока вне КЭ
    /// (<see cref="StructuralMesh.ChordNodes"/>) — ровно узлы DOF блока, не входящие в КЭ.
    /// </summary>
    private static void RunElementChordForces()
    {
        var col = Column(8, 6.0);
        col.Nodes.Add(new RcNode(50, 3.0, 0, 3.0));
        col.Beams.Add(new RcBeam(200, 4, 50, Section, [0, 0, 1]));
        col.Imperfections.Add(new RcSway(1.0 / 200, Dx));
        col.Imperfections.Add(new RcBow([100, 101, 102, 103], 0.01, [0, 1, 0]));
        var wall = Wall(4, 6, 3.0, _ => (0, 1), WallSection);
        wall.Imperfections.Add(new RcSway(1.0 / 150, [0, 1, 0]));
        foreach (var (name, m) in new[] { ("колонна с ригелем", col), ("стена", wall) })
        {
            var build = RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());
            var mesh = build.Mesh;
            mesh.SetLocalSecondOrder(RcLocalPDelta.Analyze(m).Chords);
            var field = build.Imperfection(m)!;
            var rnd = new Random(7);
            var u = Enumerable.Range(0, mesh.NDof).Select(_ => 1e-3 * (rnd.NextDouble() - 0.5)).ToArray();
            foreach (bool inPlane in new[] { true, false })
            {
                var sum = new double[mesh.NDof];
                var sumImp = new double[mesh.NDof];
                bool nodesOk = true, emptyOk = true;
                for (int e = 0; e < mesh.ElementCount; e++)
                {
                    foreach (var (d, f) in mesh.ElementChordForces(e, u, u, inPlane)) sum[d] += f;
                    var withField = mesh.ElementChordForces(e, u, u, inPlane, field);
                    foreach (var (d, f) in withField) sumImp[d] += f;
                    if (!mesh.HasGeometric(e)) emptyOk &= withField.Count == 0 && mesh.ChordNodes(e).Count == 0;
                    var own = mesh.ElementDofs(e).Select(x => x / 6).ToHashSet();
                    var expected = withField.Select(x => x.Dof / 6).Where(n => !own.Contains(n)).Distinct().Order();
                    nodesOk &= expected.SequenceEqual(mesh.ChordNodes(e).Order());
                }
                var cf = mesh.ChordForce(u, u, inPlane);
                var ci = Dense.AddV(cf, mesh.ChordImperfectionForce(field, u, inPlane));
                TestHarness.Check($"{name} (inPlane={inPlane}): поправки ненулевые", cf.Max(Math.Abs) > 0, "нули");
                TestHarness.CheckLess($"{name} (inPlane={inPlane}): Σ поблочных = ChordForce", Diff(sum, cf), 1e-12);
                TestHarness.CheckLess($"{name} (inPlane={inPlane}): Σ с полем = ChordForce + ChordImperfectionForce",
                    Diff(sumImp, ci), 1e-12);
                TestHarness.Check($"{name}: у КЭ без хорды пусто", emptyOk, "есть силы");
                TestHarness.Check($"{name}: ChordNodes = узлы блока вне КЭ", nodesOk, "не совпали");
            }
            TestHarness.Check($"{name}: узлы вне КЭ есть", Enumerable.Range(0, mesh.ElementCount).Any(e => mesh.ChordNodes(e).Count > 0),
                "нет");
        }
    }
}
