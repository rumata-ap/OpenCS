using CScore;
using CScore.Sp63.CrackWidth;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63CrackWidth;

// Sp63CrackWidthChecker переиспользует геометрию/раскладку арматуры Sp63RebarLayoutAnalyzer
// (общую с Sp63NormalChecker) и саму формулу СП63 8.2.9-8.2.16 — уже проверенную
// ShellSimplSolver.ComputeStripSls (ShellSimplSolverTests). Поэтому основная проверка здесь —
// эквивалентность результата Checker'а прямому вызову ComputeStripSls с усилиями/площадью
// арматуры, делёнными на реальную ширину b (стержень сводится к той же полосе, что и плита,
// но с b вместо расчётной полосы 1 м), плюс применимость (геометрия, раскладка, момент).
public class Sp63CrackWidthCheckerTests
{
    static Material ConcreteSls(int id, double rbSerKPa, double rbtSerKPa, double ebKPa)
    {
        var material = new Material { Id = id, Tag = $"B-{id}", Type = MatType.Concrete, E = ebKPa };
        material.N = new MaterialChars(CalcType.N)
        {
            Type = MatType.Concrete,
            Fc = -rbSerKPa,
            Ft = rbtSerKPa,
            E = ebKPa
        };
        return material;
    }

    static Material RebarSls(int id, double rsSerKPa, double esKPa = 200_000_000.0)
    {
        var material = new Material { Id = id, Tag = $"A-{id}", Type = MatType.ReSteelU, E = esKPa };
        material.N = new MaterialChars(CalcType.N)
        {
            Type = MatType.ReSteelU,
            Ft = rsSerKPa,
            Fc = -rsSerKPa,
            E = esKPa
        };
        return material;
    }

    static CrossSection Rectangle(double width, double height, Material concrete)
    {
        var section = new CrossSection { Tag = "test" };
        var area = new MaterialArea
        {
            Category = AreaCategory.Region,
            Material = concrete,
            MaterialId = concrete.Id
        };
        area.Contours.Add(new Contour(
            [-width / 2, width / 2, width / 2, -width / 2, -width / 2],
            [-height / 2, -height / 2, height / 2, height / 2, -height / 2], "hull")
        { Type = ContourType.Hull });
        area.SetWKT();
        section.Areas.Add(area);
        return section;
    }

    static void AddBar(CrossSection section, double y, double area, Material rebar, double diameter)
    {
        var group = new MaterialArea
        {
            Category = AreaCategory.RebarGroup,
            Material = rebar,
            MaterialId = rebar.Id
        };
        group.Fibers.Add(new Fiber { TypeFiber = FiberType.point, X = 0.0, Y = y, Area = area, Diameter = diameter });
        section.Areas.Add(group);
    }

    /// <summary>
    /// Сечение b×h с двумя слоями арматуры на координатах ±0.11 (Y): при tensionDirection = +1
    /// (M > 0) даёт h0 = 0.26, a' = 0.04 — как в примерах ShellSimplSolverTests.
    /// </summary>
    static (CrossSection Section, Material Concrete, Material Rebar) BuildSection(
        double b, double h, double asT, double asC, double ds,
        double rbSerMPa = 18.5, double rbtSerMPa = 1.55, double ebMPa = 30_000.0,
        double rsSerMPa = 500.0) =>
        BuildSectionAt(b, h, 0.11, -0.11, asT, asC, ds, rbSerMPa, rbtSerMPa, ebMPa, rsSerMPa);

    static (CrossSection Section, Material Concrete, Material Rebar) BuildSectionAt(
        double b, double h, double tensionY, double compressionY,
        double asT, double asC, double ds,
        double rbSerMPa, double rbtSerMPa, double ebMPa, double rsSerMPa)
    {
        var concrete = ConcreteSls(1, rbSerMPa * 1000.0, rbtSerMPa * 1000.0, ebMPa * 1000.0);
        var rebar = RebarSls(2, rsSerMPa * 1000.0);
        var section = Rectangle(b, h, concrete);
        AddBar(section, tensionY, asT, rebar, ds);
        AddBar(section, compressionY, asC, rebar, ds);
        return (section, concrete, rebar);
    }

    static Sp63CrackWidthOptions Options(double phi1 = 1.0, double phi2 = 0.5, double acrcLimMm = 0.4) =>
        new(Sp63NormalShapeKind.Rectangular, Sp63NormalAxis.Mx, phi1, phi2, acrcLimMm);

