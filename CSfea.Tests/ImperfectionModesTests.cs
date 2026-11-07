using CSfea.CScoreBridge.Structural;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>
/// Несовершенство по формам потери устойчивости (EN 1993-1-1 п. 5.3.2(11), срез 5): форма постановки устойчивости
/// (сетка дробления мельче секущей) → <see cref="RcModesImperfection"/> → поле u₀ с внутренним членом
/// <see cref="ImperfectionField.BeamInterior"/>. Сверки: согласованный вектор полуволны на одном КЭ против интеграла
/// N·∫w₀′·δw′; шарнирная колонна при P = 0,5·P_cr — полный прогиб a/(1 − P/P_cr); смена знака амплитуды; рама — форма 1
/// против крена с той же max|u₀| (справочно). Упругая колонна 0,4 × 0,4 м, E = 30 ГПа.
/// </summary>
[HarnessChecks]
public class ImperfectionModesTests
{
    private const double E = 30e9, L = 6.0, Side = 0.4;
    private static readonly double Inertia = Math.Pow(Side, 4) / 12;
    private static readonly double[] Dx = [1.0, 0.0, 0.0];

    private static readonly RcBeamSection Section = new("колонна")
    {
        Elastic = new BeamSection(E, Side * Side, Inertia, Inertia, 2 * Inertia),
    };

    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("Полуволна на одном КЭ: K_σ·u₀ + N·ĝ = N·∫w₀′·δw′");
        RunInteriorSineOneElement();

        TestHarness.Section("Шарнирная колонна, форма буклинга с дроблением 8, P = 0,5·P_cr");
        RunPinnedColumnFromBuckling();

        TestHarness.Section("Рама: форма 1 против крена (справочно)");
        RunFrameModeVersusSway();

