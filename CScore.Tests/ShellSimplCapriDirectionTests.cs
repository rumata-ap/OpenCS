using System;
using System.Linq;
using Xunit;
using CScore;

namespace CScore.Tests;

// Перебор направлений Капра-Мори (ShellSimplSolver, shell_simpl_capri_*) и знак крутящего момента.
//
// OpenCS проецирует тензор усилий на направление α как M_n = Mx·cos²α + My·sin²α + Mxy·sin2α.
// В методиках, повторяющих справку Robot, та же формула записана с «−Mxy·sin2α». Замена α → 180°−α
// переводит одну запись в другую, а проекция арматуры As_x·cos²α + As_y·sin²α при этом не меняется,
// поэтому на полном полуобороте 0…180° обе записи дают один и тот же результат с зеркальным углом.
//
// Выводы дополнительно сверяются со слоистой моделью (ShellLayeredCheck): она работает с полным
// тензором через НДС по толщине, без перебора направлений, и поэтому независима от формулы проекции.
public class ShellSimplCapriDirectionTests
{
    const double StepDeg = 5.0;

    // Плита h=200 мм, B25, A500, Ø12 шаг 200 у каждой грани в обоих направлениях, центр арматуры в 30 мм от грани.
    static class Fixture
    {
        public const double AsPerMeter = Math.PI * 0.012 * 0.012 / 4.0 / 0.2; // м²/м

        static MaterialChars ConcreteChars(CalcType ct, double rb, double rbt) => new(ct)
        {
            Type = MatType.Concrete, E = 30_000_000.0, Fc = -rb, Ft = rbt,
            Ec0 = -0.002, Ec1 = -0.6 * rb / 30_000_000.0, Ec2 = -0.0035, Ec1Red = -0.0015,
            Et0 = 0.0001, Et1 = 0.6 * rbt / 30_000_000.0, Et2 = 0.00015, Et1Red = 0.00008,
        };

        static MaterialChars RebarChars(CalcType ct, double rs) => new(ct)
        {
            Type = MatType.ReSteelF, E = 200_000_000.0, Fc = -rs, Ft = rs, Ec2 = -0.025, Et2 = 0.025,
        };

        public static Material Concrete()
        {
            var m = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0 };
            m.C = ConcreteChars(CalcType.C, 14_500.0, 1_050.0);
            m.CL = ConcreteChars(CalcType.CL, 14_500.0, 1_050.0);
            m.N = ConcreteChars(CalcType.N, 18_500.0, 1_550.0);
            m.NL = ConcreteChars(CalcType.NL, 18_500.0, 1_550.0);
            return m;
        }

        public static Material Rebar()
        {
            var m = new Material { Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000.0 };
            m.C = RebarChars(CalcType.C, 435_000.0);
            m.CL = RebarChars(CalcType.CL, 435_000.0);
            m.N = RebarChars(CalcType.N, 500_000.0);
            m.NL = RebarChars(CalcType.NL, 500_000.0);
            return m;
        }

        public static PlateSection Section() => new()
        {
            // Бетон на растяжение не работает (ULS по СП 63); в слоистой проверке это ещё и tensionOverride: false.
            H = 0.2, NLayers = 40, PlateModel = "layered", ConcreteDiagramType = DiagrammType.L3,
            TensionConcrete = false,
            RebarLayers =
            [
                Layer("низ", -0.07),
                Layer("верх", 0.07),
            ],
        };

