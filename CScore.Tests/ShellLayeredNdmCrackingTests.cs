using System;
using System.Linq;
using Xunit;
using CScore;
using CScore.Fem;

namespace CScore.Tests;

// Слоистая модель обязана брать момент трещинообразования из ДЕФОРМАЦИОННОЙ МОДЕЛИ
// (п. 8.2.8 + 8.2.14 СП 63), а не из формульного Wpl = γ·Wred (п. 8.2.10–8.2.12), который
// норма допускает лишь как упрощение для прямоугольных, тавровых и двутавровых сечений.
//
// Пока M_crc заимствовался из формульного пути, в деформационный расчёт затягивался
// эмпирический коэффициент пластичности γ: acrc,long слоистой менялась на 19 % между
// γ = 1,3 и γ = 1,75 — при том, что диаграмма растянутого бетона эту пластичность уже
// учитывает сама.
//
// Сечение то же, что в ShellCrackingSolverTests: стена h = 200 мм, B25, A500,
// ⌀12 шаг 100 у обеих граней, привязка 35 мм.
public class ShellLayeredNdmCrackingTests
{
    const double H = 0.2, Cover = 0.035, As = 1131e-6;
    const double Mx = 40.0;        // чистый изгиб, кН·м/м — растягивает грань +Z (верх)

    // ── Фикстуры ──────────────────────────────────────────────────────────────────────

    static MaterialChars ConcreteChars(CalcType ct, double rb, double rbt) => new(ct)
    {
        Type = MatType.Concrete, E = 30_000_000.0, Fc = -rb, Ft = rbt,
        Ec0 = -0.002, Ec1 = -0.6 * rb / 30_000_000.0, Ec2 = -0.0035, Ec1Red = -0.0015,
        Et0 = 0.0001, Et1 = 0.6 * rbt / 30_000_000.0, Et2 = 0.00015, Et1Red = 0.00008,
    };

    static MaterialChars RebarChars(CalcType ct, double rs, double rsc) => new(ct)
    {
        Type = MatType.ReSteelF, E = 200_000_000.0, Fc = -rsc, Ft = rs, Ec2 = -0.025, Et2 = 0.025,
    };

