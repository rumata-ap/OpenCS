using System.Text.Json;
using CScore;
using CScore.En1992;
using CScore.Fem;
using CScore.Fem.Imperfections;
using CScore.Sp63;
using CSfea.CScoreBridge.Structural;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>
/// Несовершенства ЖБ (срез 6): EN 1992-1-1 п. 5.2 (eᵢ = l₀/400), случайный эксцентриситет п. 8.1.7 СП 63 как погибь,
/// погибь стен по ярусам между перекрытиями (<see cref="RcWallTierBow"/>) и совместимость с физической нелинейностью
/// (секущий расчёт с волоконным сечением CScore). Опорное решение — та же схема с искривлённой геометрией узлов
/// (парабола в координатах, без поля u₀).
/// </summary>
[HarnessChecks]
public class ImperfectionRcTests
{
    private const double E = 30e9;
    private const int PinnedBase = 0b100111, PinnedTop = 0b100011;
    private static readonly double[] Dx = [1.0, 0.0, 0.0];

    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("Упругая колонна: погибь EN 1992 l₀/400 против аналитики");
        RunElasticColumnEn1992();

        TestHarness.Section("Стена-полоса: погибь яруса против искривлённой геометрии");
        RunWallStrip();

        TestHarness.Section("Погибь стен по ярусам: резолвер → адаптер → поле u₀");
        RunWallTiersThroughAdapter();