    [Fact]
    public void Check_PureBending_MatchesDirectComputeStripSlsOnPerWidthQuantities()
    {
        const double b = 0.5, h = 0.3, h0 = 0.26, aPrime = 0.04;
        const double asT = 20.36e-4, asC = 1e-8, ds = 0.036;
        const double m = 80.0;
        var (section, concrete, rebar) = BuildSection(b, h, asT, asC, ds);
        var options = Options(phi1: 1.4, phi2: 0.5, acrcLimMm: 0.4);

        var result = Sp63CrackWidthChecker.Check(section, new LoadItem { N = 0, Mx = m, My = 0 },
            CalcType.N, options);

        Assert.Equal(Sp63CrackWidthStatus.Calculated, result.Status);
        var expected = ShellSimplSolver.ComputeStripSls(
            m / b, 0.0 / b, h, h0, aPrime, asT / b, asC / b, ds,
            concrete.GetChars(CalcType.N)!, rebar.GetChars(CalcType.N)!,
            options.Phi1, options.Phi2, options.AcrcLimMm);

        var detail = Assert.Single(result.Details);
        Assert.Equal(expected.Acrc_mm, detail.Applied, 6);
        Assert.Equal(options.AcrcLimMm, detail.Allowable, 9);
        Assert.Equal(expected.Cracked, result.Cracked);
        Assert.Equal(expected.Acrc_mm <= options.AcrcLimMm + 1e-9, result.LimitPassed);
    }

    [Fact]
    public void Check_EccentricTension_MatchesDirectComputeStripSlsOnPerWidthQuantities()
    {
        const double b = 0.5, h = 0.3, h0 = 0.26, aPrime = 0.04;
        const double asT = 20.36e-4, asC = 20.36e-4, ds = 0.036;
        const double m = 90.0, n = 120.0;
        var (section, concrete, rebar) = BuildSection(b, h, asT, asC, ds);
        var options = Options(phi1: 1.0, phi2: 0.5, acrcLimMm: 0.4);

        var result = Sp63CrackWidthChecker.Check(section, new LoadItem { N = n, Mx = m, My = 0 },
            CalcType.N, options);

        Assert.Equal(Sp63CrackWidthStatus.Calculated, result.Status);
        var expected = ShellSimplSolver.ComputeStripSls(
            m / b, n / b, h, h0, aPrime, asT / b, asC / b, ds,
            concrete.GetChars(CalcType.N)!, rebar.GetChars(CalcType.N)!,
            options.Phi1, options.Phi2, options.AcrcLimMm);

        var detail = Assert.Single(result.Details);
        Assert.Equal(expected.Acrc_mm, detail.Applied, 6);
        Assert.True(result.Cracked);
        Assert.True(detail.Applied > 0.0);
    }

    [Fact]
    public void Check_ZeroMoment_ReturnsNotApplicable()
    {
        var (section, _, _) = BuildSection(0.5, 0.3, 20.36e-4, 20.36e-4, 0.036);
        var result = Sp63CrackWidthChecker.Check(section, new LoadItem { N = 100, Mx = 0, My = 0 },
            CalcType.N, Options());

        Assert.Equal(Sp63CrackWidthStatus.NotApplicable, result.Status);
        Assert.Contains(result.ApplicabilityMessages, message => message.Code == "zero_moment");
        Assert.Contains(result.InformationalMessages, message => message.Code == "suggest_full_model");
    }

    [Fact]
    public void Check_BiaxialMoment_ReturnsNotApplicable()
    {
        var (section, _, _) = BuildSection(0.5, 0.3, 20.36e-4, 20.36e-4, 0.036);
        var result = Sp63CrackWidthChecker.Check(section, new LoadItem { N = 0, Mx = 80, My = 10 },
            CalcType.N, Options());

        Assert.Equal(Sp63CrackWidthStatus.NotApplicable, result.Status);
        Assert.Contains(result.ApplicabilityMessages, message => message.Code == "biaxial_load");
    }

    [Fact]
    public void Check_InsufficientRebarLayers_ReturnsNotApplicable()
    {
        var concrete = ConcreteSls(1, 18_500.0, 1_550.0, 30_000_000.0);
        var rebar = RebarSls(2, 500_000.0);
        var section = Rectangle(0.5, 0.3, concrete);
        AddBar(section, 0.11, 20.36e-4, rebar, 0.036);

        var result = Sp63CrackWidthChecker.Check(section, new LoadItem { N = 0, Mx = 80, My = 0 },
            CalcType.N, Options());

        Assert.Equal(Sp63CrackWidthStatus.NotApplicable, result.Status);
        Assert.Contains(result.ApplicabilityMessages, message => message.Code == "insufficient_rebar_layers");
        // Причина неприменимости распознана общим Sp63RebarLayoutAnalyzer — переиспользует
        // ключ локализации Sp63Normal_InsufficientRebarLayers, см. Sp63CrackWidthMessage.
        Assert.Equal("Sp63Normal_InsufficientRebarLayers",
            result.ApplicabilityMessages.Single(m => m.Code == "insufficient_rebar_layers").Text);
    }

    [Fact]
    public void Check_NonFiniteLoad_ReturnsInvalidInput()
    {
        var (section, _, _) = BuildSection(0.5, 0.3, 20.36e-4, 20.36e-4, 0.036);
        var result = Sp63CrackWidthChecker.Check(section,
            new LoadItem { N = 0, Mx = double.NaN, My = 0 }, CalcType.N, Options());

        Assert.Equal(Sp63CrackWidthStatus.InvalidInput, result.Status);
    }