    static Material Concrete()
    {
        var m = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0 };
        m.C = ConcreteChars(CalcType.C, 14_500.0, 1_050.0);
        m.CL = ConcreteChars(CalcType.CL, 14_500.0, 1_050.0);
        m.N = ConcreteChars(CalcType.N, 18_500.0, 1_550.0);
        m.NL = ConcreteChars(CalcType.NL, 18_500.0, 1_550.0);
        return m;
    }

    static Material Rebar()
    {
        var m = new Material { Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000.0 };
        m.C = RebarChars(CalcType.C, 435_000.0, 400_000.0);
        m.CL = RebarChars(CalcType.CL, 435_000.0, 400_000.0);
        m.N = RebarChars(CalcType.N, 500_000.0, 500_000.0);
        m.NL = RebarChars(CalcType.NL, 500_000.0, 500_000.0);
        return m;
    }

    static PlateSection Section() => new()
    {
        H = H, NLayers = 40, PlateModel = "layered", ConcreteDiagramType = DiagrammType.L3,
        TensionConcrete = false,
        RebarLayers =
        [
            new PlateRebarLayer { Name = "низ", InputMode = "direct", Asx = As, Asy = As,
                Zsx = -(H / 2 - Cover), Zsy = -(H / 2 - Cover), DiameterX = 0.012, DiameterY = 0.012 },
            new PlateRebarLayer { Name = "верх", InputMode = "direct", Asx = As, Asy = As,
                Zsx = H / 2 - Cover, Zsy = H / 2 - Cover, DiameterX = 0.012, DiameterY = 0.012 },
        ],
    };

    static Diagramm CDiag() => Concrete().GetDiagramms(DiagrammType.L3)![CalcType.N];

    static Diagramm RDiag()
    {
        var r = Rebar();
        return r.GetDiagramms(DiagrammCompatibility.Coerce(r.Type, DiagrammType.L2))![CalcType.N];
    }

    static ShellLoadItem Load(double nx = 0.0) =>
        new() { Nx = nx, Ny = 0.0, Nxy = 0.0, Mx = Mx, My = 0.0, Mxy = 0.0 };

    static ShellStrainState State(ShellLoadItem shell)
    {
        var res = new ShellStrainSolver(Section(), CDiag(), RDiag())
            .Solve([shell.Nx, shell.Ny, shell.Nxy, shell.Mx, shell.My, shell.Mxy]);
        Assert.True(res.Converged, "рабочее НДС не сошлось");
        return res.StrainState;
    }

    /// <summary>Тот самый пробник: (усилия, направление) → (M_crc, НДС при M = M_crc).</summary>
    static ShellCrackingProbe Probe()
    {
        var solver = new ShellCrackingSolver(Section(), CDiag(), RDiag());
        return (target, alongX) => solver.Solve(target, alongX);
    }

    static ShellCrackStripResult TensileStrip(
        WplGammaMethod gamma, ShellCrackingProbe? probe)
    {
        var shell = Load();
        var strips = ShellLayeredCrackWidth.ComputeAll(
            Section(), shell, State(shell),
            Concrete().chars[CalcType.N], Rebar().chars[CalcType.N],
            phi1: 1.4, phi2: 0.5,
            SigmaSCrcMethod.ReleasedConcrete8137, gamma, probe);

        // Mx > 0 растягивает грань +Z (конвенция PlateSection), значит работает верхний слой.
        return strips.Single(s => s.Direction == "x" && s.IsTop);
    }

    /// <summary>Формульный M_crc = Rbt·γ·Wred − N·ex, ровно как его считал ComputeStrip.</summary>
    static double FormulaMcrc(WplGammaMethod gamma, double nDes = 0.0)
    {
        const double h0 = H / 2.0 + (H / 2.0 - Cover), aPrime = H / 2.0 - (H / 2.0 - Cover);
        double alphaFull = 200_000_000.0 / 30_000_000.0;
        ShellSimplSolver.FullSectionProps(H, h0, aPrime, As, 0.0, alphaFull,
            out double aRed, out double iRed);
        double yt = H - (H * H / 2.0 + alphaFull * As * h0) / aRed;
        double wRed = iRed / yt;
        return Math.Max(0.0,
            1_550.0 * ShellSimplSolver.ResolveWplGamma(gamma, As, 1.0, H) * wRed - nDes * wRed / aRed);
    }

    // ── Проверки ──────────────────────────────────────────────────────────────────────

    // Определяющая: с подключённым пробником M_crc полосы — это M_crc деформационной модели,
    // а не Rbt·γ·Wred.
    [Fact]
    public void LayeredStrip_TakesMcrcFromDeformationModel()
    {
        var ndm = new ShellCrackingSolver(Section(), CDiag(), RDiag())
            .Solve([0, 0, 0, Mx, 0, 0], alongX: true);
        Assert.True(ndm.Converged, ndm.Description);

        var strip = TensileStrip(WplGammaMethod.Sp63, Probe());

        Assert.Equal(ndm.Mcrc, strip.Mcrc, 6);
        Assert.True(strip.Mcrc > 1.25 * FormulaMcrc(WplGammaMethod.Sp63),
            $"M_crc полосы {strip.Mcrc:F3} не отличается от формульного при γ = 1,3");
    }

    // Следствие: выбор γ перестаёт влиять на слоистую модель — эмпирический коэффициент
    // из деформационного расчёта уходит совсем.
    [Fact]
    public void LayeredStrip_IgnoresWplGamma_WhenProbeSupplied()
    {
        var sp63 = TensileStrip(WplGammaMethod.Sp63, Probe());
        var snip = TensileStrip(WplGammaMethod.Snip2030184, Probe());

        Assert.Equal(sp63.Mcrc, snip.Mcrc, 6);
        Assert.Equal(sp63.AcrcMm, snip.AcrcMm, 6);
    }

    // σs,crc — напряжение «сразу ПОСЛЕ образования нормальных трещин, определяемое по 8.2.16»
    // (п. 8.2.18). Состояние, на котором останавливается поиск M_crc, — это состояние ДО
    // трещины: растянутый бетон в нём ещё работает и держит почти всё усилие, арматура
    // напряжена слабо. Брать σs,crc оттуда нельзя — нужно решение при том же M = M_crc, но
    // с выключенным растянутым бетоном (сечение с трещиной по п. 8.2.16).
    [Fact]
    public void LayeredStrip_TakesSigmaSCrcFromCrackedState_NotFromPreCrackState()
    {
        var ndm = new ShellCrackingSolver(Section(), CDiag(), RDiag())
            .Solve([0, 0, 0, Mx, 0, 0], alongX: true);
        Assert.True(ndm.Converged && ndm.StrainState != null, ndm.Description);

        // Эталон считается независимо: та же задача при M = M_crc в сечении с трещиной.
        var cracked = new ShellStrainSolver(Section(), CDiag(), RDiag(), tensionOverride: false)
            .Solve([0, 0, 0, ndm.Mcrc, 0, 0]);
        Assert.True(cracked.Converged, "эталонное решение сечения с трещиной не сошлось");
        double expected = Math.Min(
            200_000_000.0 * cracked.StrainState.EpsX(H / 2.0 - Cover), 500_000.0);

        var strip = TensileStrip(WplGammaMethod.Sp63, Probe());

        Assert.True(expected > 0.0, "в состоянии с трещиной арматура не растянута");
        Assert.Equal(expected, strip.SigmaSCrc, 3);
        Assert.Equal(Math.Clamp(1.0 - 0.8 * strip.SigmaSCrc / strip.SigmaS, 0.1, 1.0), strip.PsiS, 9);

        // И это заметно больше, чем в состоянии до трещины, — там усилие несёт бетон.
        double preCrack = 200_000_000.0 * ndm.StrainState!.EpsX(H / 2.0 - Cover);
        Assert.True(expected > 1.5 * preCrack,
            $"σs,crc из состояния с трещиной {expected / 1000.0:F1} МПа не отличается от " +
            $"состояния до трещины {preCrack / 1000.0:F1} МПа");
    }

    // Сходимость на реальном двухосном сочетании: низ стены, сочетание 3 из сверки с
    // Апхадзе (Nx = +350, Ny = −900, Nxy = 50, Mx = 16,9, My = 40,1, Mxy = 7,4). Поиск с
    // включённой растянутой ветвью бетона здесь и ломался — решение не находилось ни при
    // одном моменте, и слоистая молча уходила на формульный M_crc.
    [Fact]
    public void Solver_Converges_OnBiaxialWallCombination()
    {
        double[] wall = [350.0, -900.0, 50.0, 16.9, 40.1, 7.4];
        var solver = new ShellCrackingSolver(Section(), CDiag(), RDiag());

        var alongX = solver.Solve(wall, alongX: true);
        var alongY = solver.Solve(wall, alongX: false);

        Assert.True(alongX.Converged, $"направление x: {alongX.Description}");
        Assert.True(alongY.Converged, $"направление y: {alongY.Description}");
    }

    // Без пробника поведение прежнее — формульный запасной путь остаётся для вызовов,
    // которым решатель НДС недоступен (ComputeAcrcStrip, старые тесты CSfea).
    [Fact]
    public void LayeredStrip_FallsBackToFormulaMcrc_WithoutProbe()
    {
        var strip = TensileStrip(WplGammaMethod.Sp63, probe: null);

        Assert.Equal(FormulaMcrc(WplGammaMethod.Sp63), strip.Mcrc, 6);
    }

    // Ответ на вопрос «1,3·Wred или 1,75 ближе к истине»: эквивалентная γ деформационной
    // модели для этого сечения. Считается как γ_экв = 1,3·M_crc(НДМ)/M_crc(γ = 1,3).
    [Fact]
    public void EquivalentGamma_IsCloserTo175ThanTo13()
    {
        var ndm = new ShellCrackingSolver(Section(), CDiag(), RDiag())
            .Solve([0, 0, 0, Mx, 0, 0], alongX: true);
        Assert.True(ndm.Converged);

        double gammaEquiv = 1.3 * ndm.Mcrc / FormulaMcrc(WplGammaMethod.Sp63);

        Assert.InRange(gammaEquiv, 1.7, 1.9);
        Assert.True(Math.Abs(gammaEquiv - 1.75) < Math.Abs(gammaEquiv - 1.3));
    }

    // Но ответ не универсален: γ_экв зависит от продольной силы. Формульный путь учитывает N
    // слагаемым −N·ex при неизменном γ, а деформационная модель — тем, что растяжение выбирает
    // часть предельной деформации бетона ещё до момента. При заметной растягивающей N эти два
    // учёта расходятся, и эквивалентная γ падает ниже 1,3, то есть формула с γ = 1,75 (и даже
    // с 1,3) момент трещинообразования ЗАВЫШАЕТ, а ширину раскрытия трещин занижает.
    //
    // γ_экв выражается из M_crc = Rbt·γ·Wred − N·ex: γ_экв = (M_crc + N·ex)/(Rbt·Wred).
    [Fact]
    public void EquivalentGamma_DropsBelow13_UnderSignificantTension()
    {
        const double n = 350.0;   // сочетание 3 низа стены: Nx = +350 кН/м
        var ndm = new ShellCrackingSolver(Section(), CDiag(), RDiag())
            .Solve([n, 0, 0, Mx, 0, 0], alongX: true);
        Assert.True(ndm.Converged, ndm.Description);

        const double h0 = H / 2.0 + (H / 2.0 - Cover), aPrime = H / 2.0 - (H / 2.0 - Cover);
        double alphaFull = 200_000_000.0 / 30_000_000.0;
        ShellSimplSolver.FullSectionProps(H, h0, aPrime, As, 0.0, alphaFull,
            out double aRed, out double iRed);
        double yt = H - (H * H / 2.0 + alphaFull * As * h0) / aRed;
        double wRed = iRed / yt;
        double gammaEquiv = (ndm.Mcrc + n * wRed / aRed) / (1_550.0 * wRed);

        Assert.InRange(gammaEquiv, 1.15, 1.35);
        Assert.True(ndm.Mcrc < FormulaMcrc(WplGammaMethod.Sp63, nDes: n),
            $"деформационный M_crc {ndm.Mcrc:F3} не ниже формульного при γ = 1,3 " +
            $"{FormulaMcrc(WplGammaMethod.Sp63, nDes: n):F3}");
    }
}