        static PlateRebarLayer Layer(string name, double z) => new()
        {
            Name = name, InputMode = "direct", Asx = AsPerMeter, Asy = AsPerMeter,
            Zsx = z, Zsy = z, DiameterX = 0.012, DiameterY = 0.012,
        };
    }

    // Усилия: Nx, Ny, Nxy (кН/м), Mx, My, Mxy (кН·м/м). Положительный M растягивает верхнюю грань.
    // A — общий случай прочности, N_n сжимающая на всех направлениях.
    static readonly double[] CaseA = [-50, -30, 15, 18, 8, 10];
    // B — общий случай трещиностойкости, N_n сжимающая на всех направлениях.
    static readonly double[] CaseB = [-20, -10, 8, 12, 5, 7];
    // C — надколонная зона, прочность: Mx и My разных знаков.
    static readonly double[] CaseC = [0, 0, 0, 25, -12, 9];
    // D — надколонная зона, трещиностойкость.
    static readonly double[] CaseD = [0, 0, 0, 16, -14, 7];

    const string Uls = "shell_simpl_capri_uls";
    const string Sls = "shell_simpl_capri_sls";

    static ShellSimplSolver.SolveResult Capri(double[] f, string kind) =>
        ShellSimplSolver.Solve(
            new ShellSimplSolver.SolveParams(f[0], f[1], f[2], f[3], f[4], f[5], kind, StepDeg, 0.3, 1.0, 0.5),
            Fixture.Section(), Fixture.Concrete(), Fixture.Rebar(),
            kind == Uls ? CalcType.C : CalcType.N);

    // Смена знака обеих касательных компонент — то же, что запись проекции с «−».
    static double[] MirrorShear(double[] f) => [f[0], f[1], -f[2], f[3], f[4], -f[5]];
    static double[] With(double[] f, int index, double value) { var c = (double[])f.Clone(); c[index] = value; return c; }

    static double Value(ShellSimplDirectionResult d, string kind) => kind == Uls ? d.Strip.Eta : d.Strip.Acrc_mm;
    static double Mirror(double alphaDeg) => (180.0 - alphaDeg) % 180.0;

    static double MaxOverRange(ShellSimplSolver.SolveResult r, string kind, bool top, double maxAlphaDeg) =>
        r.CapriDirs!.Where(d => d.Top == top && d.Alpha_deg <= maxAlphaDeg && !d.Strip.NoRebar)
            .Select(d => Value(d, kind)).DefaultIfEmpty(0.0).Max();

    // ── Проверка 1: запись с «−Mxy·sin2α» даёт тот же результат на зеркальном угле ─────────────

    [Theory]
    [InlineData(Uls)]
    [InlineData(Sls)]
    public void MirroredShearSign_GivesIdenticalResultAtMirroredAngle(string kind)
    {
        var forces = kind == Uls ? CaseA : CaseB;
        var plus = Capri(forces, kind);
        var minus = Capri(MirrorShear(forces), kind);

        Assert.Equal(36, plus.CapriDirs!.Count);
        foreach (var d in plus.CapriDirs!)
        {
            var m = minus.CapriDirs!.Single(x => Math.Abs(x.Alpha_deg - Mirror(d.Alpha_deg)) < 1e-9);
            Assert.Equal(d.M_n, m.M_n, 9);
            Assert.Equal(d.N_n, m.N_n, 9);
            Assert.Equal(d.Top, m.Top);
            Assert.Equal(Value(d, kind), Value(m, kind), 9);
        }

        Assert.Equal(Value(plus.CriticalTop!, kind), Value(minus.CriticalTop!, kind), 9);
        Assert.Equal(Mirror(plus.CriticalTop!.Alpha_deg), minus.CriticalTop!.Alpha_deg, 9);
    }

    [Fact]
    public void MirroredShearSign_ReferenceValues()
    {
        var plus = Capri(CaseA, Uls);
        var minus = Capri(MirrorShear(CaseA), Uls);
        Assert.Equal(35.0, plus.CriticalTop!.Alpha_deg);
        Assert.Equal(145.0, minus.CriticalTop!.Alpha_deg);
        Assert.InRange(plus.EtaMax!.Value, 0.6636, 0.6646);
        Assert.Equal(plus.EtaMax!.Value, minus.EtaMax!.Value, 9);

        var slsPlus = Capri(CaseB, Sls);
        var slsMinus = Capri(MirrorShear(CaseB), Sls);
        Assert.Equal(35.0, slsPlus.CriticalTop!.Alpha_deg);
        Assert.Equal(145.0, slsMinus.CriticalTop!.Alpha_deg);
        Assert.InRange(slsPlus.CriticalTop!.Strip.Acrc_mm, 0.0531, 0.0541);
        Assert.Equal(slsPlus.CriticalTop!.Strip.Acrc_mm, slsMinus.CriticalTop!.Strip.Acrc_mm, 9);
    }

    // ── Проверка 2: независимая формула с «−» совпадает с проекцией OpenCS ──────────────────────

    [Fact]
    public void RobotStyleProjection_EqualsOpenCsProjectionAtMirroredAngle()
    {
        var r = Capri(CaseA, Uls);
        var (nx, ny, nxy, mx, my, mxy) = (CaseA[0], CaseA[1], CaseA[2], CaseA[3], CaseA[4], CaseA[5]);

        foreach (var d in r.CapriDirs!)
        {
            double a = Mirror(d.Alpha_deg) * Math.PI / 180.0;
            double c2 = Math.Cos(a) * Math.Cos(a), s2 = Math.Sin(a) * Math.Sin(a), s2a = Math.Sin(2.0 * a);
            Assert.Equal(mx * c2 + my * s2 - mxy * s2a, d.M_n, 9);
            Assert.Equal(nx * c2 + ny * s2 - nxy * s2a, d.N_n, 9);
        }

        // Ручной счёт при α=35°: 18·0,67101 + 8·0,32899 + 10·0,93969 = 24,107 кН·м/м.
        var crit = r.CapriDirs!.Single(d => d.Alpha_deg == 35.0);
        Assert.InRange(crit.M_n, 24.106, 24.108);
    }

    // ── Проверка 3: перебор только четверти оборота теряет критическое направление ─────────────

    [Fact]
    public void QuarterTurnScan_MissesCriticalDirection_WhenShearSignIsMirrored()
    {
        var uls = Capri(MirrorShear(CaseA), Uls);
        double full = uls.EtaMax!.Value;
        double quarter = MaxOverRange(uls, Uls, top: true, maxAlphaDeg: 90.0);
        Assert.InRange(full, 0.6636, 0.6646);
        Assert.InRange(quarter, 0.5012, 0.5022);   // критерий занижен на четверть

        var sls = Capri(MirrorShear(CaseB), Sls);
        Assert.True(sls.CriticalTop!.Strip.Cracked);
        Assert.InRange(sls.CriticalTop!.Strip.Acrc_mm, 0.0531, 0.0541);
        Assert.Equal(0.0, MaxOverRange(sls, Sls, top: true, maxAlphaDeg: 90.0)); // трещина не найдена вовсе
    }

    // ── Проверка 4: знак Mxy нельзя менять отдельно от Nxy ──────────────────────────────────────

    [Fact]
    public void FlippingOnlyMxy_ChangesResult_WhenMembraneShearIsPresent()
    {
        double both = Capri(CaseA, Uls).EtaMax!.Value;
        double onlyMxy = Capri(With(CaseA, 5, -CaseA[5]), Uls).EtaMax!.Value;
        Assert.InRange(both, 0.6636, 0.6646);
        Assert.InRange(onlyMxy, 0.6382, 0.6392);

        double slsBoth = Capri(CaseB, Sls).CriticalTop!.Strip.Acrc_mm;
        double slsOnlyMxy = Capri(With(CaseB, 5, -CaseB[5]), Sls).CriticalTop!.Strip.Acrc_mm;
        Assert.InRange(slsBoth, 0.0531, 0.0541);
        Assert.InRange(slsOnlyMxy, 0.0480, 0.0490);
    }

    // ── Проверка 5: Mx и My разных знаков — перебор сам проверяет обе грани ─────────────────────

    [Fact]
    public void MixedSignMoments_ScanFindsCriticalDirectionOnBothFaces()
    {
        var uls = Capri(CaseC, Uls);
        Assert.Equal(15.0, uls.CriticalTop!.Alpha_deg);
        Assert.InRange(uls.CriticalTop!.Strip.Eta, 0.7841, 0.7851);
        Assert.Equal(105.0, uls.CriticalBot!.Alpha_deg);
        Assert.InRange(uls.CriticalBot!.Strip.Eta, 0.4066, 0.4076);

        var sls = Capri(CaseD, Sls);
        Assert.Equal(15.0, sls.CriticalTop!.Alpha_deg);
        Assert.InRange(sls.CriticalTop!.Strip.Acrc_mm, 0.0705, 0.0715);
        Assert.Equal(105.0, sls.CriticalBot!.Alpha_deg);
        Assert.InRange(sls.CriticalBot!.Strip.Acrc_mm, 0.0474, 0.0484);
    }

    // ── Проверка 6: обнуление «разгружающего» момента идёт в запас для своей грани ─────────────

    [Theory]
    [InlineData(Uls)]
    [InlineData(Sls)]
    public void ZeroingRelievingMoment_IsConservativeForItsFace(string kind)
    {
        var forces = kind == Uls ? CaseC : CaseD;
        var full = Capri(forces, kind);
        var myZero = Capri(With(forces, 4, 0.0), kind);  // верх: убран My<0, разгружавший верхнюю грань
        var mxZero = Capri(With(forces, 3, 0.0), kind);  // низ: убран Mx>0, разгружавший нижнюю грань

        Assert.True(Value(myZero.CriticalTop!, kind) > Value(full.CriticalTop!, kind));
        Assert.True(Value(mxZero.CriticalBot!, kind) > Value(full.CriticalBot!, kind));
    }

    // ── Проверки 7–8: независимая слоистая модель ───────────────────────────────────────────────

    static ShellLayeredCheck.Result Layered(double[] f, double lambda, out ShellStrainState state)
    {
        var item = new ShellLoadItem
        {
            Nx = lambda * f[0], Ny = lambda * f[1], Nxy = lambda * f[2],
            Mx = lambda * f[3], My = lambda * f[4], Mxy = lambda * f[5],
        };
        return ShellLayeredCheck.CheckUls(Fixture.Section(), item, Fixture.Concrete(), Fixture.Rebar(),
            CalcType.C, DiagrammType.L3, out state, out _, out _, tensionOverride: false);
    }

    // Предельный множитель нагрузки: наибольший λ, при котором НДС найдено и util ≤ 1 (п. 8.1.30).
    static double LimitLoadFactor(double[] f)
    {
        bool Ok(double lambda) { var r = Layered(f, lambda, out _); return r.Converged && r.Utilization <= 1.0; }
        double lo = 0.0, hi = 1.0;
        while (Ok(hi) && hi < 64.0) { lo = hi; hi *= 2.0; }
        for (int i = 0; i < 30; i++) { double mid = 0.5 * (lo + hi); if (Ok(mid)) lo = mid; else hi = mid; }
        return lo;
    }

    [Fact]
    public void Layered_MirroredShearSign_IsExactReflection()
    {
        var plus = Layered(CaseA, 1.0, out var sp);
        var minus = Layered(MirrorShear(CaseA), 1.0, out var sm);
        Assert.True(plus.Converged && minus.Converged);

        // Совпадение ограничено точностью решателя НДС (невязка tolRes = 1e-3), поэтому допуск относительный.
        static void Near(double expected, double actual) =>
            Assert.Equal(expected, actual, Math.Abs(expected) * 1e-4);

        Near(plus.Utilization, minus.Utilization);
        Near(sp.Eps0x, sm.Eps0x);
        Near(sp.Eps0y, sm.Eps0y);
        Near(sp.Kx, sm.Kx);
        Near(sp.Ky, sm.Ky);
        Near(sp.Gamma0xy, -sm.Gamma0xy);
        Near(sp.Kxy, -sm.Kxy);

        double lPlus = LimitLoadFactor(CaseA);
        double lMinus = LimitLoadFactor(MirrorShear(CaseA));
        Assert.InRange(lPlus, 1.828, 1.832);
        Assert.Equal(lPlus, lMinus, 4);

        // Смена знака одного Mxy — другое напряжённое состояние, в обеих моделях это разгрузка.
        Assert.InRange(LimitLoadFactor(With(CaseA, 5, -CaseA[5])), 2.014, 2.018);
    }

    [Fact]
    public void Layered_ZeroingOneMomentEnvelope_IsConservative()
    {
        double full = LimitLoadFactor(CaseC);
        double myZero = LimitLoadFactor(With(CaseC, 4, 0.0));
        double mxZero = LimitLoadFactor(With(CaseC, 3, 0.0));

        Assert.InRange(full, 1.455, 1.460);
        Assert.InRange(myZero, 1.432, 1.437);
        Assert.InRange(mxZero, 2.392, 2.398);
        Assert.True(Math.Min(myZero, mxZero) < full);
    }
}
