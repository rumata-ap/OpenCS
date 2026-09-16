using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;
using CScore;
using CScore.Fem;

namespace CScore.Tests;

// Сверка трёх методов OpenCS (Вуд-Армер, Капра-Мори, слоистая модель) с натурным
// расчётом ЛИРА-САПР 2024 (модуль армирования "Оболочка", СП 63.13330.2012/2018).
// Источник: проект "Плита_упругое_1091", элемент 1 — опорное сечение плиты над
// колонной (переписка с Георгием Апхадзе, 16.09.2026).
//
// Плита h = 200 мм, B25, A500 (Rs = Rsc = 435 МПа — как в самой Лире, без снижения
// Rsc до 400 по СНиП 78*/2.03.01-84*). Сверху и снизу, по X и по Y: ⌀12 шаг 100
// (As = 11,31 см²/м), привязка центра арматуры к грани 35 мм — армирование ПОЛНОСТЬЮ
// изотропно (одинаково по X/Y и по верху/низу).
//
// Знаки: Лира даёт Mx, My < 0 (растяжение верхней грани — ожидаемо для опорной зоны
// над колонной). Конвенция OpenCS: положительный момент растягивает верхнюю грань,
// поэтому берём Mx, My с обратным знаком (Lira → OpenCS: M = −M_Lira), Mxy — знак
// не важен по причине ниже.
//
// Из-за полной изотропии армирования худший результат (etaMax, acrcMax) НЕ зависит
// от того, какая конкретно конвенция осей/знаков принята в Лире: Вуд-Армер использует
// |Mxy| (знак Mxy не влияет), а Капра-Мори и слоистая модель ищут максимум по всем
// направлениям/граням — перестановка X↔Y или общая смена знака момента только
// переставляет местами подписи «верх/низ», «x/y» у одного и того же физического
// экстремума. Поэтому здесь (в отличие от ShellSimplExternalPlateTests, где
// армирование было анизотропным) отдельная таблица приведения осей не нужна.
public class ShellSimplLiraSupportSectionTests
{
    const double StepDeg = 0.5;

