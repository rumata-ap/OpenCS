using CSfea.CScoreBridge.Structural;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>
/// Начальные несовершенства в секущем расчёте по теории второго порядка (EN 1993-1-1 п. 5.3, срез 0): поле u₀ даёт
/// правую часть −K_σ(N)·u₀ и вклад в истинные внутренние силы. Сверки: шарнирная колонна с погибью-параболой против
/// аналитического решения; эквивалентность расчёту без несовершенства с эквивалентными силами рис. 5.3–5.4 (крен,
/// погибь, переменная N, шарнир КЭ); стена в своей плоскости с креном; отказ при «Больших перемещениях» и нулевой эффект
/// без геометрической нелинейности. Упругая колонна 0,4 × 0,4 м, E = 30 ГПа, без физической нелинейности.
/// </summary>
[HarnessChecks]
public class ImperfectionSecondOrderTests
{
    private const double E = 30e9, L = 6.0, Side = 0.4;
    private static readonly double Inertia = Math.Pow(Side, 4) / 12, Ei = E * Inertia;
    private static readonly double[] Dx = [1.0, 0.0, 0.0];

    private static readonly RcBeamSection Section = new("колонна")
    {
        Elastic = new BeamSection(E, Side * Side, Inertia, Inertia, 2 * Inertia),
    };

    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("Шарнирная колонна с погибью: против аналитики");
        RunPinnedBow();

        TestHarness.Section("Погибь при P → 0: дополнительного изгиба нет");
        RunVanishingLoad();

        TestHarness.Section("Погибь ≡ эквивалентные силы рис. 5.4 (1 и 8 КЭ)");
        RunBowEquivalence(1);
        RunBowEquivalence(8);

        TestHarness.Section("Крен консоли ≡ силы φ·P рис. 5.3");
        RunSwayCantilever();

        TestHarness.Section("Переменная N и шарнир КЭ ≡ поэлементные эквивалентные силы");
        RunVariableAxialWithHinge();

        TestHarness.Section("Стена в своей плоскости с креном");
        RunWallSway();