        TestHarness.Section("ЖБ колонна, физическая нелинейность: погибь e_a против искривлённой геометрии");
        RunNonlinearColumn();
    }

    private static RcSecantOptions Options(bool physicallyLinear) => new()
    {
        PhysicallyLinear = physicallyLinear,
        Solver = new SecantPicardOptions { Geometric = true, GeometricSecondOrder = true, TolDisplacement = 1e-10 },
    };

    private static RcSecantStepFields? Solve(string name, RcStructuralModel m, bool physicallyLinear)
    {
        var run = RcSecantAnalysis.Run(m, Options(physicallyLinear));
        TestHarness.Check($"{name}: расчёт сошёлся", run.Result.Completed, run.Result.Message ?? "");
        return run.Result.Completed ? run.LastConvergedFields() : null;
    }

    private static double Disp(RcSecantStepFields f, int node, int dof)
        => f.Displacements[RcSecantStepFields.NodeComponents * Array.IndexOf(f.NodeIds, node) + dof];

    private static double MomentI(RcSecantStepFields f, int beamIndex)
    {
        int o = beamIndex * RcSecantStepFields.BeamForceComponents;
        return Math.Sqrt(f.BeamForces[o + 4] * f.BeamForces[o + 4] + f.BeamForces[o + 5] * f.BeamForces[o + 5]);
    }

    /// <summary>Вертикальная колонна из <paramref name="n"/> КЭ; <paramref name="x"/> — смещение узла по X от ζ (искривлённая ось).</summary>
    private static RcStructuralModel Column(int n, double length, RcBeamSection section, double p, int steps, Func<double, double>? x = null)
    {
        var m = new RcStructuralModel();
        for (int i = 0; i <= n; i++) m.Nodes.Add(new RcNode(i, x?.Invoke((double)i / n) ?? 0.0, 0, length * i / n));
        for (int i = 0; i < n; i++) m.Beams.Add(new RcBeam(100 + i, i, i + 1, section, Dx));
        m.Supports.Add(new RcSupport(0, PinnedBase));
        m.Supports.Add(new RcSupport(n, PinnedTop));
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(n, [0.0, 0.0, -p, 0.0, 0.0, 0.0]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("P", [(1, 1.0)], steps));
        return m;
    }

    /// <summary>
    /// Шарнирная колонна 6 м, 8 КЭ, упругая 0,4 × 0,4, P = 0,5·P_cr; eᵢ = l₀/400 (μ = 1) — полный прогиб середины
    /// 8e/(kL)²·(sec(kL/2) − 1) до 0,5 %.
    /// </summary>
    private static void RunElasticColumnEn1992()
    {
        const double length = 6.0, side = 0.4;
        double inertia = Math.Pow(side, 4) / 12, ei = E * inertia;
        var section = new RcBeamSection("колонна") { Elastic = new BeamSection(E, side * side, inertia, inertia, 2 * inertia) };
        double pcr = Math.PI * Math.PI * ei / (length * length), p = 0.5 * pcr;
        double e0 = En1992Imperfections.BowSimplified(length, 1.0, out _);
        TestHarness.CheckRel("eᵢ = l₀/400 = 15 мм", e0, 0.015, 1e-12);
        var m = Column(8, length, section, p, 5);
        m.Imperfections.Add(new RcBow(m.Beams.Select(b => b.Id).ToList(), e0, Dx));
        var f = Solve("упругая колонна", m, physicallyLinear: true);
        if (f == null) return;
        double kl = Math.Sqrt(p / ei) * length;
        TestHarness.CheckRel("e + u середины = 8e/(kL)²·(sec(kL/2) − 1)", e0 + Disp(f, 4, 0), 8 * e0 / (kl * kl) * (1 / Math.Cos(kl / 2) - 1), 0.005);
    }

    // ---------------- стены ----------------

    private const double WallB = 1.0, WallH = 3.0, WallT = 0.2;

    /// <summary>
    /// Полоса стены 1 × 3 м (t = 0,2, Q4 2 × 8) в плоскости xz: низ — шарнир по линии (ux, uy, uz), верх — uy; вертикальная
    /// сила P = 0,3·P_cr по верхнему ребру (P_cr = π²·E·b·t³/12/h²). Погибь яруса 0…3 м e₀ = l₀/400 = 7,5 мм вдоль +Y
    /// (<see cref="RcWallTierBow"/>) против той же полосы с узлами, смещёнными по параболе (без поля): полный прогиб
    /// середины до 3 %; справочно — против e₀/(1 − P/P_cr) балочной полосы до 10 %.
    /// </summary>
    private static void RunWallStrip()
    {
        const int nx = 2, nz = 8;
        double pcr = Math.PI * Math.PI * E * WallB * Math.Pow(WallT, 3) / 12 / (WallH * WallH), p = 0.3 * pcr;
        double e0 = En1992Imperfections.BowSimplified(WallH, 1.0, out _);
        var sec = new RcShellSection("стена") { Elastic = new Laminate([new Ply(new OrthotropicMaterial(E, E, 0.2, E / 2.4), 0, WallT)]) };
        static int Id(int i, int k) => k * (nx + 1) + i;
        RcStructuralModel Wall(bool curved)
        {
            var m = new RcStructuralModel();
            for (int k = 0; k <= nz; k++)
            {
                double zeta = (double)k / nz;
                double y = curved ? 4 * e0 * zeta * (1 - zeta) : 0.0;
                for (int i = 0; i <= nx; i++) m.Nodes.Add(new RcNode(Id(i, k), WallB * i / nx, y, WallH * zeta));
            }
            for (int k = 0; k < nz; k++)
                for (int i = 0; i < nx; i++)
                    m.Shells.Add(new RcShell(k * nx + i + 1, [Id(i, k), Id(i + 1, k), Id(i + 1, k + 1), Id(i, k + 1)], sec));
            for (int i = 0; i <= nx; i++)
            {
                m.Supports.Add(new RcSupport(Id(i, 0), 0b000111));
                m.Supports.Add(new RcSupport(Id(i, nz), 0b000010));
            }
            var lc = new RcLoadCase(1, "V");
            for (int i = 0; i <= nx; i++)
                lc.Nodal.Add(new RcNodalLoad(Id(i, nz), [0.0, 0.0, -p * (i == 0 || i == nx ? 0.5 / nx : 1.0 / nx), 0.0, 0.0, 0.0]));
            m.LoadCases.Add(lc);
            m.Stages.Add(new RcStage("V", [(1, 1.0)], 4));
            if (!curved)
                m.Imperfections.Add(new RcWallTierBow(m.Shells.Select(s => s.Id).ToHashSet(), 0.0, WallH, [0.0, 1.0, 0.0], e0));
            return m;
        }
        var fi = Solve("стена, погибь яруса", Wall(false), physicallyLinear: true);
        var fc = Solve("стена, искривлённая геометрия", Wall(true), physicallyLinear: true);
        if (fi == null || fc == null) return;
        int mid = Id(nx / 2, nz / 2);
        double yi = e0 + Disp(fi, mid, 1), yc = e0 + Disp(fc, mid, 1);
        TestHarness.CheckRel("полный прогиб середины: погибь ≡ искривлённая геометрия", yi, yc, 0.03);
        TestHarness.Check("прогиб в сторону погиби", Disp(fi, mid, 1) > 0, $"{Disp(fi, mid, 1):e3}");
        TestHarness.CheckRel("справочно: e₀/(1 − P/P_cr) балочной полосы", yi, e0 / (1 - p / pcr), 0.10);
    }

    private sealed class WallProps : IFemElementStiffnessSource
    {
        public FemShellStiffness? Shell(FemElement e) => new(E, 0.2, e.ThicknessM ?? WallT);
        public FemBarStiffness? Bar(FemElement e) => null;
        public double? UnitWeight(FemElement e) => null;
        public bool IsWithoutStiffness(FemElement e) => false;
        public double? BarArea(FemElement e) => null;
    }

    /// <summary>
    /// Стена 1 × 3 м (2 × 8 КЭ) в плоскости xz с перекрытием на z = 1,5 м (две пластины y 0…1): резолвер даёт два яруса по
    /// 1,5 м с e₀ = l₀/400 = 3,75 мм вдоль +Y (нормаль стенки ориентирована детерминированно), адаптер — два
    /// <see cref="RcWallTierBow"/>; u₀ узлов: на уровне перекрытия и опор — 0, в серединах ярусов — e₀; перекрытию — нет.
    /// Знак «−» второго элемента — по нормали; погибь с формами в одном варианте — ошибка.
    /// </summary>
    private static void RunWallTiersThroughAdapter()
    {
        const int nx = 2, nz = 8;
        var nodes = new List<FemMeshNode>();
        var elements = new List<FemElement>();
        static int Id(int i, int k) => 1 + k * (nx + 1) + i;
        for (int k = 0; k <= nz; k++)
            for (int i = 0; i <= nx; i++)
                nodes.Add(new FemMeshNode { NodeTag = Id(i, k).ToString(), X = WallB * i / nx, Y = 0, Z = WallH * k / nz });
        for (int k = 0; k < nz; k++)
            for (int i = 0; i < nx; i++)
                elements.Add(new FemElement
                {
                    ElemTag = (1 + k * nx + i).ToString(), ElemType = "shell", SourceMemberTag = "W", ThicknessM = WallT,
                    NodeIdsJson = JsonSerializer.Serialize(new[] { Id(i, k), Id(i + 1, k), Id(i + 1, k + 1), Id(i, k + 1) }),
                });
        // Перекрытие на z = 1,5 м: узлы стены (i, 4) и узлы по y = 1.
        int floorBase = 100;
        for (int i = 0; i <= nx; i++)
            nodes.Add(new FemMeshNode { NodeTag = (floorBase + i).ToString(), X = WallB * i / nx, Y = 1.0, Z = 1.5 });
        for (int i = 0; i < nx; i++)
            elements.Add(new FemElement
            {
                ElemTag = (50 + i).ToString(), ElemType = "shell", SourceMemberTag = "F", ThicknessM = WallT,
                NodeIdsJson = JsonSerializer.Serialize(new[] { Id(i, 4), Id(i + 1, 4), floorBase + i + 1, floorBase + i }),
            });
        var imp = new FemImperfection
        {
            Id = 7, LoadCaseId = 9, Kind = FemImperfectionKinds.WallBow, Tag = "Стена",
            ParamsJson = new FemWallBowParams { MemberTags = ["W"], Sign = FemBowParams.SignPlus }.ToJson(),
        };
        var members = new List<FemMember> { new() { ElemTag = "W", ElemType = "shell" }, new() { ElemTag = "F", ElemType = "shell" } };
        var resolved = FemImperfectionResolver.Resolve([(imp, 1.0)], elements, nodes, members);
        TestHarness.Check("привязка без ошибок", !resolved.HasErrors, string.Join("; ", resolved.Diagnostics.Select(d => d.Message)));
        if (resolved.HasErrors) return;
        var wall = (FemResolvedWallBow)resolved.Items.Single();
        TestHarness.Check("два яруса", wall.Tiers.Count == 2, $"{wall.Tiers.Count}");
        if (wall.Tiers.Count != 2) return;
        TestHarness.CheckRel("ярус 1: 0…1,5 м", wall.Tiers[0].ZTop, 1.5, 1e-9);
        TestHarness.CheckRel("e₀ = 1,5/400", wall.Tiers[0].E0, 1.5 / 400, 1e-12);
        TestHarness.Check("нормаль +Y", Math.Abs(wall.Tiers[0].Normal.Y - 1) < 1e-12);

        var decided = FemImperfectionResolver.Decide(resolved.Items, null, elements, nodes, "нет расчёта");
        TestHarness.Check("отчёт: две строки ярусов", decided.Report.WallBows.Count == 2);
        var input = new FemRcModelInput
        {
            MeshNodes = nodes, MeshElements = elements, Properties = new WallProps(), Members = members,
            Supports = [.. Enumerable.Range(0, nx + 1).Select(i => new FemMeshNodeSupport { NodeTag = Id(i, 0).ToString(), Mask = 0b000111 })],
            LoadCases = [new FemLoadCase { Id = 1, Tag = "P" }],
            MeshNodeLoads = [new FemMeshNodeLoad { LoadCaseId = 1, MeshNodeTag = Id(1, nz).ToString(), Fz = -1e3 }],
            Stages = [new FemRcStage("S", [(1, 1.0)], 1)], Imperfections = decided.Items,
        };
        var adapted = FemRcModelAdapter.Adapt(input);
        TestHarness.Check("адаптер без ошибок", !adapted.HasErrors, string.Join("; ", adapted.Report));
        if (adapted.HasErrors) return;
        TestHarness.Check("в модели два яруса", adapted.Model.Imperfections.OfType<RcWallTierBow>().Count() == 2);
        var u = RcStructuralMeshBuild.ImperfectionNodeTranslations(adapted.Model);
        double Uy(int node) => u.TryGetValue(node, out var v) ? v[1] : 0.0;
        TestHarness.CheckLess("u₀ на уровне перекрытия = 0", Math.Abs(Uy(Id(1, 4))), 1e-15);
        TestHarness.CheckLess("u₀ у опоры = 0", Math.Abs(Uy(Id(1, 0))), 1e-15);
        TestHarness.CheckRel("u₀ середины яруса 1 = e₀", Uy(Id(1, 2)), 1.5 / 400, 1e-12);
        TestHarness.CheckRel("u₀ середины яруса 2 = e₀", Uy(Id(1, 6)), 1.5 / 400, 1e-12);
        TestHarness.Check("перекрытию u₀ не задано", !u.ContainsKey(floorBase) || Math.Abs(u[floorBase][1]) < 1e-15);

        var minus = imp.Clone();
        minus.ParamsJson = new FemWallBowParams { MemberTags = ["W"], Sign = FemBowParams.SignMinus, E0Mode = FemBowParams.E0Sp63 }.ToJson();
        var rm = FemImperfectionResolver.Resolve([(minus, 1.0)], elements, nodes, members);
        var wm = (FemResolvedWallBow)rm.Items.Single();
        // СП 63 п. 8.1.7: max(1,5/600 = 2,5 мм; 200/30 = 6,67 мм; 10 мм) = 10 мм.
        TestHarness.CheckRel("СП 63: e_a = −10 мм", wm.Tiers[0].E0, -Sp63AccidentalEccentricity.Ea(1.5, WallT), 1e-12);
        TestHarness.CheckRel("СП 63: e_a = 10 мм", Sp63AccidentalEccentricity.Ea(1.5, WallT), 0.010, 1e-12);

        // Стена из одного КЭ по высоте 0…3 м: узлов между уровнями нет.
        var coarseNodes = new List<FemMeshNode>
        {
            new() { NodeTag = "1", X = 0, Z = 0 }, new() { NodeTag = "2", X = 1, Z = 0 },
            new() { NodeTag = "3", X = 1, Z = 3 }, new() { NodeTag = "4", X = 0, Z = 3 },
        };
        var coarseElements = new List<FemElement>
        {
            new() { ElemTag = "1", ElemType = "shell", ThicknessM = WallT, NodeIdsJson = "[1,2,3,4]" },
        };
        var coarse = imp.Clone();
        coarse.ParamsJson = new FemWallBowParams { ElementTags = ["1"] }.ToJson();
        var rc = FemImperfectionResolver.Resolve([(coarse, 1.0)], coarseElements, coarseNodes, []);
        TestHarness.Check("ярус из одного КЭ по высоте — предупреждение и ошибка «нет ярусов»",
            rc.Diagnostics.Any(d => d.Code == "imperfection_wall_coarse" && !d.IsError) && rc.HasErrors);
        var floor = imp.Clone();
        floor.ParamsJson = new FemWallBowParams { ElementTags = ["50"] }.ToJson();
        TestHarness.Check("перекрытие — не стена: ошибка",
            FemImperfectionResolver.Resolve([(floor, 1.0)], elements, nodes, members).Diagnostics.Any(d => d.Code == "imperfection_wall_not_wall"));
    }

    // ---------------- физическая нелинейность ----------------

    /// <summary>
    /// ЖБ колонна 0,3 × 0,3 м, B25 / A500 (по 3 стержня у граней, As = 804 мм² у грани), волоконное сечение CScore,
    /// шарнирная, L = 5 м, 16 КЭ, секущий расчёт с физической нелинейностью; N = 0,35·N_ult (0,45 — предельная стадия секущего расчёта); погибь e_a по п. 8.1.7
    /// (max(5000/600; 300/30; 10) = 10 мм): (1) сходится; (2) M середины = N·(e₀ + v) до 1,5 % (выдача концевых сил по K·d);
    /// (3) против той же колонны с искривлённой осью (без поля) — полный прогиб и M середины до 2 %; (4) прогиб больше
    /// упругого с начальной жёсткостью сечения; (5) знак «−» — зеркально.
    /// </summary>
    private const double NonlinearShare = 0.35;

    private static void RunNonlinearColumn()
    {
        const double length = 5.0, b = 0.3, a = 0.04, asFace = 804e-6;
        const int n = 16;
        var cross = RcTestMaterials.Rect(b, b, a, asFace, asFace, nBars: 3, nx: 8, ny: 30);
        var section = new RcBeamSection("ЖБ 300×300") { Cross = cross, Calc = CalcType.N, TorsionGJ = 1e7 };
        // N_ult ≈ R_b·A + R_s·A_s,tot = 18,5 МПа · 0,09 + 500 МПа · 1608 мм² ≈ 2,47 МН.
        double nUlt = 18.5e6 * b * b + 500e6 * 2 * asFace, p = NonlinearShare * nUlt;
        double e0 = Sp63AccidentalEccentricity.Ea(length, b);
        TestHarness.CheckRel("e_a = 10 мм", e0, 0.010, 1e-12);
        // Упругое сечение с начальной жёсткостью самого сечения CScore (бетон + арматура, малая кривизна, кН·м² → Н·м²);
        // по обеим осям берётся большая — упругий прогиб не больше, чем при любой из осей.
        const double k0 = 1e-7;
        double eiX = Math.Abs(cross.Integral(new Kurvature { e0 = 0, ky = k0, kz = 0 }, CalcType.N, true, true).Mx) / k0 * 1e3;
        double eiY = Math.Abs(cross.Integral(new Kurvature { e0 = 0, ky = 0, kz = k0 }, CalcType.N, true, true).My) / k0 * 1e3;
        double ei0 = Math.Max(eiX, eiY), ib = ei0 / E;
        var elastic = new RcBeamSection("упругое 300×300") { Elastic = new BeamSection(E, b * b, ib, ib, 2 * ib) };

        RcSecantStepFields? Run(string name, double e, bool curved, bool linear)
        {
            var m = Column(n, length, linear ? elastic : section, p, 8, curved ? z => 4 * e * z * (1 - z) : null);
            if (!curved) m.Imperfections.Add(new RcBow(m.Beams.Select(x => x.Id).ToList(), e, Dx));
            return Solve(name, m, linear);
        }
        var fi = Run("погибь, физнелин", e0, false, false);
        var fc = Run("искривлённая ось, физнелин", e0, true, false);
        var fl = Run("погибь, упругое сечение", e0, false, true);
        var fm = Run("погибь «−», физнелин", -e0, false, false);
        if (fi == null || fc == null || fl == null || fm == null) return;
        const int mid = n / 2;
        double v = Disp(fi, mid, 0);
        double yi = e0 + v, yc = e0 + Disp(fc, mid, 0);
        TestHarness.Check("прогиб в сторону погиби", v > 0, $"{v:e3}");
        TestHarness.CheckRel("равновесие: M середины = N·(e₀ + v)", MomentI(fi, mid), p * yi, 0.015);
        TestHarness.CheckRel("полный прогиб середины: погибь ≡ искривлённая ось", yi, yc, 0.02);
        TestHarness.CheckRel("M середины: погибь ≡ искривлённая ось", MomentI(fi, mid), MomentI(fc, mid), 0.02);
        TestHarness.Check("физнелин: прогиб больше упругого", v > Disp(fl, mid, 0), $"{v:e3} против {Disp(fl, mid, 0):e3}");
        TestHarness.CheckRel("знак «−» — зеркально", -Disp(fm, mid, 0), v, 1e-6);
    }
}
