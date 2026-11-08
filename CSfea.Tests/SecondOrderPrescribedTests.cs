using CSfea.CScoreBridge.Structural;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>
/// Секущий расчёт по теории второго порядка с ненулевыми заданными перемещениями (срез 1 спеки второго порядка
/// фрагмента): (K + K_σ)·ū переносится в правую часть (<see cref="StructuralMesh.SolveSecondOrder"/>). Сверки: консоль
/// со сдвигом верха против функций устойчивости; заданный сдвиг ≡ сила; погибь с кинематическим сжатием; стена-пилон со
/// сдвигом верха против CR-Ньютона; выше критической — отказ «потеря устойчивости». Упругие сечения, без физической
/// нелинейности.
/// </summary>
[HarnessChecks]
public class SecondOrderPrescribedTests
{
    private const double E = 30e9, L = 6.0, Side = 0.4;
    private static readonly double Inertia = Math.Pow(Side, 4) / 12, Ei = E * Inertia, Area = Side * Side;
    private static readonly double[] Dx = [1.0, 0.0, 0.0];
    private const int N = 8, Fixed = 0x3F, Uy = 0b000010, PinnedBase = 0b100111, PinnedTop = 0b100011;

    private static readonly RcBeamSection Section = new("колонна")
    {
        Elastic = new BeamSection(E, Area, Inertia, Inertia, 2 * Inertia),
    };

    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("Консоль со сдвигом верха: H = P·k·Δ/(tg kL − kL)");
        RunCantileverShift(0.5);
        RunCantileverShift(2.0);
        RunCantileverVanishingLoad();

        TestHarness.Section("Заданный сдвиг ≡ сила H");
        RunShiftEqualsForce();

        TestHarness.Section("Погибь при сжатии заданным укорочением");
        RunBowWithPrescribedShortening();

        TestHarness.Section("Стена-пилон в своей плоскости: второй порядок против CR");
        RunPierAgainstCr();

