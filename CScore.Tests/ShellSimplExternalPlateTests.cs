using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;
using CScore;

namespace CScore.Tests;

// Сверка упрощённой проверки плиты по Капра-Мори (ShellSimplSolver, shell_simpl_capri_*)
// с независимо написанным расчётом стороннего автора (таблица MS Excel, метод Капра-Мори).
//
// Плита h = 200 мм, B25, A500. Низ: ⌀12 шаг 200 основная сетка + ⌀12 шаг 200 дополнительная
// в каждом направлении (As = 11,31 см²/м). Верх: только основная сетка (As = 5,655 см²/м).
// Привязка центра арматуры к грани 35 мм, h0 = 165 мм.
//
// В присланной таблице оси переставлены относительно OpenCS (с cos²α идёт момент «вдоль Y»)
// и положительный момент растягивает НИЖНЮЮ грань. Приведение к конвенции OpenCS:
// Mx(OpenCS) = −My(таблица), My(OpenCS) = −Mx(таблица), Mxy(OpenCS) = −Mxy(таблица).
public class ShellSimplExternalPlateTests
{
    const double StepDeg = 0.1;

    static class Fixture
    {
        public const double AsMesh = 565.5e-6;          // м²/м, ⌀12 шаг 200
        public const double AsBot = 2.0 * AsMesh;       // основная + дополнительная сетка
        public const double AsTop = AsMesh;
        public const double Cover = 0.035;              // привязка центра арматуры к грани

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
            m.C = RebarChars(CalcType.C, 435_000.0, 400_000.0);
            m.CL = RebarChars(CalcType.CL, 435_000.0, 400_000.0);
            m.N = RebarChars(CalcType.N, 500_000.0, 500_000.0);
            m.NL = RebarChars(CalcType.NL, 500_000.0, 500_000.0);
            return m;
        }

        public static PlateSection Section() => new()
        {
            H = 0.2, NLayers = 40, PlateModel = "layered", ConcreteDiagramType = DiagrammType.L3,
            TensionConcrete = false,
            RebarLayers =
            [
                Layer("низ", -(0.1 - Cover), AsBot),
                Layer("верх", 0.1 - Cover, AsTop),
            ],
        };