    [Fact]
    public void Check_InvalidAcrcLimit_ReturnsInvalidInput()
    {
        var (section, _, _) = BuildSection(0.5, 0.3, 20.36e-4, 20.36e-4, 0.036);
        var result = Sp63CrackWidthChecker.Check(section,
            new LoadItem { N = 0, Mx = 80, My = 0 }, CalcType.N, Options(acrcLimMm: 0.0));

        Assert.Equal(Sp63CrackWidthStatus.InvalidInput, result.Status);
    }

    // Пример 47 («Пособие СП63.13330.2018», раздел 4): плита фундамента, h=300, b=1150, a=42 мм,
    // B15 (Rb,ser=11 МПа, Rbt,ser=1.1 МПа, Eb=24000 МПа — как в примере 48), As=923 мм² (6⌀14,
    // A400, Es=200000 МПа), чистый изгиб без сжатой арматуры. Книга проверяет условие (4.32) и
    // приходит к выводу, что определяющим является продолжительное раскрытие трещин при
    // M = Ml = 50 кН·м (пример также считает кратковременное M = Ml+Msh = 60 кН·м только для
    // проверки применимости (4.32), не для итогового acrc) — поэтому здесь воспроизводится именно
    // расчёт при M = 50 кН·м, φ1 = 1.4 (продолжительное), φ2 = 0.5, φ3 = 1.0 (N = 0).
    // Книга получает Mcrc, ζ (zs) и Acrc через табличный метод старого СНиП (Wpl = 1.75·Wred без
    // арматуры, ζ по графику рис. 4.3), а не через ф. (8.134)/(8.135) действующего СП63 — точного
    // числового совпадения не ожидается (см. также Example48-тест в ShellSimplSolverTests), но
    // zs и σs у обоих методов сходятся почти точно (для одиночного армирования при N=0 оба сводятся
    // к σs=M/(zs·As)), поэтому для них используется узкий допуск, а для Acrc — широкий (методики
    // расходятся через Mcrc/ψs).
    [Fact]
    public void Check_Example47_PlateFoundation_MatchesBookWithinMethodTolerance()
    {
        const double b = 1.15, h = 0.3;
        const double asT = 923e-6, asC = 1e-8, ds = 0.014;
        const double m = 50.0; // Ml — продолжительная часть момента, определяющая проверку
        var (section, _, _) = BuildSectionAt(b, h, tensionY: 0.108, compressionY: -0.108,
            asT, asC, ds, rbSerMPa: 11.0, rbtSerMPa: 1.1, ebMPa: 24_000.0, rsSerMPa: 400.0);
        var options = Options(phi1: 1.4, phi2: 0.5, acrcLimMm: 0.3);

        var result = Sp63CrackWidthChecker.Check(section, new LoadItem { N = 0, Mx = m, My = 0 },
            CalcType.N, options);

        Assert.Equal(Sp63CrackWidthStatus.Calculated, result.Status);
        Assert.True(result.Cracked);
        var detail = Assert.Single(result.Details);

        double sigmaS = result.Variables["sigma_s"];
        double ls = result.Variables["ls"];
        // Книга: σs = 235.9 МПа — для одиночного армирования при N=0 обе методики сводятся
        // к σs=M/(zs·As), а их zs почти совпадает (0.2293 м у ф. (8.2.28) против 0.2296 м по
        // графику ζ книги), поэтому здесь оправдан узкий допуск.
        Assert.InRange(sigmaS, 235.9 * 0.9, 235.9 * 1.1);
        // Книга: ls капируется на 400 мм (> 40·ds = 560 ... здесь 40·ds=560мм, но предел по норме
        // и there 400 мм абсолютный максимум) — оба метода упираются в один и тот же абсолютный
        // предел ls ≤ 400 мм независимо от методики вычисления сырого ls.
        Assert.Equal(0.4, ls, 3);
        // Книга: acrc = 0.155 мм (табличный метод старого СНиП: Wpl = 1.75·Wred, ζ по графику
        // рис. 4.3). Здесь ψs замыкается по напряжениям (ф. 8.137), и acrc получается ниже
        // книги на ~21%; НДМ на том же примере даёт 0.132 мм, то есть формульный и
        // деформационный пути OpenCS сходятся между собой, а расходятся оба с табличной
        // методикой книги. Значение закреплено, чтобы изменение методики было видно явно.
        Assert.Equal(0.1227, detail.Applied, 4);
        Assert.True(result.LimitPassed);
    }

    [Fact]
    public void Check_NotCracked_ReturnsZeroAcrcAndPassed()
    {
        var (section, _, _) = BuildSection(0.5, 0.3, 20.36e-4, 1e-8, 0.036);
        var result = Sp63CrackWidthChecker.Check(section,
            new LoadItem { N = 0, Mx = 1.0, My = 0 }, CalcType.N, Options());

        Assert.Equal(Sp63CrackWidthStatus.Calculated, result.Status);
        Assert.False(result.Cracked);
        Assert.Equal(0.0, Assert.Single(result.Details).Applied);
        Assert.True(result.LimitPassed);
        Assert.Contains(result.InformationalMessages, message => message.Code == "not_cracked");
    }
}