    static class Fixture
    {
        public const double As = 1131e-6;   // м²/м, ⌀12 шаг 100 (11,31 см²/м — прямо из отчёта Лиры)
        public const double Cover = 0.035;  // привязка центра арматуры к грани, м

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
            // B25 из отчёта Лиры: Rb=14.50, Rbt=1.05 (расчётные); Rbn=18.50, Rbtn=1.55 (норм.).
            var m = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0 };
            m.C = ConcreteChars(CalcType.C, 14_500.0, 1_050.0);
            m.CL = ConcreteChars(CalcType.CL, 14_500.0, 1_050.0);
            m.N = ConcreteChars(CalcType.N, 18_500.0, 1_550.0);
            m.NL = ConcreteChars(CalcType.NL, 18_500.0, 1_550.0);
            return m;
        }

        public static Material Rebar()
        {
            // A500 из отчёта Лиры: Rs=Rsc=435 (расчётные); Rs,ser=500 (норм.).
            var m = new Material { Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000.0 };
            m.C = RebarChars(CalcType.C, 435_000.0, 435_000.0);
            m.CL = RebarChars(CalcType.CL, 435_000.0, 435_000.0);
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
                Layer("низ", -(0.1 - Cover), As),
                Layer("верх", 0.1 - Cover, As),
            ],
        };

        static PlateRebarLayer Layer(string name, double z, double As_) => new()
        {
            Name = name, InputMode = "direct", Asx = As_, Asy = As_,
            Zsx = z, Zsy = z, DiameterX = 0.012, DiameterY = 0.012,
        };
    }

    // РСУ из отчёта Лиры (photo_103), кН·м/м. Знак — см. заголовочный комментарий.
    //
    // Назначение А2/В2 — по официальной легенде самой Лиры (не по первоначальной подписи
    // Георгия Апхадзе в переписке, которая перепутала их местами): «В — сочетания, учитывающие
    // ВСЕ загружения» (полное/непродолжительное), «А — сочетания, учитывающие загружения,
    // которые обладают длительностью» (длительное/продолжительное). Переменные названы по
    // роли в формуле, со ссылкой на исходную строку РСУ в комментарии.
    static readonly (double Mx, double My, double Mxy) Uls1A1  = (61.5367, 25.9279, 11.3110);  // А1, прочность
    static readonly (double Mx, double My, double Mxy) SlsFull  = (52.9528, 22.3112, 9.7332);   // В2 — полное (все загружения)
    static readonly (double Mx, double My, double Mxy) SlsLong  = (40.1327, 16.9095, 7.3767);   // А2 — длительное

    const string Uls = "shell_simpl_wa_uls";
    const string UlsCapri = "shell_simpl_capri_uls";
    const string Sls = "shell_simpl_wa_sls";
    const string SlsCapri = "shell_simpl_capri_sls";

    static ShellSimplSolver.SolveResult Simpl(
        (double Mx, double My, double Mxy) m, string kind, double phi1 = 1.0)
        => ShellSimplSolver.Solve(
            new ShellSimplSolver.SolveParams(0, 0, 0, m.Mx, m.My, m.Mxy, kind, StepDeg, 0.3, phi1, 0.5),
            Fixture.Section(), Fixture.Concrete(), Fixture.Rebar(),
            kind.EndsWith("uls") ? CalcType.C : CalcType.N);

    // acrc,long = acrc(long, φ1=1.4); acrc,short = acrc(full,φ1=1.0) − acrc(long,φ1=1.0) + acrc(long,φ1=1.4)
    // (п. 8.2.5 СП 63), поэлементно по 4 полосам Вуда / по направлениям Капра-Мори.
    static (double Long, double Short) WaCrackWidths()
    {
        var full10 = Simpl(SlsFull, Sls, 1.0).WaStrips!;
        var long10 = Simpl(SlsLong, Sls, 1.0).WaStrips!;
        var long14 = Simpl(SlsLong, Sls, 1.4).WaStrips!;

        double aLong = 0, aShort = 0;
        for (int i = 0; i < full10.Count; i++)
        {
            double l = long14[i].Acrc_mm;
            double sh = full10[i].Acrc_mm - long10[i].Acrc_mm + l;
            if (l > aLong) aLong = l;
            if (sh > aShort) aShort = sh;
        }
        return (aLong, aShort);
    }

    static (double Long, double Short) CapriCrackWidths()
    {
        var full10 = Simpl(SlsFull, SlsCapri, 1.0).CapriDirs!;
        var long10 = Simpl(SlsLong, SlsCapri, 1.0).CapriDirs!;
        var long14 = Simpl(SlsLong, SlsCapri, 1.4).CapriDirs!;

        double aLong = 0, aShort = 0;
        for (int i = 0; i < full10.Count; i++)
        {
            if (full10[i].Strip.NoRebar) continue;
            double l = long14[i].Strip.Acrc_mm;
            double sh = full10[i].Strip.Acrc_mm - long10[i].Strip.Acrc_mm + l;
            if (l > aLong) aLong = l;
            if (sh > aShort) aShort = sh;
        }
        return (aLong, aShort);
    }

    // ── Слоистая модель ──────────────────────────────────────────────────────────

    static ShellStrainSolverResult LayeredState((double Mx, double My, double Mxy) m, CalcType calc)
    {
        var section = Fixture.Section();
        var concreteMat = Fixture.Concrete();
        var rebarMat = Fixture.Rebar();
        var cDiag = concreteMat.GetDiagramms(DiagrammType.L3)![calc];
        var rDiag = rebarMat.GetDiagramms(DiagrammCompatibility.Coerce(rebarMat.Type, DiagrammType.L2))![calc];

        var solver = new ShellStrainSolver(section, cDiag, rDiag);
        var target = new[] { 0.0, 0.0, 0.0, m.Mx, m.My, m.Mxy };
        var res = solver.SolveRobust(target, concreteMat, rebarMat, calc);
        Assert.True(res.Converged, $"Слоистая модель не сошлась: {res.Iterations} ит., Δ={res.Residual:G3}");
        return res;
    }

    static IReadOnlyList<ShellCrackStripResult> LayeredCrackStrips(
        (double Mx, double My, double Mxy) m, ShellStrainState st, double phi1)
    {
        var section = Fixture.Section();
        var concreteMat = Fixture.Concrete();
        var rebarMat = Fixture.Rebar();
        var shell = new ShellLoadItem { Mx = m.Mx, My = m.My, Mxy = m.Mxy };
        return ShellLayeredCrackWidth.ComputeAll(
            section, shell, st, concreteMat.chars[CalcType.N], rebarMat.chars[CalcType.N],
            phi1, 0.5, SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63);
    }

    static (double Long, double Short) LayeredCrackWidths()
    {
        var stFull = LayeredState(SlsFull, CalcType.N).StrainState;
        var stLong = LayeredState(SlsLong, CalcType.N).StrainState;

        var full10 = LayeredCrackStrips(SlsFull, stFull, 1.0);
        var long10 = LayeredCrackStrips(SlsLong, stLong, 1.0);
        var long14 = LayeredCrackStrips(SlsLong, stLong, 1.4);

        double aLong = 0, aShort = 0;
        for (int i = 0; i < full10.Count; i++)
        {
            double l = long14[i].AcrcMm;
            double sh = full10[i].AcrcMm - long10[i].AcrcMm + l;
            if (l > aLong) aLong = l;
            if (sh > aShort) aShort = sh;
        }
        return (aLong, aShort);
    }

    // ── Прочность (ПС1) ─────────────────────────────────────────────────────────

    [Fact]
    public void WoodArmer_Uls_ClosesToLira()
    {
        var r = Simpl(Uls1A1, Uls);
        // Лира (теория Вуда): К.З = 0.985 (ОК). Сравнение не может быть точным —
        // разные модели/допущения, — но порядок и запас должны совпадать.
        Assert.InRange(r.EtaMax!.Value, 0.5, 1.1);
    }

    [Fact]
    public void CapraMori_Uls_ClosesToLira()
    {
        var r = Simpl(Uls1A1, UlsCapri);
        Assert.InRange(r.EtaMax!.Value, 0.5, 1.1);
    }

    // Деформационный критерий (п. 8.1.30) не сравним 1:1 с плоской пластической
    // схемой (Вуд/Капра-Мори, п. 8.1.9): при том же моменте η получается заметно
    // ниже — сечение ещё далеко от предельных деформаций бетона/арматуры, хотя
    // упрощённая схема уже показывает исчерпание несущей способности. То же самое
    // расхождение уже задокументировано в ShellSimplExternalPlateTests
    // (Ndm_LimitMoment_IsBetweenBothSimplifiedValues). Поэтому здесь проверяем
    // только сходимость и физичность (0 ≤ η), не близость к Лире.
    [Fact]
    public void Layered_Uls_Converges()
    {
        var section = Fixture.Section();
        var concreteMat = Fixture.Concrete();
        var rebarMat = Fixture.Rebar();
        var shell = new ShellLoadItem { Mx = Uls1A1.Mx, My = Uls1A1.My, Mxy = Uls1A1.Mxy };

        var r = ShellLayeredCheck.CheckUls(section, shell, concreteMat, rebarMat, CalcType.C,
            DiagrammType.L3, out _, out _, out _);

        Assert.True(r.Converged, r.Description);
        Assert.InRange(r.Utilization, 0.05, 1.1);
    }

    // Предельный момент слоистой модели вдоль луча пропорционального нагружения
    // (Mx,My,Mxy = λ·m): бисекция по λ до η(λ) = 1 (п. 8.1.30). Даёт Кисп на ТОЙ ЖЕ
    // основе (момент/момент), что и Вуд-Армер/Капра-Мори — прямое сравнение с ними
    // и с К.З Лиры, а не деформация/деформация.
    static double LayeredMomentUtilisation((double Mx, double My, double Mxy) m, CalcType calc)
    {
        var section = Fixture.Section();
        var concreteMat = Fixture.Concrete();
        var rebarMat = Fixture.Rebar();

        double UtilAt(double lambda)
        {
            var shell = new ShellLoadItem { Mx = m.Mx * lambda, My = m.My * lambda, Mxy = m.Mxy * lambda };
            var r = ShellLayeredCheck.CheckUls(section, shell, concreteMat, rebarMat, calc,
                DiagrammType.L3, out _, out _, out _);
            return r.Converged ? r.Utilization : double.PositiveInfinity;
        }

        double lo = 1.0, hi = 1.0;
        double uLo = UtilAt(lo);
        Assert.True(uLo < 1.0, $"Уже при λ=1 η={uLo:F3} ≥ 1 — сечение не имеет запаса по деформациям");

        // Расширяем вверх, пока не перескочим через η=1 или не потеряем сходимость.
        double uHi = uLo;
        while (uHi < 1.0)
        {
            lo = hi; uLo = uHi;
            hi *= 1.5;
            uHi = UtilAt(hi);
            Assert.True(hi < 100.0, "Не удалось найти предел по моменту (λ→∞)");
        }

        for (int i = 0; i < 60; i++)
        {
            double mid = 0.5 * (lo + hi);
            double uMid = UtilAt(mid);
            if (double.IsInfinity(uMid) || uMid > 1.0) hi = mid; else lo = mid;
        }

        double lambdaUlt = 0.5 * (lo + hi);
        return 1.0 / lambdaUlt;   // Кисп = M_demand / M_ult = 1 / λ_ult
    }

    [Fact]
    public void Layered_MomentBasedUtilisation_ClosesToLira()
    {
        double kisp = LayeredMomentUtilisation(Uls1A1, CalcType.C);
        // Лира (теория Вуда): К.З = 0.985. Вуд-Армер OpenCS: 1.000. Капра-Мори: 0.890.
        Assert.InRange(kisp, 0.5, 1.1);
    }

    // ── Трещиностойкость (ПС2) ──────────────────────────────────────────────────

    // SlsFull (52.95 кН·м/м) больше SlsLong (40.13 кН·м/м) — обычное соотношение
    // (полное сочетание включает кратковременную часть нагрузки сверх длительной).

    [Fact]
    public void WoodArmer_CrackWidths_ClosetoLira()
    {
        var (aLong, aShort) = WaCrackWidths();
        // Лира (теория Вуда): wcrc,long ≈ 0,33 мм.
        Assert.InRange(aLong, 0.15, 0.45);
        Assert.InRange(aShort, 0.15, 0.45);
    }

    [Fact]
    public void CapraMori_CrackWidths_ClosetoLira()
    {
        var (aLong, aShort) = CapriCrackWidths();
        Assert.InRange(aLong, 0.15, 0.45);
        Assert.InRange(aShort, 0.15, 0.45);
    }

    [Fact]
    public void Layered_CrackWidths_ClosetoLira()
    {
        var (aLong, aShort) = LayeredCrackWidths();
        Assert.InRange(aLong, 0.15, 0.45);
        Assert.InRange(aShort, 0.15, 0.45);
    }

    // ── Выгрузка чисел для страницы верификации ────────────────────────────────

    [Fact]
    public void Dump()
    {
        string? path = Environment.GetEnvironmentVariable("SHELL_SIMPL_LIRA_DUMP");
        if (string.IsNullOrEmpty(path)) return;

        var ci = CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();

        void Row(string label, double v) => sb.AppendLine($"{label} = {v.ToString("F5", ci)}");

        var wa = Simpl(Uls1A1, Uls);
        Row("WA eta_max", wa.EtaMax!.Value);
        var capri = Simpl(Uls1A1, UlsCapri);
        Row("Capri eta_max", capri.EtaMax!.Value);

        var section = Fixture.Section();
        var concreteMat = Fixture.Concrete();
        var rebarMat = Fixture.Rebar();
        var shellUls = new ShellLoadItem { Mx = Uls1A1.Mx, My = Uls1A1.My, Mxy = Uls1A1.Mxy };
        var layered = ShellLayeredCheck.CheckUls(section, shellUls, concreteMat, rebarMat, CalcType.C,
            DiagrammType.L3, out _, out _, out _);
        Row("Layered eta (strain-based)", layered.Utilization);
        sb.AppendLine($"Layered formula = {layered.Formula}, {layered.Description}");
        Row("Layered Kisp (moment-based)", LayeredMomentUtilisation(Uls1A1, CalcType.C));

        var (waLong, waShort) = WaCrackWidths();
        Row("WA acrc_long", waLong);
        Row("WA acrc_short", waShort);

        var (capriLong, capriShort) = CapriCrackWidths();
        Row("Capri acrc_long", capriLong);
        Row("Capri acrc_short", capriShort);

        var (layLong, layShort) = LayeredCrackWidths();
        Row("Layered acrc_long", layLong);
        Row("Layered acrc_short", layShort);

        sb.AppendLine();
        sb.AppendLine("=== Промежуточные величины критической полосы (длительное, φ1=1,4) ===");

        // Вуд-Армер: критическая из 4 полос (x/верх, x/низ, y/верх, y/низ).
        {
            var strips = Simpl(SlsLong, Sls, 1.4).WaStrips!;
            var c = strips.MaxBy(s => s.Acrc_mm)!;
            sb.AppendLine($"WA[{c.Name}]: M_des={c.M_des:F4} h0={c.H0:F4} Mcrc={c.Mcrc:F4} cracked={c.Cracked} " +
                          $"xm={c.Xm:F5} zs={c.Zs:F5} sigma_s={c.Sigma_s_MPa:F3} sigma_s_crc={c.Sigma_s_crc_MPa:F3} " +
                          $"psi_s={c.Psi_s:F5} ls={c.Ls_m:F5} gamma={c.Gamma:F3} acrc={c.Acrc_mm:F5}");
        }

        // Капра-Мори: критическое направление из перебора по α.
        {
            var dirs = Simpl(SlsLong, SlsCapri, 1.4).CapriDirs!;
            var c = dirs.Where(d => !d.Strip.NoRebar).MaxBy(d => d.Strip.Acrc_mm)!;
            sb.AppendLine($"Capri[alpha={c.Alpha_deg:F2} top={c.Top}]: M_n={c.M_n:F4} h0={c.Strip.H0:F4} " +
                          $"Mcrc={c.Strip.Mcrc:F4} cracked={c.Strip.Cracked} xm={c.Strip.Xm:F5} zs={c.Strip.Zs:F5} " +
                          $"sigma_s={c.Strip.Sigma_s_MPa:F3} sigma_s_crc={c.Strip.Sigma_s_crc_MPa:F3} " +
                          $"psi_s={c.Strip.Psi_s:F5} ls={c.Strip.Ls_m:F5} gamma={c.Strip.Gamma:F3} acrc={c.Strip.Acrc_mm:F5}");
        }

        // Слоистая модель: критическая полоса (слой × направление x|y) из ComputeAll.
        {
            var stLong = LayeredState(SlsLong, CalcType.N).StrainState;
            var strips = LayeredCrackStrips(SlsLong, stLong, 1.4);
            var c = strips.MaxBy(s => s.AcrcMm)!;
            sb.AppendLine($"Layered[{c.LayerName}/{c.Direction}, z={c.Z:F4}]: M_des={c.MDes:F4} " +
                          $"Mcrc={c.Mcrc:F4} cracked={c.Cracked} sigma_s={c.SigmaS / 1000.0:F3} " +
                          $"sigma_s_crc={c.SigmaSCrc / 1000.0:F3} psi_s={c.PsiS:F5} ls={c.LsM:F5} " +
                          $"angle={c.CrackAngleDeg:F2} acrc={c.AcrcMm:F5}");
        }

        sb.AppendLine();
        sb.AppendLine("Ref (Лира, Вуд), Кзап: прочность = 0.985 → Кисп ≈ 1.015; " +
                       "трещины = 0.909 → Кисп ≈ 1.10 (wcrc,long = 0.33 мм при пределе 0.30 мм)");

        File.WriteAllText(path, sb.ToString());
    }
}