        TestHarness.Section("Отказ при CR, нулевой эффект без геомнелина");
        RunFormulationGuards();
    }

    // ---------------- модели ----------------

    private const int PinnedBase = 0b100111, PinnedTop = 0b100011, Fixed = 0x3F;

    /// <summary>Вертикальная колонна из <paramref name="n"/> КЭ (узлы 0…n по z, стержни 100 + i), без нагрузок.</summary>
    private static RcStructuralModel Column(int n, int baseMask, int topMask, int topReleaseJ = 0)
    {
        var m = new RcStructuralModel();
        for (int i = 0; i <= n; i++) m.Nodes.Add(new RcNode(i, 0, 0, L * i / n));
        for (int i = 0; i < n; i++)
            m.Beams.Add(new RcBeam(100 + i, i, i + 1, Section, Dx, ReleaseJ: i == n - 1 ? topReleaseJ : 0));
        m.Supports.Add(new RcSupport(0, baseMask));
        if (topMask != 0) m.Supports.Add(new RcSupport(n, topMask));
        return m;
    }

    /// <summary>Загружение 1 — сосредоточенные силы вниз (узел → P), стадия из <paramref name="steps"/> шагов.</summary>
    private static void AddVertical(RcStructuralModel m, IReadOnlyDictionary<int, double> loads, int steps)
    {
        var lc = new RcLoadCase(1, "P");
        foreach (var (node, p) in loads) lc.Nodal.Add(new RcNodalLoad(node, [0.0, 0.0, -p, 0.0, 0.0, 0.0]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("P", [(1, 1.0)], steps));
    }

    /// <summary>
    /// Эквивалентные силы погиби-параболы цепочки всех стержней колонны (рис. 5.4, поэлементно): на КЭ со сжатием Pₑ —
    /// погонная 8·Pₑ·e₀/L² по d и силы −Pₑ·w₀′(sᵢ) в узле I, +Pₑ·w₀′(sⱼ) в узле J (у соседей одной N взаимно гасятся).
    /// Добавляются в загружение 1.
    /// </summary>
    private static void AddBowEquivalent(RcStructuralModel m, double e0, Func<int, double> compression)
    {
        var lc = m.LoadCases.Single(c => c.Id == 1);
        double W1(double s) => 4.0 * e0 * (L - 2.0 * s) / (L * L);
        for (int k = 0; k < m.Beams.Count; k++)
        {
            var b = m.Beams[k];
            double p = compression(k);
            double si = m.Nodes.Single(x => x.Id == b.NodeI).Z, sj = m.Nodes.Single(x => x.Id == b.NodeJ).Z;
            lc.Beams.Add(new RcBeamLoad(b.Id, [8.0 * p * e0 / (L * L), 0.0, 0.0]));
            lc.Nodal.Add(new RcNodalLoad(b.NodeI, [-p * W1(si), 0.0, 0.0, 0.0, 0.0, 0.0]));
            lc.Nodal.Add(new RcNodalLoad(b.NodeJ, [p * W1(sj), 0.0, 0.0, 0.0, 0.0, 0.0]));
        }
    }

    private static RcSecantOptions Options(bool geometric = true, bool secondOrder = true) => new()
    {
        PhysicallyLinear = true,
        Solver = new SecantPicardOptions
        {
            Geometric = geometric, GeometricSecondOrder = secondOrder, TolDisplacement = 1e-10,
        },
    };

    private static RcSecantStepFields? Solve(string name, RcStructuralModel m, RcSecantOptions? o = null)
    {
        var run = RcSecantAnalysis.Run(m, o ?? Options());
        TestHarness.Check($"{name}: расчёт сошёлся", run.Result.Completed, run.Result.Message ?? "");
        return run.Result.Completed ? run.LastConvergedFields() : null;
    }

    private static double Ux(RcSecantStepFields f, int node)
        => f.Displacements[RcSecantStepFields.NodeComponents * Array.IndexOf(f.NodeIds, node)];

    // Изгибающий момент конца КЭ (|M| по обеим плоскостям), end — 0 (I) или 1 (J).
    private static double Moment(RcSecantStepFields f, int beamIndex, int end)
    {
        int o = beamIndex * RcSecantStepFields.BeamForceComponents + 6 * end;
        return Math.Sqrt(f.BeamForces[o + 4] * f.BeamForces[o + 4] + f.BeamForces[o + 5] * f.BeamForces[o + 5]);
    }

    // Изгибающие моменты концов КЭ (My, Mz подряд); count — первые count КЭ (null — все).
    private static double[] Moments(RcSecantStepFields f, int? count = null)
        => Enumerable.Range(0, count ?? f.BeamIds.Length)
            .SelectMany(e => new[] { 4, 5, 10, 11 }.Select(c => f.BeamForces[e * RcSecantStepFields.BeamForceComponents + c]))
            .ToArray();

    // max|a − b| / scale (scale по умолчанию — max|b|).
    private static double Diff(double[] a, double[] b, double? scale = null)
    {
        double s = scale ?? b.Max(Math.Abs);
        return a.Zip(b, (x, y) => Math.Abs(x - y)).Max() / Math.Max(s, 1e-300);
    }

    // ---------------- проверки ----------------

    /// <summary>
    /// Шарнирная колонна, 8 КЭ, погибь e₀ = L/250 по X, P = 0,5·P_cr: полный прогиб середины y = e₀ + u =
    /// 8e₀/(kL)²·(sec(kL/2) − 1), k = √(P/EI) — до 0,5 %; момент середины M = P·y — до 1 % (план — 0,5 %): усилия концов
    /// КЭ второго порядка выдаются по K·d (без K_σ·d), у 8 КЭ это +0,63 % — столько же даёт расчёт с эквивалентными
    /// силами (тест эквивалентности), то есть это свойство выдачи усилий, а не несовершенства.
    /// </summary>
    private static void RunPinnedBow()
    {
        double pcr = Math.PI * Math.PI * Ei / (L * L), p = 0.5 * pcr, e0 = L / 250;
        var m = Column(8, PinnedBase, PinnedTop);
        AddVertical(m, new Dictionary<int, double> { [8] = p }, steps: 5);
        m.Imperfections.Add(new RcBow(m.Beams.Select(b => b.Id).ToList(), e0, Dx));
        var f = Solve("колонна", m);
        if (f == null) return;
        double kl = Math.Sqrt(p / Ei) * L;
        double yExact = 8 * e0 / (kl * kl) * (1 / Math.Cos(kl / 2) - 1);
        double y = e0 + Ux(f, 4);
        TestHarness.CheckRel("y середины = 8e₀/(kL)²·(sec(kL/2) − 1)", y, yExact, 0.005);
        TestHarness.CheckRel("M середины (КЭ 4, конец I) = P·y", Moment(f, 4, 0), p * yExact, 0.01);
        TestHarness.CheckRel("M середины (КЭ 3, конец J) = P·y", Moment(f, 3, 1), p * yExact, 0.01);
    }

    /// <summary>
    /// P = 10⁻⁶·P_cr: дополнительный прогиб u ≪ e₀ (сжатие почти не изгибает), момент середины = P·e₀ — эквивалентная
    /// погонная 8Pe₀/L² даёт qL²/8 = P·e₀. (План: |M|/(P_cr·e₀) &lt; 1e−8 — недостижимо, M ≈ 1e−6·P_cr·e₀.)
    /// </summary>
    private static void RunVanishingLoad()
    {
        double pcr = Math.PI * Math.PI * Ei / (L * L), p = 1e-6 * pcr, e0 = L / 250;
        var m = Column(8, PinnedBase, PinnedTop);
        AddVertical(m, new Dictionary<int, double> { [8] = p }, steps: 1);
        m.Imperfections.Add(new RcBow(m.Beams.Select(b => b.Id).ToList(), e0, Dx));
        var f = Solve("P → 0", m);
        if (f == null) return;
        TestHarness.CheckLess("|u середины| / e₀", Math.Abs(Ux(f, 4)) / e0, 1e-5);
        TestHarness.CheckRel("M середины = P·e₀", Moment(f, 4, 0), p * e0, 1e-4);
    }

    /// <summary>
    /// Рис. 5.4: колонна из <paramref name="n"/> КЭ с погибью ≡ та же колонна без несовершенства с погонной 8Pe₀/L² и
    /// силами 4Pe₀/L на концах (против неё) при той же P — перемещения узлов, моменты КЭ и реакции до 1e−6.
    /// </summary>
    private static void RunBowEquivalence(int n)
    {
        double pcr = Math.PI * Math.PI * Ei / (L * L), p = 0.5 * pcr, e0 = L / 250;
        var loads = new Dictionary<int, double> { [n] = p };
        var mi = Column(n, PinnedBase, PinnedTop);
        AddVertical(mi, loads, steps: 3);
        mi.Imperfections.Add(new RcBow(mi.Beams.Select(b => b.Id).ToList(), e0, Dx));
        var me = Column(n, PinnedBase, PinnedTop);
        AddVertical(me, loads, steps: 3);
        AddBowEquivalent(me, e0, _ => p);
        var fi = Solve($"{n} КЭ, погибь", mi);
        var fe = Solve($"{n} КЭ, эквивалентные силы", me);
        if (fi == null || fe == null) return;
        TestHarness.CheckLess($"{n} КЭ: перемещения", Diff(fi.Displacements, fe.Displacements), 1e-6);
        TestHarness.CheckLess($"{n} КЭ: моменты концов КЭ (доля P·e₀)", Diff(Moments(fi), Moments(fe), p * e0), 1e-6);
        TestHarness.CheckLess($"{n} КЭ: реакции (доля P)", Diff(fi.Reactions, fe.Reactions, p), 1e-6);
    }

    /// <summary>
    /// Консоль (заделка внизу), крен φ = 1/200 по X, P = 0,3·P_cr консоли ≡ без несовершенства с силой φ·P на верхе и
    /// −φ·P у заделки (рис. 5.3) — перемещения, моменты, реакции до 1e−6. Σ горизонтальных реакций = 0: крен —
    /// геометрия, не нагрузка (у наклонной консоли под вертикальной силой горизонтальной реакции нет). План ждал φ·P —
    /// это без нижней силы пары рис. 5.3.
    /// </summary>
    private static void RunSwayCantilever()
    {
        double pcr = Math.PI * Math.PI * Ei / (4 * L * L), p = 0.3 * pcr, phi = 1.0 / 200;
        const int n = 8;
        var loads = new Dictionary<int, double> { [n] = p };
        var mi = Column(n, Fixed, 0);
        AddVertical(mi, loads, steps: 3);
        mi.Imperfections.Add(new RcSway(phi, Dx));
        var me = Column(n, Fixed, 0);
        AddVertical(me, loads, steps: 3);
        var lc = me.LoadCases[0];
        lc.Nodal.Add(new RcNodalLoad(n, [phi * p, 0.0, 0.0, 0.0, 0.0, 0.0]));
        lc.Nodal.Add(new RcNodalLoad(0, [-phi * p, 0.0, 0.0, 0.0, 0.0, 0.0]));
        var fi = Solve("консоль, крен", mi);
        var fe = Solve("консоль, силы φ·P", me);
        if (fi == null || fe == null) return;
        TestHarness.CheckRel("u верха", Ux(fi, n), Ux(fe, n), 1e-6);
        TestHarness.CheckLess("моменты концов КЭ (доля φ·P·L)", Diff(Moments(fi), Moments(fe), phi * p * L), 1e-6);
        TestHarness.CheckLess("реакции (доля P)", Diff(fi.Reactions, fe.Reactions, p), 1e-6);
        int s0 = Array.IndexOf(fi.ReactionNodeIds, 0);
        double rmy = s0 >= 0 ? fi.Reactions[6 * s0 + 4] : double.NaN;
        TestHarness.CheckRel("|Rmy| заделки = φ·P·L + P·u", Math.Abs(rmy), phi * p * L + p * Ux(fi, n), 1e-6);
        TestHarness.CheckLess("|ΣRx| / (φ·P)", Math.Abs(fi.ReactionSums[0]) / (phi * p), 1e-9);
        TestHarness.Check("верх смещается в сторону крена", Ux(fi, n) > 0, $"{Ux(fi, n):e3}");
    }

    /// <summary>
    /// Колонна: заделка внизу, верх — шарнирно-неподвижный; силы P₁ на верхе и P₂ в середине высоты (N верхних КЭ — P₁,
    /// нижних — P₁ + P₂), погибь всей колонны. Шарнир — освобождение My, Mz конца J верхнего КЭ при закреплённых поворотах
    /// узла (А) и без освобождения при свободных поворотах узла (Б) — одна задача: А ≡ Б до 1e−6 проверяет конденсацию
    /// g шарнирного КЭ по K + K_σ; Б ≡ поэлементные эквивалентные силы (8Pₑe₀/L², ∓Pₑ·w₀′ на концах) до 1e−6.
    /// Моменты А и Б сравниваются без верхнего КЭ: его концевые силы выдаются по K·d + g без K_σ·d, поэтому у Б момент
    /// конца J равен −(K_σ·d)_θ, а не нулю (у А освобождённый момент — 0, проверяется отдельно).
    /// </summary>
    private static void RunVariableAxialWithHinge()
    {
        double pcr = 2.046 * Math.PI * Math.PI * Ei / (L * L), p1 = 0.2 * pcr, p2 = 0.15 * pcr, e0 = L / 300;
        const int n = 8;
        var loads = new Dictionary<int, double> { [n] = p1, [n / 2] = p2 };
        const int releaseMyMz = 0b110000;
        var ma = Column(n, Fixed, 0b111011, topReleaseJ: releaseMyMz);
        var mb = Column(n, Fixed, PinnedTop);
        var me = Column(n, Fixed, PinnedTop);
        foreach (var m in new[] { ma, mb, me }) AddVertical(m, loads, steps: 3);
        ma.Imperfections.Add(new RcBow(ma.Beams.Select(b => b.Id).ToList(), e0, Dx));
        mb.Imperfections.Add(new RcBow(mb.Beams.Select(b => b.Id).ToList(), e0, Dx));
        AddBowEquivalent(me, e0, k => k < n / 2 ? p1 + p2 : p1);
        var fa = Solve("шарнир КЭ (А)", ma);
        var fb = Solve("шарнир узла (Б)", mb);
        var fe = Solve("эквивалентные силы (Б)", me);
        if (fa == null || fb == null || fe == null) return;
        // Повороты верхнего узла у А закреплены, у Б свободны — сравнение без них.
        static double[] NoTopRotations(RcSecantStepFields f)
        {
            var d = (double[])f.Displacements.Clone();
            int o = RcSecantStepFields.NodeComponents * Array.IndexOf(f.NodeIds, n);
            for (int c = 3; c < 5; c++) d[o + c] = 0.0;
            return d;
        }
        double mScale = (p1 + p2) * e0;
        TestHarness.CheckLess("А ≡ Б: перемещения", Diff(NoTopRotations(fa), NoTopRotations(fb)), 1e-6);
        TestHarness.CheckLess("А ≡ Б: моменты КЭ ниже шарнирного (доля P·e₀)",
            Diff(Moments(fa, n - 1), Moments(fb, n - 1), mScale), 1e-6);
        TestHarness.CheckLess("А: момент у шарнира = 0 (доля P·e₀)", Moment(fa, n - 1, 1) / mScale, 1e-9);
        TestHarness.CheckLess("Б ≡ силы: перемещения", Diff(fb.Displacements, fe.Displacements), 1e-6);
        TestHarness.CheckLess("Б ≡ силы: моменты (доля P·e₀)", Diff(Moments(fb), Moments(fe), mScale), 1e-6);
        TestHarness.CheckLess("Б ≡ силы: реакции (доля P)", Diff(fb.Reactions, fe.Reactions, p1 + p2), 1e-6);
    }

    /// <summary>
    /// Стена 6 × 3 м (t = 0,2, Q4 12 × 6) в плоскости xz, низ заделан, uy закреплены; вертикальная нагрузка по верхнему
    /// ребру P, крен φ = 1/200 по X ≡ без несовершенства с горизонтальной φ·P по верхнему ребру — сдвиг верха до 1 %.
    /// </summary>
    private static void RunWallSway()
    {
        const double b = 6.0, h = 3.0, t = 0.2, p = 20e6, phi = 1.0 / 200;
        const int nx = 12, nz = 6;
        var sec = new RcShellSection("стена") { Elastic = new Laminate([new Ply(new OrthotropicMaterial(E, E, 0.2, E / 2.4), 0, t)]) };
        static int Id(int i, int k) => k * (nx + 1) + i;
        RcStructuralModel Wall(bool equivalent)
        {
            var m = new RcStructuralModel();
            for (int k = 0; k <= nz; k++)
                for (int i = 0; i <= nx; i++) m.Nodes.Add(new RcNode(Id(i, k), b * i / nx, 0, h * k / nz));
            for (int k = 0; k < nz; k++)
                for (int i = 0; i < nx; i++)
                    m.Shells.Add(new RcShell(k * nx + i + 1, [Id(i, k), Id(i + 1, k), Id(i + 1, k + 1), Id(i, k + 1)], sec));
            for (int i = 0; i <= nx; i++) m.Supports.Add(new RcSupport(Id(i, 0), Fixed));
            for (int k = 1; k <= nz; k++)
                for (int i = 0; i <= nx; i++) m.Supports.Add(new RcSupport(Id(i, k), 0b000010));
            var lc = new RcLoadCase(1, "V");
            for (int i = 0; i <= nx; i++)
            {
                double w = (i == 0 || i == nx) ? 0.5 / nx : 1.0 / nx;
                lc.Nodal.Add(new RcNodalLoad(Id(i, nz), [equivalent ? phi * p * w : 0.0, 0.0, -p * w, 0.0, 0.0, 0.0]));
            }
            m.LoadCases.Add(lc);
            m.Stages.Add(new RcStage("V", [(1, 1.0)], 2));
            if (!equivalent) m.Imperfections.Add(new RcSway(phi, Dx));
            return m;
        }
        var fi = Solve("стена, крен", Wall(false));
        var fe = Solve("стена, силы φ·P", Wall(true));
        if (fi == null || fe == null) return;
        TestHarness.CheckRel("стена: сдвиг верхнего угла", Ux(fi, Id(nx, nz)), Ux(fe, Id(nx, nz)), 0.01);
        TestHarness.CheckRel("стена: сдвиг середины верха", Ux(fi, Id(nx / 2, nz)), Ux(fe, Id(nx / 2, nz)), 0.01);
    }

    /// <summary>
    /// «Большие перемещения» (CR) с несовершенством — отказ с текстом; без геометрической нелинейности поле не учитывается:
    /// перемещения совпадают с расчётом без него.
    /// </summary>
    private static void RunFormulationGuards()
    {
        double p = 1e6, e0 = L / 250;
        RcStructuralModel Model(bool withBow)
        {
            var m = Column(4, PinnedBase, PinnedTop);
            AddVertical(m, new Dictionary<int, double> { [4] = p }, steps: 1);
            m.LoadCases[0].Nodal.Add(new RcNodalLoad(2, [1e3, 0.0, 0.0, 0.0, 0.0, 0.0]));
            if (withBow) m.Imperfections.Add(new RcBow(m.Beams.Select(b => b.Id).ToList(), e0, Dx));
            return m;
        }
        try
        {
            RcSecantAnalysis.Run(Model(true), Options(geometric: true, secondOrder: false));
            TestHarness.Check("CR + несовершенство — исключение", false, "расчёт прошёл");
        }
        catch (NotSupportedException ex)
        {
            TestHarness.Check("CR + несовершенство — «только в теории второго порядка»",
                ex.Message.Contains("только в теории второго порядка", StringComparison.Ordinal), ex.Message);
        }
        var with = Solve("без геомнелина, с погибью", Model(true), Options(geometric: false));
        var without = Solve("без геомнелина, без погиби", Model(false), Options(geometric: false));
        if (with == null || without == null) return;
        TestHarness.CheckLess("без геомнелина: перемещения как без поля", Diff(with.Displacements, without.Displacements), 1e-12);
        TestHarness.CheckLess("без геомнелина: моменты как без поля", Diff(Moments(with), Moments(without)), 1e-12);
    }
}