        TestHarness.Section("Адаптер: формы → RcModesImperfection");
        RunAdapter();
    }

    /// <summary>Решённые формы (теги сетки = номера модели) переходят в модель адаптера одним элементом.</summary>
    private static void RunAdapter()
    {
        var m = Column(2);
        var modes = new CScore.Fem.Imperfections.FemResolvedModes("Ф", "Длины", "C1", [new(1, 2.5, 0.01)],
            new Dictionary<int, double[]> { [1] = [0.01, 0, 0, 0, 0, 0] }, new Dictionary<int, double[][]>());
        var diag = FemRcModelAdapter.ReplaceImperfections(m, [modes]);
        var imp = m.Imperfections.OfType<RcModesImperfection>().SingleOrDefault();
        TestHarness.Check("одно несовершенство по формам", imp != null && m.Imperfections.Count == 1);
        TestHarness.Check("узловое u₀ перенесено", imp?.Nodes[1][0] == 0.01);
        TestHarness.Check("сведения адаптера", diag.Any(d => d.Code == "imperfections" && !d.IsError));
    }

    private const int PinnedBase = 0b100111, PinnedTop = 0b100011, HoldY = 0b000010;

    /// <summary>Вертикальная колонна из <paramref name="n"/> КЭ (узлы 0…n по z, стержни 100 + i), Y всех узлов закреплён.</summary>
    private static RcStructuralModel Column(int n)
    {
        var m = new RcStructuralModel();
        for (int i = 0; i <= n; i++) m.Nodes.Add(new RcNode(i, 0, 0, L * i / n));
        for (int i = 0; i < n; i++) m.Beams.Add(new RcBeam(100 + i, i, i + 1, Section, Dx));
        m.Supports.Add(new RcSupport(0, PinnedBase));
        m.Supports.Add(new RcSupport(n, PinnedTop));
        for (int i = 1; i < n; i++) m.Supports.Add(new RcSupport(i, HoldY));
        return m;
    }

    /// <summary>Загружение 1 — силы вниз (узел → P), стадия из <paramref name="steps"/> шагов.</summary>
    private static void AddVertical(RcStructuralModel m, IReadOnlyDictionary<int, double> loads, int steps)
    {
        var lc = new RcLoadCase(1, "P");
        foreach (var (node, p) in loads) lc.Nodal.Add(new RcNodalLoad(node, [0.0, 0.0, -p, 0.0, 0.0, 0.0]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("P", [(1, 1.0)], steps));
    }

    private static RcSecantOptions Options() => new()
    {
        PhysicallyLinear = true,
        Solver = new SecantPicardOptions { Geometric = true, GeometricSecondOrder = true, TolDisplacement = 1e-10 },
    };

    private static RcSecantStepFields? Solve(string name, RcStructuralModel m)
    {
        var run = RcSecantAnalysis.Run(m, Options());
        TestHarness.Check($"{name}: расчёт сошёлся", run.Result.Completed, run.Result.Message ?? "");
        return run.Result.Completed ? run.LastConvergedFields() : null;
    }

    private static double Ux(RcSecantStepFields f, int node)
        => f.Displacements[RcSecantStepFields.NodeComponents * Array.IndexOf(f.NodeIds, node)];

    /// <summary>
    /// Один КЭ длины L, u₀ = a·sin(πz/L) по X (точки дробления 8, повороты концов ±aπ/L): вектор g КЭ при продольной
    /// силе N — повороты θy концов N·∫w₀′·N₂′ = 2aN/π и −2aN/π, сдвиги — 0 (сплайн, допуск 0,1 %). Без внутреннего
    /// члена (кубика по концам) было бы N·aπ/6 — на 18 % меньше: полуволна внутри КЭ не теряется.
    /// </summary>
    private static void RunInteriorSineOneElement()
    {
        double a = L / 300;
        var m = Column(1);
        var build = RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());
        var points = Enumerable.Range(1, 7).Select(k => new[] { a * Math.Sin(Math.PI * k / 8), 0.0, 0.0 }).ToArray();
        double slope = a * Math.PI / L;
        m.Imperfections.Add(new RcModesImperfection(
            new Dictionary<int, double[]> { [0] = [0, 0, 0, 0, slope, 0], [1] = [0, 0, 0, 0, -slope, 0] },
            new Dictionary<int, double[][]> { [100] = points }));
        var field = build.Imperfection(m)!;
        TestHarness.Check("внутренний член построен", field.BeamInterior[0] != null);

        var u = new double[build.Mesh.NDof];
        u[6 * build.NodeIndex[1] + 2] = -1e-4;               // укорочение — сжатие
        double n = build.Mesh.BeamAxialForce(0, u);
        TestHarness.Check("N < 0 (сжатие)", n < 0, n.ToString("G4"));
        var g = build.Mesh.ImperfectionForceElement(build.Mesh.Shells.Count, field, u)!;
        double expected = 2 * a * n / Math.PI;
        TestHarness.CheckRel("θy конца I: N·∫w₀′·N₂′ = 2aN/π", g[4], expected, 1e-3);
        TestHarness.CheckRel("θy конца J: −2aN/π", g[10], -expected, 1e-3);
        TestHarness.CheckLess("сдвиги концов X ≈ 0 (доля 2aN/π·1/L)", Math.Max(Math.Abs(g[0]), Math.Abs(g[6])) * L / Math.Abs(expected), 1e-3);

        // Без точек дробления (кубика Эрмита по концам): N·aπ/6 — проверка, что внутренний член работает.
        var cubic = new RcStructuralModel();
        cubic.Imperfections.Add(new RcModesImperfection(
            new Dictionary<int, double[]> { [0] = [0, 0, 0, 0, slope, 0], [1] = [0, 0, 0, 0, -slope, 0] },
            new Dictionary<int, double[][]>()));
        var gc = build.Mesh.ImperfectionForceElement(build.Mesh.Shells.Count, build.Imperfection(cubic)!, u)!;
        TestHarness.CheckRel("кубика по концам: θy конца I = N·aπ/6", gc[4], n * a * Math.PI / 6, 1e-9);
    }

    /// <summary>
    /// Шарнирная колонна: постановка устойчивости (4 КЭ, дробление до 8 — по точке внутри каждого КЭ) даёт P_cr и форму
    /// 1; секущий расчёт второго порядка той же колонны из 4 КЭ при P = 0,5·P_cr с u₀ = a·φ₁ (a = L/300) — полный прогиб
    /// середины a/(1 − P/P_cr) = 2a до 1 %. Смена знака амплитуды — прогиб и моменты меняют знак (1e−9).
    /// План просил секущую сетку из 1 КЭ: у одного КЭ сам секущий расчёт второго порядка — кубика с P_cr = 12EI/L²
    /// (+22 %), и 1 % недостижим при любом u₀; вектор формы на одном КЭ проверен отдельно (полуволна, выше).
    /// </summary>
    private static void RunPinnedColumnFromBuckling()
    {
        const int n = 4;
        var mb = Column(n);
        AddVertical(mb, new Dictionary<int, double> { [n] = 1.0 }, steps: 1);
        var r = RcBucklingAnalysis.Run(mb, [(1, 1.0)], [mb.Beams.Select(b => b.Id).ToList()], new RcBucklingOptions(Modes: 3, MinSubdivisions: 8));
        double pcr = r.Lambda[0];
        TestHarness.CheckRel("P_cr дробления 8 = π²EI/L²", pcr, Math.PI * Math.PI * E * Inertia / (L * L), 2e-3);
        var shape = r.ModeShape(r.Modes[0], mb.Nodes.Select(x => x.Id));
        TestHarness.Check("точки дробления — по одной на КЭ", mb.Beams.All(b => shape.BarPoints[b.Id].Length == 1));
        double phiMid = shape.Nodes[n / 2][0];
        TestHarness.CheckRel("форма нормирована: |φ середины| = 1", Math.Abs(phiMid), 1.0, 1e-9);

        double a = L / 300 * Math.Sign(phiMid);
        double p = 0.5 * pcr;
        RcSecantStepFields? Run(double amplitude)
        {
            var m = Column(n);
            AddVertical(m, new Dictionary<int, double> { [n] = p }, steps: 5);
            m.Imperfections.Add(RcBucklingModeShape.Sum([(shape, amplitude)]));
            return Solve($"a = {amplitude * 1000:0.##} мм", m);
        }
        var f = Run(a);
        var fm = Run(-a);
        if (f == null || fm == null) return;
        double y = a * phiMid + Ux(f, n / 2);
        TestHarness.CheckRel("полный прогиб середины = a/(1 − P/P_cr) = 2a", y, 2 * a * Math.Sign(phiMid), 0.01);
        // Продольные перемещения и N от P одинаковы при ±a — сравнивается изгиб: прогибы X и моменты концов.
        var ux = Enumerable.Range(0, n + 1).Select(i => Ux(f, i)).ToArray();
        var uxm = Enumerable.Range(0, n + 1).Select(i => Ux(fm, i)).ToArray();
        TestHarness.CheckLess("знак: прогибы(−a) = −прогибы(+a)", ux.Zip(uxm, (x, z) => Math.Abs(x + z)).Max() / ux.Max(Math.Abs), 1e-9);
        double[] Moments(RcSecantStepFields s) => Enumerable.Range(0, n)
            .SelectMany(e => new[] { 4, 5, 10, 11 }.Select(c => s.BeamForces[e * RcSecantStepFields.BeamForceComponents + c])).ToArray();
        var mp = Moments(f);
        TestHarness.CheckLess("знак: моменты(−a) = −моменты(+a)", mp.Zip(Moments(fm), (x, z) => Math.Abs(x + z)).Max() / mp.Max(Math.Abs), 1e-9);
    }

    /// <summary>
    /// Рама с заделками (стойки H = 6 м, ригель B = 6 м), вертикальные силы P на верхних узлах, P = 0,5·P_cr рамы: форма 1
    /// (сдвиговая) с амплитудой max|u₀| = φ·H против крена φ = 1/200 — полное смещение верха и момент заделки. Справочно:
    /// форма 1 рамы — не чистый сдвиг (стойки изгибаются), расхождение пишется в журнал, проверка — до 10 % (при сдаче
    /// среза 07.11: смещение верха +6,4 %, момент заделки +2,8 %; план ждал ≤ 5 % для чисто сдвиговой формы).
    /// </summary>
    private static void RunFrameModeVersusSway()
    {
        const double h = 6.0, span = 6.0, phi = 1.0 / 200;
        RcStructuralModel Frame()
        {
            var m = new RcStructuralModel();
            m.Nodes.Add(new RcNode(0, 0, 0, 0));
            m.Nodes.Add(new RcNode(1, 0, 0, h));
            m.Nodes.Add(new RcNode(2, span, 0, h));
            m.Nodes.Add(new RcNode(3, span, 0, 0));
            m.Beams.Add(new RcBeam(100, 0, 1, Section, Dx));
            m.Beams.Add(new RcBeam(101, 3, 2, Section, Dx));
            m.Beams.Add(new RcBeam(102, 1, 2, Section, [0.0, 0.0, 1.0]));
            m.Supports.Add(new RcSupport(0, 0x3F));
            m.Supports.Add(new RcSupport(3, 0x3F));
            m.Supports.Add(new RcSupport(1, 0b101010));      // из плоскости рамы: uy, θx, θz
            m.Supports.Add(new RcSupport(2, 0b101010));
            return m;
        }
        var mb = Frame();
        AddVertical(mb, new Dictionary<int, double> { [1] = 1.0, [2] = 1.0 }, steps: 1);
        var r = RcBucklingAnalysis.Run(mb, [(1, 1.0)], [[100], [101], [102]], new RcBucklingOptions(Modes: 3, MinSubdivisions: 8));
        var shape = r.ModeShape(r.Modes[0], mb.Nodes.Select(x => x.Id));
        double top = shape.Nodes[1][0];
        TestHarness.Check("форма 1 — сдвиговая (верх смещается по X)", Math.Abs(top) > 0.9, top.ToString("0.###"));
        double p = 0.5 * r.Lambda[0], a = phi * h / top;   // смещение верха формы = φ·H
        var loads = new Dictionary<int, double> { [1] = p, [2] = p };

        var ms = Frame();
        AddVertical(ms, loads, steps: 5);
        ms.Imperfections.Add(new RcSway(phi, Dx));
        var mm = Frame();
        AddVertical(mm, loads, steps: 5);
        mm.Imperfections.Add(RcBucklingModeShape.Sum([(shape, a)]));
        var fs = Solve("крен", ms);
        var fm = Solve("форма 1", mm);
        if (fs == null || fm == null) return;
        double uSway = phi * h + Ux(fs, 1), uMode = a * top + Ux(fm, 1);
        static double Base(RcSecantStepFields f) => Math.Sqrt(f.BeamForces[4] * f.BeamForces[4] + f.BeamForces[5] * f.BeamForces[5]);
        double mSway = Base(fs), mMode = Base(fm);
        TestHarness.Section($"справочно: смещение верха — крен {uSway * 1000:0.##} мм, форма {uMode * 1000:0.##} мм " +
                            $"({(uMode / uSway - 1) * 100:+0.#;−0.#} %); момент заделки — крен {mSway / 1e3:0.##} кН·м, " +
                            $"форма {mMode / 1e3:0.##} кН·м ({(mMode / mSway - 1) * 100:+0.#;−0.#} %)");
        TestHarness.CheckRel("смещение верха: форма против крена (справочно, 10 %)", uMode, uSway, 0.10);
        TestHarness.CheckRel("момент заделки: форма против крена (справочно, 10 %)", mMode, mSway, 0.10);
    }
}