        TestHarness.Section("Выше критической — потеря устойчивости");
        RunAboveCritical();
    }

    // ---------------- модели ----------------

    private static RcStructuralModel Column(int baseMask, int topMask)
    {
        var m = new RcStructuralModel();
        for (int i = 0; i <= N; i++) m.Nodes.Add(new RcNode(i, 0, 0, L * i / N));
        for (int i = 0; i < N; i++) m.Beams.Add(new RcBeam(100 + i, i, i + 1, Section, Dx));
        m.Supports.Add(new RcSupport(0, baseMask));
        if (topMask != 0) m.Supports.Add(new RcSupport(N, topMask));
        return m;
    }

    /// <summary>Загружение 1: сила вниз P и сила H по X на верхе, заданное ux верха (null — нет); стадия в 3 шага.</summary>
    private static RcStructuralModel Cantilever(double p, double? shift, double h = 0.0)
    {
        var m = Column(Fixed, Uy);
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(N, [h, 0.0, -p, 0.0, 0.0, 0.0]));
        if (shift is { } d) lc.Prescribed.Add(new RcPrescribed(N, 0, d));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("P", [(1, 1.0)], 3));
        return m;
    }

    private static RcSecantOptions Options(bool secondOrder = true, Action<string>? log = null) => new()
    {
        PhysicallyLinear = true,
        Solver = new SecantPicardOptions
        {
            Geometric = true, GeometricSecondOrder = secondOrder, TolDisplacement = 1e-10, GeometricTolerance = 1e-10,
            Log = log,
        },
    };

    private static RcSecantStepFields? Solve(string name, RcStructuralModel m, RcSecantOptions? o = null)
    {
        var run = RcSecantAnalysis.Run(m, o ?? Options());
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

    private static double PcrCantilever => Math.PI * Math.PI * Ei / (4 * L * L);

    // ---------------- проверки ----------------

    /// <summary>
    /// Консоль 8 КЭ, заделка внизу, верху задан сдвиг Δ = L/100 (поворот свободен), сила P = ratio·P_cr консоли:
    /// H = P·k·Δ/(tg kL − kL), M₀ = H·L + P·Δ, k = √(P/EI) — до 0,5 %. При ratio = 2 (за критической силой консоли, но
    /// ниже подпёртой) H &lt; 0: боковая жёсткость отрицательна, равновесие держит только заданный сдвиг.
    /// </summary>
    private static void RunCantileverShift(double ratio)
    {
        double p = ratio * PcrCantilever, delta = L / 100;
        var f = Solve($"P = {ratio}·P_cr,конс", Cantilever(p, delta));
        if (f == null) return;
        double kl = Math.Sqrt(p / Ei) * L;
        double h = p * (kl / L) * delta / (Math.Tan(kl) - kl);
        double m0 = h * L + p * delta;
        TestHarness.CheckRel($"P = {ratio}·P_cr: ux верха = Δ", Disp(f, N, 0), delta, 1e-12);
        TestHarness.CheckRel($"P = {ratio}·P_cr: Rx верха = H", Reaction(f, N, 0), h, 0.005);
        TestHarness.CheckRel($"P = {ratio}·P_cr: Rx заделки = −H", Reaction(f, 0, 0), -h, 0.005);
        TestHarness.CheckRel($"P = {ratio}·P_cr: |M заделки| = |H·L + P·Δ|", Math.Abs(Reaction(f, 0, 4)), Math.Abs(m0), 0.005);
        TestHarness.CheckRel($"P = {ratio}·P_cr: Rz заделки = P", Reaction(f, 0, 2), p, 1e-9);
        if (ratio > 1) TestHarness.Check($"P = {ratio}·P_cr: H < 0", h < 0 && Reaction(f, N, 0) < 0, $"{Reaction(f, N, 0):e3}");
    }

    /// <summary>P = 10⁻⁶·P_cr: H = 3EIΔ/L³ (консоль первого порядка).</summary>
    private static void RunCantileverVanishingLoad()
    {
        double delta = L / 100;
        var f = Solve("P → 0", Cantilever(1e-6 * PcrCantilever, delta));
        if (f == null) return;
        TestHarness.CheckRel("P → 0: Rx верха = 3EIΔ/L³", Reaction(f, N, 0), 3 * Ei * delta / (L * L * L), 1e-5);
    }

    /// <summary>
    /// P = 0,5·P_cr консоли: сдвиг Δ заданный ≡ сила H (реакция расчёта со сдвигом) на свободном верху — перемещения,
    /// моменты концов КЭ и реакции заделки до 1e−6.
    /// </summary>
    private static void RunShiftEqualsForce()
    {
        double p = 0.5 * PcrCantilever, delta = L / 100;
        var fs = Solve("сдвиг задан", Cantilever(p, delta));
        if (fs == null) return;
        double h = Reaction(fs, N, 0);
        var ff = Solve("сила H", Cantilever(p, null, h));
        if (ff == null) return;
        TestHarness.CheckRel("сила H: ux верха = Δ", Disp(ff, N, 0), delta, 1e-6);
        TestHarness.CheckLess("перемещения", Diff(fs.Displacements, ff.Displacements), 1e-6);
        TestHarness.CheckLess("моменты концов КЭ", Diff(Moments(fs), Moments(ff)), 1e-6);
        var r0s = Enumerable.Range(0, 6).Select(c => Reaction(fs, 0, c)).ToArray();
        var r0f = Enumerable.Range(0, 6).Select(c => Reaction(ff, 0, c)).ToArray();
        TestHarness.CheckLess("реакции заделки (доля P)", Diff(r0s, r0f, p), 1e-6);
    }

    /// <summary>
    /// Шарнирная колонна 8 КЭ, погибь e₀ = L/250, сжатие заданным укорочением верха δ = P·L/EA (P = 0,5·P_cr) —
    /// кинематический торец фрагмента: полный прогиб середины 8e₀/(kL)²·(sec(kL/2) − 1) — до 0,5 %, Rz верха = P.
    /// </summary>
    private static void RunBowWithPrescribedShortening()
    {
        double pcr = Math.PI * Math.PI * Ei / (L * L), p = 0.5 * pcr, e0 = L / 250;
        var m = Column(PinnedBase, PinnedTop);
        var lc = new RcLoadCase(1, "δ");
        lc.Prescribed.Add(new RcPrescribed(N, 2, -p * L / (E * Area)));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("δ", [(1, 1.0)], 5));
        m.Imperfections.Add(new RcBow(m.Beams.Select(b => b.Id).ToList(), e0, Dx));
        var f = Solve("погибь, укорочение задано", m);
        if (f == null) return;
        double kl = Math.Sqrt(p / Ei) * L;
        double yExact = 8 * e0 / (kl * kl) * (1 / Math.Cos(kl / 2) - 1);
        TestHarness.CheckRel("Rz верха = −P", Reaction(f, N, 2), -p, 1e-6);
        TestHarness.CheckRel("y середины = 8e₀/(kL)²·(sec(kL/2) − 1)", e0 + Disp(f, N / 2, 0), yExact, 0.005);
    }

    /// <summary>
    /// Стена-пилон 1 × 6 м (t = 0,2, Q4 4 × 24) в плоскости xz, низ заделан, uy закреплены; верху ux = Δ = H/200 у всех
    /// узлов, сжатие P = 0,5·P_cr консоли (EI в плоскости) по верхнему ребру. ΣRx верха против балки
    /// P·k·Δ/(tg kL − kL) — до 1,5 %; против CR-Ньютона — сдвиг середины высоты до 1 %, ΣRx до 5 % (CR в плоскости
    /// грубее, см. ниже); сдвиг заданный ≡ узловые силы-реакции — перемещения до 1e−6.
    /// </summary>
    private static void RunPierAgainstCr()
    {
        const double b = 1.0, t = 0.2;
        const int nx = 4, nz = 24;
        var sec = new RcShellSection("пилон") { Elastic = new Laminate([new Ply(new OrthotropicMaterial(E, E, 0.2, E / 2.4), 0, t)]) };
        double pcr = Math.PI * Math.PI * E * t * b * b * b / 12 / (4 * L * L), delta = L / 200;
        static int Id(int i, int k) => k * (nx + 1) + i;
        RcStructuralModel Pier(double p)
        {
            var m = new RcStructuralModel();
            for (int k = 0; k <= nz; k++)
                for (int i = 0; i <= nx; i++) m.Nodes.Add(new RcNode(Id(i, k), b * i / nx, 0, L * k / nz));
            for (int k = 0; k < nz; k++)
                for (int i = 0; i < nx; i++)
                    m.Shells.Add(new RcShell(k * nx + i + 1, [Id(i, k), Id(i + 1, k), Id(i + 1, k + 1), Id(i, k + 1)], sec));
            for (int i = 0; i <= nx; i++) m.Supports.Add(new RcSupport(Id(i, 0), Fixed));
            for (int k = 1; k <= nz; k++)
                for (int i = 0; i <= nx; i++) m.Supports.Add(new RcSupport(Id(i, k), Uy));
            var lc = new RcLoadCase(1, "P");
            for (int i = 0; i <= nx; i++)
            {
                double w = (i == 0 || i == nx) ? 0.5 / nx : 1.0 / nx;
                lc.Nodal.Add(new RcNodalLoad(Id(i, nz), [0.0, 0.0, -p * w, 0.0, 0.0, 0.0]));
                lc.Prescribed.Add(new RcPrescribed(Id(i, nz), 0, delta));
            }
            m.LoadCases.Add(lc);
            m.Stages.Add(new RcStage("P", [(1, 1.0)], 4));
            return m;
        }
        double TopRx(RcSecantStepFields f) => Enumerable.Range(0, nx + 1).Sum(i => Reaction(f, Id(i, nz), 0));
        double MidUx(RcSecantStepFields f) => Disp(f, Id(nx / 2, nz / 2), 0);
        double p = 0.5 * pcr;
        var so = Solve("пилон, второй порядок", Pier(p));
        var cr = Solve("пилон, CR", Pier(p), Options(secondOrder: false));
        if (so == null || cr == null) return;
        // Балка Бернулли: H = P·k·Δ/(tg kL − kL) (сетка Q4 в плоскости — сдвиг и ν, отсюда допуск 1,5 %).
        double kl = Math.Sqrt(p / (E * t * b * b * b / 12)) * L;
        double hBeam = p * (kl / L) * delta / (Math.Tan(kl) - kl);
        TestHarness.CheckRel("ΣRx верха = P·k·Δ/(tg kL − kL)", TopRx(so), hBeam, 0.015);
        TestHarness.CheckRel("ux середины высоты: второй порядок = CR", MidUx(so), MidUx(cr), 0.01);
        // CR-Ньютон в своей плоскости теряет P-δ внутри КЭ (поворот КЭ как жёсткого целого, см. CrShellTangentTests):
        // на этой сетке ΣRx CR на +3,8 % выше балочного, второй порядок — −0,7 %.
        TestHarness.CheckRel("ΣRx верха: второй порядок ≈ CR", TopRx(so), TopRx(cr), 0.05);

        // Заданный сдвиг ≡ силы: реакции верха расчёта со сдвигом — узловыми силами на свободном верху.
        var mf = Pier(p);
        var lc = mf.LoadCases[0];
        lc.Prescribed.Clear();
        for (int i = 0; i <= nx; i++) lc.Nodal.Add(new RcNodalLoad(Id(i, nz), [Reaction(so, Id(i, nz), 0), 0.0, 0.0, 0.0, 0.0, 0.0]));
        var ff = Solve("пилон, силы вместо сдвига", mf);
        if (ff == null) return;
        TestHarness.CheckLess("пилон: перемещения сдвиг ≡ силы", Diff(so.Displacements, ff.Displacements), 1e-6);
    }

    /// <summary>
    /// Верх: ux задан (Δ = L/100), uy закреплён — подпёртая консоль, P = 1,2·P_cr = 1,2·2,046·π²EI/L²: K + K_σ не
    /// положительно определена — расчёт не завершён, в журнале «потеря устойчивости».
    /// </summary>
    private static void RunAboveCritical()
    {
        double p = 1.2 * 2.046 * Math.PI * Math.PI * Ei / (L * L);
        var log = new List<string>();
        var run = RcSecantAnalysis.Run(Cantilever(p, L / 100), Options(log: s => { lock (log) log.Add(s); }));
        TestHarness.Check("P = 1,2·P_cr: расчёт остановлен", !run.Result.Completed, run.Result.Message ?? "пройден");
        TestHarness.Check("P = 1,2·P_cr: в журнале «потеря устойчивости»",
            log.Any(s => s.Contains("потеря устойчивости", StringComparison.Ordinal)), log.LastOrDefault() ?? "журнал пуст");
    }
}