        static PlateRebarLayer Layer(string name, double z, double As) => new()
        {
            Name = name, InputMode = "direct", Asx = As, Asy = As,
            Zsx = z, Zsy = z, DiameterX = 0.012, DiameterY = 0.012,
        };
    }

    // Усилия присланной таблицы, кН·м/м: (M вдоль Y, M вдоль X, Mxy).
    static readonly double[] ExtUls = [60.0, 50.0, 6.0];   // сочетание 1, расчётные
    static readonly double[] ExtShort = [55.0, 45.0, 5.0]; // сочетание 2, нормативные полные
    static readonly double[] ExtLong = [45.0, 35.0, 3.0];  // сочетание 3, нормативные длительные

    // Приведение к конвенции OpenCS: перестановка осей + смена знака (положительный момент
    // растягивает верхнюю грань).
    static (double Mx, double My, double Mxy) ToOpenCs(double[] ext) => (-ext[0], -ext[1], -ext[2]);

    const string Uls = "shell_simpl_capri_uls";
    const string Sls = "shell_simpl_capri_sls";

    static ShellSimplSolver.SolveResult Capri(double[] ext, string kind, double phi1 = 1.0,
        SigmaSCrcMethod sigmaSCrc = SigmaSCrcMethod.ReleasedConcrete8137,
        WplGammaMethod wplGamma = WplGammaMethod.Sp63)
    {
        var (mx, my, mxy) = ToOpenCs(ext);
        return ShellSimplSolver.Solve(
            new ShellSimplSolver.SolveParams(0, 0, 0, mx, my, mxy, kind, StepDeg, 0.3, phi1, 0.5,
                sigmaSCrc, wplGamma),
            Fixture.Section(), Fixture.Concrete(), Fixture.Rebar(),
            kind == Uls ? CalcType.C : CalcType.N);
    }

    // Георгий Апхадзе сообщил, что в своей таблице принял Wpl = 1,75·Wred (СНиП 2.03.01-84*),
    // а не нормативные 1,3 СП 63. Полное воспроизведение его цепочки — это γ = 1,75 плюс
    // замыкание ψs через момент, ф. (8.138).
    static (double Long, double Short) CrackWidthsWith(SigmaSCrcMethod m, WplGammaMethod g)
    {
        var full10 = Capri(ExtShort, Sls, 1.0, m, g).CapriDirs!;
        var long10 = Capri(ExtLong, Sls, 1.0, m, g).CapriDirs!;
        var long14 = Capri(ExtLong, Sls, 1.4, m, g).CapriDirs!;

        double aLong = 0, aShort = 0;
        for (int i = 0; i < full10.Count; i++)
        {
            if (full10[i].Strip.NoRebar || full10[i].Top) continue;
            double l = long14[i].Strip.Acrc_mm;
            double sh = full10[i].Strip.Acrc_mm - long10[i].Strip.Acrc_mm + l;
            if (l > aLong) aLong = l;
            if (sh > aShort) aShort = sh;
        }
        return (aLong, aShort);
    }

    [Fact]
    public void ExternalCrackWidths_AreReproducedExactly_WithGamma175AndMomentRoute()
    {
        var (aLong, aShort) = CrackWidthsWith(
            SigmaSCrcMethod.CrackingMoment8138, WplGammaMethod.Snip2030184);

        Assert.Equal(0.260, aLong, 3);
        Assert.Equal(0.329, aShort, 3);
    }

    // ψs = 1 − 0,8·σs,crc/σs (п. 8.2.18) замыкается двумя способами. По напряжениям, ф. (8.137),
    // σs,crc — приращение от сброса растянутого бетона рабочей зоны. Через момент, ф. (8.138),
    // σs,crc получается подстановкой Mcrc в ту же упругую модель сечения с трещиной, отчего
    // отношение вырождается в Mcrc/M и наследует точность формульного Mcrc = Rbt,ser·1,3·Wred.
    // Коэффициент Wpl = 1,3·Wred занижает момент образования трещин, поэтому моментный вариант
    // даёт меньшую σs,crc, больший ψs и БОЛЬШУЮ ширину раскрытия: погрешность идёт в запас.
    [Fact]
    public void SigmaSCrcMethod_MomentVariantIsConservative()
    {
        var stress = Capri(ExtLong, Sls, 1.4, SigmaSCrcMethod.ReleasedConcrete8137).CapriDirs!;
        var moment = Capri(ExtLong, Sls, 1.4, SigmaSCrcMethod.CrackingMoment8138).CapriDirs!;

        int compared = 0;
        for (int i = 0; i < stress.Count; i++)
        {
            var s = stress[i].Strip;
            var m = moment[i].Strip;

            // Трещина, σs, Mcrc и ls от способа не зависят — различаются только σs,crc и ψs.
            Assert.Equal(s.Cracked, m.Cracked);
            Assert.Equal(s.Sigma_s_MPa, m.Sigma_s_MPa, 9);
            Assert.Equal(s.Mcrc, m.Mcrc, 9);
            Assert.Equal(s.Ls_m, m.Ls_m, 9);

            // Сравнивать есть что только там, где трещина есть и ф. (8.137) не упёрлась
            // в нормативную границу σs,crc ≤ σs.
            if (!s.Cracked || s.Sigma_s_crc_MPa >= s.Sigma_s_MPa - 1e-9) continue;

            compared++;
            Assert.True(m.Sigma_s_crc_MPa < s.Sigma_s_crc_MPa);
            Assert.True(m.Psi_s > s.Psi_s);
            Assert.True(m.Acrc_mm > s.Acrc_mm);
        }

        Assert.True(compared > 0, "не нашлось направлений для сравнения способов σs,crc");
    }

    // ── Проверка 1: проекция усилий и критическое направление ──────────────────────────────

    [Fact]
    public void CriticalDirection_MatchesAnalyticalPrincipalAngle()
    {
        var r = Capri(ExtUls, Uls);

        // M_n(α) = −(60cos²α + 50sin²α + 6sin2α) = −(55 + 5cos2α + 6sin2α);
        // максимум по модулю: 55 + √(5² + 6²) при tg2α = 6/5.
        double mMax = 55.0 + Math.Sqrt(25.0 + 36.0);
        double alphaMax = 0.5 * Math.Atan2(6.0, 5.0) * 180.0 / Math.PI;

        var crit = r.CapriDirs!.Where(d => !d.Strip.NoRebar).MaxBy(d => d.Strip.Eta)!;

        Assert.False(crit.Top);                                   // растянута нижняя грань
        Assert.Equal(alphaMax, crit.Alpha_deg, 1);                // 25,1°
        Assert.Equal(-mMax, crit.M_n, 3);
        Assert.Equal(Fixture.AsBot, crit.Strip.As_t, 12);         // проекция сетки: As_x = As_y
        Assert.Equal(Fixture.AsTop, crit.Strip.As_c, 12);
    }

    // ── Проверка 2: прочность ──────────────────────────────────────────────────────────────

    [Fact]
    public void UlsUtilisation_MatchesExternalCalculation()
    {
        var r = Capri(ExtUls, Uls);
        var crit = r.CapriDirs!.Where(d => !d.Strip.NoRebar).MaxBy(d => d.Strip.Eta)!;

        double rs = 435e3, rsc = 400e3, rb = 14.5e3, h0 = 0.165, aPrime = 0.035;

        // По ф. (8.4) высота сжатой зоны с учётом сжатой арматуры меньше 2a', поэтому
        // работает ветвь п. 8.1.9: момент относительно равнодействующей сжатой зоны при
        // исключённой сжатой арматуре — ровно то же, что считает присланная таблица.
        double xFormula84 = (rs * Fixture.AsBot - rsc * Fixture.AsTop) / rb;
        Assert.True(xFormula84 <= 2.0 * aPrime);

        double xNoCompression = rs * Fixture.AsBot / rb;
        double mUlt = rs * Fixture.AsBot * (h0 - 0.5 * xNoCompression);

        Assert.Equal(xNoCompression, crit.Strip.Xm, 6);
        Assert.Equal(mUlt, crit.Strip.M_ult, 3);
        Assert.Equal(0.862, crit.Strip.Eta, 3);
        Assert.Contains("8.1.9", crit.Strip.Case);
    }

    // Ветвь (8.5) с учётом сжатой арматуры занижает предельный момент при x ≤ 2a′:
    // равнодействующая сжатия уезжает к грани, плечо внутренней пары сокращается.
    [Fact]
    public void Sp63_8_1_9_Branch_IsNotLowerThanFormula85()
    {
        var crit = Capri(ExtUls, Uls).CapriDirs!.Where(d => !d.Strip.NoRebar).MaxBy(d => d.Strip.Eta)!;

        double rs = 435e3, rsc = 400e3, rb = 14.5e3, h0 = 0.165, aPrime = 0.035;
        double x85 = (rs * Fixture.AsBot - rsc * Fixture.AsTop) / rb;
        double mUlt85 = rb * x85 * (h0 - 0.5 * x85) + rsc * Fixture.AsTop * (h0 - aPrime);

        Assert.True(crit.Strip.M_ult > mUlt85);
    }

    // ── Проверка 3: трещиностойкость ───────────────────────────────────────────────────────

    // acrc,long = acrc(длит., φ1 = 1,4);
    // acrc,short = acrc(полн., φ1 = 1,0) − acrc(длит., φ1 = 1,0) + acrc(длит., φ1 = 1,4).
    static (double Long, double Short, double AlphaLong, double AlphaShort) CrackWidths()
    {
        var full10 = Capri(ExtShort, Sls, 1.0).CapriDirs!;
        var long10 = Capri(ExtLong, Sls, 1.0).CapriDirs!;
        var long14 = Capri(ExtLong, Sls, 1.4).CapriDirs!;

        double aLong = 0, aShort = 0, alphaLong = 0, alphaShort = 0;
        for (int i = 0; i < full10.Count; i++)
        {
            if (full10[i].Strip.NoRebar || full10[i].Top) continue;
            double l = long14[i].Strip.Acrc_mm;
            double s = full10[i].Strip.Acrc_mm - long10[i].Strip.Acrc_mm + l;
            if (l > aLong) { aLong = l; alphaLong = long14[i].Alpha_deg; }
            if (s > aShort) { aShort = s; alphaShort = full10[i].Alpha_deg; }
        }
        return (aLong, aShort, alphaLong, alphaShort);
    }

    [Fact]
    public void CrackWidths_AreAssembledPerSp63AndCompared()
    {
        var (aLong, aShort, _, _) = CrackWidths();

        Assert.True(aShort > aLong);
        Assert.InRange(aLong, 0.20, 0.40);
        Assert.InRange(aShort, 0.25, 0.45);
    }

    // Весь разрыв по ширине раскрытия сидит в моменте трещинообразования: формульный
    // Mcrc = Rbt,ser·1,3·Wred заведомо ниже найденного бисекцией по трёхлинейной диаграмме,
    // а ψs = 1 − 0,8·Mcrc/M линейно переносит эту разницу в acrc.
    [Fact]
    public void CrackWidthGap_IsExplainedByCrackingMomentAlone()
    {
        var long14 = Capri(ExtLong, Sls, 1.4).CapriDirs!;
        var crit = long14.Where(d => !d.Strip.NoRebar && !d.Top).MaxBy(d => d.Strip.Acrc_mm)!;

        double mcrcNdm = new CrackWidthSolver(Strip(), calcCrc: CalcType.N, calcService: CalcType.N,
                calcServiceLong: CalcType.N, phi2: 0.5, acrcUltLong: 0.3, acrcUltShort: 0.4)
            .Compute(N: 0.0, mxLong: crit.M_n, mxTotal: crit.M_n).Mcrc;

        Assert.True(mcrcNdm > crit.Strip.Mcrc);

        double psiNdm = Math.Clamp(1.0 - 0.8 * mcrcNdm / Math.Abs(crit.M_n), 0.1, 1.0);
        double acrcWithNdmMcrc = crit.Strip.Acrc_mm * psiNdm / crit.Strip.Psi_s;

        // Присланный расчёт: acrc,long = 0,260 мм.
        Assert.Equal(0.260, acrcWithNdmMcrc, 2);
    }

    // ── Проверка 4: независимые пути расчёта OpenCS на той же полосе ───────────────────────

    // Полоса шириной 1 м как обычное сечение: бетон B25 + две группы стержней.
    public static CrossSection Strip(int ny = 200)
    {
        const double H = 0.2, B = 1.0;

        var concreteMat = Fixture.Concrete();
        var x = new[] { -B / 2, B / 2, B / 2, -B / 2, -B / 2 };
        var y = new[] { -H / 2, -H / 2, H / 2, H / 2, -H / 2 };
        var concrete = new MaterialArea
        {
            Id = 101, Tag = "Бетон B25", Category = AreaCategory.Region,
            Material = concreteMat, MaterialId = concreteMat.Id,
            DiagrammType = DiagrammType.L3, Hull = new Contour(x, y, "hull")
        };
        concrete.SetWKT();
        concrete.SliceXY(nx: 4, ny: ny);

        var rebarMat = Fixture.Rebar();
        var areas = new List<MaterialArea> { concrete };
        int id = 102;
        foreach (var (tag, z, As) in new[]
                 {
                     ("низ", -(H / 2 - Fixture.Cover), Fixture.AsBot),
                     ("верх", H / 2 - Fixture.Cover, Fixture.AsTop),
                 })
        {
            var group = new MaterialArea
            {
                Id = id, Tag = tag, Category = AreaCategory.RebarGroup,
                Material = rebarMat, MaterialId = rebarMat.Id,
                DiagrammType = DiagrammType.L2, HostArea = concrete, HostAreaId = concrete.Id
            };
            for (int i = 0; i < 5; i++)
            {
                var bar = Fiber.CreatePoint(0.012, -0.4 + i * 0.2, z);
                bar.Area = As / 5.0;
                group.Fibers.Add(bar);
            }
            areas.Add(group);
            id++;
        }

        var section = new CrossSection { Id = 100, Tag = "Полоса 1 м", Areas = areas };
        section.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        return section;
    }

    // Предельный момент полосы по п. 8.1.9 СП 63 в трактовке модуля Sp63Normal:
    // при x ≤ 2a' работает симметричная ветвь (8.9) — сжатая арматура исключается,
    // плечо считается до центра тяжести сжатой зоны бетона.
    static double Sp63NormalMoment()
    {
        double rs = 435e3, rsc = 400e3, rb = 14.5e3, h0 = 0.165, aPrime = 0.035, b = 1.0;
        double x = CScore.Sp63.Normal.Sp63NormalFormulas.BendingX(
            rs, Fixture.AsBot, rsc, Fixture.AsTop, rb, b);
        double xNoCompression = rs * Fixture.AsBot / (rb * b);
        Assert.True(x <= 2.0 * aPrime);
        return CScore.Sp63.Normal.Sp63NormalFormulas.SymmetricMoment(
            rs, Fixture.AsBot, h0, aPrime, xNoCompression, compressionRebarWasExcluded: true);
    }

    [Fact]
    public void Sp63NormalBranch_ReproducesExternalCapacity()
    {
        var r = Capri(ExtUls, Uls);
        var crit = r.CapriDirs!.Where(d => !d.Strip.NoRebar).MaxBy(d => d.Strip.Eta)!;

        double mUlt = Sp63NormalMoment();
        Assert.Equal(0.862, Math.Abs(crit.M_n) / mUlt, 3);
        // Плитная полоса и формульная проверка нормального сечения идут одной ветвью (8.9).
        Assert.Equal(mUlt, crit.Strip.M_ult, 6);
    }

    [Fact]
    public void Ndm_LimitMoment_IsBetweenBothSimplifiedValues()
    {
        var r = Capri(ExtUls, Uls);
        var crit = r.CapriDirs!.Where(d => !d.Strip.NoRebar).MaxBy(d => d.Strip.Eta)!;

        var solver = LimitForceSolver.ForCrossSection(Strip(), CalcType.C, ten: false);
        var res = solver.MomentFactor(0.0, crit.M_n, 0.0);

        Assert.True(res.Converged);
        double mUltNdm = Math.Abs(res.Factor * crit.M_n);
        Assert.InRange(mUltNdm, Sp63NormalMoment() * 0.95, Sp63NormalMoment() * 1.05);
    }

    // ── Выгрузка чисел для страницы верификации ────────────────────────────────────────────

    [Fact]
    public void Dump()
    {
        string? path = Environment.GetEnvironmentVariable("SHELL_SIMPL_DUMP");
        if (string.IsNullOrEmpty(path)) return;

        var ci = CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();

        var uls = Capri(ExtUls, Uls);
        var crit = uls.CapriDirs!.Where(d => !d.Strip.NoRebar).MaxBy(d => d.Strip.Eta)!;
        sb.AppendLine($"ULS: alpha={crit.Alpha_deg.ToString("F2", ci)} top={crit.Top} " +
                      $"M_n={crit.M_n.ToString("F4", ci)} N_n={crit.N_n.ToString("F4", ci)}");
        sb.AppendLine($"ULS: As_t={crit.Strip.As_t.ToString("E6", ci)} As_c={crit.Strip.As_c.ToString("E6", ci)} " +
                      $"x={crit.Strip.Xm.ToString("F6", ci)} xi={crit.Strip.Xi.ToString("F5", ci)} " +
                      $"xiR={crit.Strip.Xi_R.ToString("F5", ci)}");
        sb.AppendLine($"ULS: M_ult={crit.Strip.M_ult.ToString("F4", ci)} demand={crit.Strip.Demand.ToString("F4", ci)} " +
                      $"eta={crit.Strip.Eta.ToString("F5", ci)} case={crit.Strip.Case}");
        sb.AppendLine($"ULS: etaMax={uls.EtaMax!.Value.ToString("F5", ci)} " +
                      $"sigma_s={crit.Strip.Sigma_s_MPa.ToString("F2", ci)}");

        foreach (var (name, ext, phi1) in new[]
                 {
                     ("SLS full phi1=1.0", ExtShort, 1.0),
                     ("SLS long phi1=1.0", ExtLong, 1.0),
                     ("SLS long phi1=1.4", ExtLong, 1.4),
                 })
        {
            var res = Capri(ext, Sls, phi1);
            var c = res.CapriDirs!.Where(d => !d.Strip.NoRebar && !d.Top).MaxBy(d => d.Strip.Acrc_mm)!;
            sb.AppendLine($"{name}: alpha={c.Alpha_deg.ToString("F2", ci)} M_n={c.M_n.ToString("F4", ci)} " +
                          $"Mcrc={c.Strip.Mcrc.ToString("F4", ci)} cracked={c.Strip.Cracked} " +
                          $"x={c.Strip.Xm.ToString("F6", ci)} zs={c.Strip.Zs.ToString("F6", ci)} " +
                          $"sigma_s={c.Strip.Sigma_s_MPa.ToString("F3", ci)} psi={c.Strip.Psi_s.ToString("F5", ci)} " +
                          $"ls={c.Strip.Ls_m.ToString("F5", ci)} acrc={c.Strip.Acrc_mm.ToString("F5", ci)}");
        }

        var (aLong, aShort, alphaLong, alphaShort) = CrackWidths();
        sb.AppendLine($"acrc,long={aLong.ToString("F5", ci)} at {alphaLong.ToString("F2", ci)}");
        sb.AppendLine($"acrc,short={aShort.ToString("F5", ci)} at {alphaShort.ToString("F2", ci)}");

        // Прочность при шаге перебора 1° — насколько грубее шаг влияет на результат.
        foreach (double step in new[] { 10.0, 5.0, 1.0, 0.5, 0.1 })
        {
            var (mx, my, mxy) = ToOpenCs(ExtUls);
            var res = ShellSimplSolver.Solve(
                new ShellSimplSolver.SolveParams(0, 0, 0, mx, my, mxy, Uls, step, 0.3, 1.0, 0.5),
                Fixture.Section(), Fixture.Concrete(), Fixture.Rebar(), CalcType.C);
            var c = res.CapriDirs!.Where(d => !d.Strip.NoRebar).MaxBy(d => d.Strip.Eta)!;
            sb.AppendLine($"step={step.ToString("F1", ci)}: alpha={c.Alpha_deg.ToString("F2", ci)} " +
                          $"M_n={c.M_n.ToString("F4", ci)} eta={c.Strip.Eta.ToString("F5", ci)}");
        }

        // Независимые пути расчёта OpenCS на той же полосе.
        double mSp63 = Sp63NormalMoment();
        sb.AppendLine($"SP63 normal (8.9): M_ult={mSp63.ToString("F4", ci)} " +
                      $"K={(Math.Abs(crit.M_n) / mSp63).ToString("F5", ci)}");

        var ndm = LimitForceSolver.ForCrossSection(Strip(), CalcType.C, ten: false)
            .MomentFactor(0.0, crit.M_n, 0.0);
        sb.AppendLine($"NDM ULS: converged={ndm.Converged} factor={ndm.Factor.ToString("F5", ci)} " +
                      $"M_ult={Math.Abs(ndm.Factor * crit.M_n).ToString("F4", ci)} " +
                      $"eta={(1.0 / ndm.Factor).ToString("F5", ci)} governing={ndm.Governing} " +
                      $"epsC={ndm.EpsContourMin.ToString("E4", ci)} epsS={ndm.EpsRebarMax?.ToString("E4", ci)}");
        if (ndm.StrainPlane is { } kp)
            sb.AppendLine($"NDM ULS plane: e0={kp.e0.ToString("E6", ci)} ky={kp.ky.ToString("E6", ci)} kz={kp.kz.ToString("E6", ci)}");

        // Трещины по НДМ в направлении, критическом для собранной ширины раскрытия.
        double a = alphaShort * Math.PI / 180.0;
        double c2 = Math.Cos(a) * Math.Cos(a), s2 = Math.Sin(a) * Math.Sin(a), sc = Math.Sin(2 * a);
        var (fx, fy, fxy) = ToOpenCs(ExtShort);
        var (lx, ly, lxy) = ToOpenCs(ExtLong);
        double mFull = fx * c2 + fy * s2 + fxy * sc;
        double mLong = lx * c2 + ly * s2 + lxy * sc;
        var crack = new CrackWidthSolver(Strip(), calcCrc: CalcType.N, calcService: CalcType.N,
            calcServiceLong: CalcType.N, phi2: 0.5, acrcUltLong: 0.3, acrcUltShort: 0.4)
            .Compute(N: 0.0, mxLong: mLong, mxTotal: mFull);
        sb.AppendLine($"NDM SLS at {alphaShort.ToString("F2", ci)}: M_long={mLong.ToString("F4", ci)} " +
                      $"M_full={mFull.ToString("F4", ci)} cracked={crack.Cracked} " +
                      $"Mcrc={crack.Mcrc.ToString("F4", ci)} sigma_s={(crack.SigmaS / 1000.0).ToString("F3", ci)} " +
                      $"ls={crack.Ls.ToString("F5", ci)} " +
                      $"acrc_long={crack.AcrcLong.ToString("F5", ci)} acrc_short={crack.AcrcShort.ToString("F5", ci)}");

        var long14b = Capri(ExtLong, Sls, 1.4).CapriDirs!;
        var critL = long14b.Where(d => !d.Strip.NoRebar && !d.Top).MaxBy(d => d.Strip.Acrc_mm)!;
        double mcrcNdm = new CrackWidthSolver(Strip(), calcCrc: CalcType.N, calcService: CalcType.N,
                calcServiceLong: CalcType.N, phi2: 0.5, acrcUltLong: 0.3, acrcUltShort: 0.4)
            .Compute(N: 0.0, mxLong: critL.M_n, mxTotal: critL.M_n).Mcrc;
        double psiNdm = Math.Clamp(1.0 - 0.8 * mcrcNdm / Math.Abs(critL.M_n), 0.1, 1.0);
        sb.AppendLine($"Mcrc substitution: Mcrc_ndm={mcrcNdm.ToString("F4", ci)} psi={psiNdm.ToString("F5", ci)} " +
                      $"acrc_long={(critL.Strip.Acrc_mm * psiNdm / critL.Strip.Psi_s).ToString("F5", ci)}");

        File.WriteAllText(path, sb.ToString());
    }
}
