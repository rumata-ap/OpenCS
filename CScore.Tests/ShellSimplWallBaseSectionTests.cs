using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;
using CScore;
using CScore.Fem;

namespace CScore.Tests;

// Сверка трёх методов OpenCS (Вуд-Армер, Капра-Мори, слоистая модель) с авторским
// расчётом Георгия Апхадзе в Excel: низ стены на упругом основании — обжатие по
// вертикали, растяжение по горизонтали, изгиб в стыке с фундаментом (переписка
// 18.09.2026). Расчёт эталона выполнен без учёта коэффициента деформационной схемы
// (η = 1,0), высота сжатой зоны для трещин у автора реализована упрощённо.
//
// Стена h = 200 мм, B25, A500. У каждой грани (наружной и внутренней), по обоим
// направлениям: ⌀12 шаг 100 (As = 11,31 см²/м), привязка центра арматуры 35 мм
// (h0 = 165 мм) — армирование ПОЛНОСТЬЮ изотропно.
//
// Угол: у автора углы отсчитываются как 90° минус угол OpenCS (подтверждено им в переписке),
// поэтому его "главная площадка 80°" — это площадка с нормалью 10° от горизонтали у нас.
//
// Оси и знаки эталона: Y — вертикальная, X — горизонтальная; сжатие с минусом;
// положительные моменты растягивают НАРУЖНУЮ грань стены. Конвенция OpenCS:
// положительный момент растягивает ВЕРХНЮЮ грань, "+" у N — растяжение. Поэтому
// наружная грань эталона = "верх" OpenCS, и усилия подставляются как есть, без
// смены знака. Момент и нормальная сила одного индекса относятся к одной и той же
// полосе (My действует вдоль Ny — прямое уточнение автора), что совпадает с
// конвенцией ShellSimplSolver/ShellStrainSolver (Mx и Nx дают σx).
//
// Слоистая модель прогоняется в двух вариантах: без β-снижения и с критерием двухосного
// НДС по Vecchio-Collins (PlateSection.SofteningModel = "vecchio_collins", β = 1/(0,8+170·ε₁)).
// ВНИМАНИЕ: PlateSection.SofteningEpsC2 в формулу β не входит (VecchioCollinsBeta принимает
// параметр и игнорирует его, множитель 170 = 0,34/0,002 зашит), поэтому значение εc2 здесь
// ни на что не влияет.
//
// Из-за полной изотропии армирования перестановка X↔Y или общая смена знака
// моментов только переставляет подписи "верх/низ" у одного и того же физического
// экстремума — отдельная таблица приведения осей, как в
// ShellSimplExternalPlateTests, здесь не нужна.
public class ShellSimplWallBaseSectionTests
{
    const double StepDeg = 0.5;

    static class Fixture
    {
        public const double As = 1131e-6;   // м²/м, ⌀12 шаг 100 (11,31 см²/м — из исходных данных автора)
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
            // B25 из исходных данных: Rb=14.50, Rbt=1.05 (расчётные); Rbn=18.50, Rbtn=1.55 (норм.),
            // Eb0 = 30 000 МПа.
            var m = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0 };
            m.C = ConcreteChars(CalcType.C, 14_500.0, 1_050.0);
            m.CL = ConcreteChars(CalcType.CL, 14_500.0, 1_050.0);
            m.N = ConcreteChars(CalcType.N, 18_500.0, 1_550.0);
            m.NL = ConcreteChars(CalcType.NL, 18_500.0, 1_550.0);
            return m;
        }

        public static Material Rebar()
        {
            // A500 из исходных данных: Rs=435, Rsc=400 (расчётные, Rsc снижен по СНиП 2.03.01-84*
            // — в отличие от расчёта ЛИРА, где Rsc=435); Rs,ser=500 (норм.), Es=200 000 МПа.
            var m = new Material { Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000.0 };
            m.C = RebarChars(CalcType.C, 435_000.0, 400_000.0);
            m.CL = RebarChars(CalcType.CL, 435_000.0, 400_000.0);
            m.N = RebarChars(CalcType.N, 500_000.0, 500_000.0);
            m.NL = RebarChars(CalcType.NL, 500_000.0, 500_000.0);
            return m;
        }

        /// <param name="softening">Учитывать снижение прочности сжатого бетона при поперечном
        /// растяжении по Vecchio-Collins (β = 1/(0,8 + 170·ε₁), п. 1986 г. / МКПТ). В диалоге
        /// плитного сечения OpenCS это выбор "Модель β-снижения"; по умолчанию β = 1.</param>
        public static PlateSection Section(bool softening = false) => new()
        {
            H = 0.2, NLayers = 40, PlateModel = "layered", ConcreteDiagramType = DiagrammType.L3,
            TensionConcrete = false,
            SofteningModel = softening ? "vecchio_collins" : "",
            SofteningEpsC2 = 0.002,
            RebarLayers =
            [
                Layer("внутр.", -(0.1 - Cover), As),
                Layer("наружн.", 0.1 - Cover, As),
            ],
        };

        static PlateRebarLayer Layer(string name, double z, double As_) => new()
        {
            Name = name, InputMode = "direct", Asx = As_, Asy = As_,
            Zsx = z, Zsy = z, DiameterX = 0.012, DiameterY = 0.012,
        };
    }

    // Усилия из таблиц автора (кН/м, кН·м/м). Знаки — см. заголовочный комментарий.
    readonly record struct Forces(double Nx, double Ny, double Nxy, double Mx, double My, double Mxy);

    // Сочетание 1 — расчётные полные (прочность).
    static readonly Forces Uls = new(Nx: 500.0, Ny: -1200.0, Nxy: 100.0, Mx: 25.9, My: 61.5, Mxy: 11.3);
    // Сочетание 2 — нормативные полные (кратковременные трещины).
    static readonly Forces SlsFull = new(Nx: 400.0, Ny: -1000.0, Nxy: 75.0, Mx: 22.3, My: 53.0, Mxy: 9.7);
    // Сочетание 3 — нормативные длительные (длительные трещины).
    static readonly Forces SlsLong = new(Nx: 350.0, Ny: -900.0, Nxy: 50.0, Mx: 16.9, My: 40.1, Mxy: 7.4);

    const string WaUls = "shell_simpl_wa_uls";
    const string CapriUls = "shell_simpl_capri_uls";
    const string WaSls = "shell_simpl_wa_sls";
    const string CapriSls = "shell_simpl_capri_sls";

    // Формульные методы (Вуд-Армер, Капра-Мори) работают по нормативным формулам плоской
    // пластической схемы и β-снижение не видят вовсе — оно относится только к слоистой модели,
    // где бетон интегрируется по главным деформациям.
    static ShellSimplSolver.SolveResult Simpl(Forces f, string kind, double phi1 = 1.0,
        WplGammaMethod gamma = WplGammaMethod.Sp63)
        => ShellSimplSolver.Solve(
            new ShellSimplSolver.SolveParams(f.Nx, f.Ny, f.Nxy, f.Mx, f.My, f.Mxy, kind, StepDeg, 0.3, phi1, 0.5,
                SigmaSCrcMethod.ReleasedConcrete8137, gamma),
            Fixture.Section(), Fixture.Concrete(), Fixture.Rebar(),
            kind.EndsWith("uls") ? CalcType.C : CalcType.N);

    // acrc,long = acrc(long, φ1=1.4); acrc,short = acrc(full,φ1=1.0) − acrc(long,φ1=1.0) + acrc(long,φ1=1.4)
    // (п. 8.2.5 СП 63), поэлементно по 4 полосам Вуда / по направлениям Капра-Мори / по полосам слоистой.
    static (double Long, double Short) WaCrackWidths(WplGammaMethod gamma = WplGammaMethod.Sp63)
    {
        var full10 = Simpl(SlsFull, WaSls, 1.0, gamma).WaStrips!;
        var long10 = Simpl(SlsLong, WaSls, 1.0, gamma).WaStrips!;
        var long14 = Simpl(SlsLong, WaSls, 1.4, gamma).WaStrips!;

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

    static (double Long, double Short) CapriCrackWidths(WplGammaMethod gamma = WplGammaMethod.Sp63)
    {
        var full10 = Simpl(SlsFull, CapriSls, 1.0, gamma).CapriDirs!;
        var long10 = Simpl(SlsLong, CapriSls, 1.0, gamma).CapriDirs!;
        var long14 = Simpl(SlsLong, CapriSls, 1.4, gamma).CapriDirs!;

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

    static ShellStrainSolverResult LayeredState(Forces f, CalcType calc, bool softening)
    {
        var section = Fixture.Section(softening);
        var concreteMat = Fixture.Concrete();
        var rebarMat = Fixture.Rebar();
        var cDiag = concreteMat.GetDiagramms(DiagrammType.L3)![calc];
        var rDiag = rebarMat.GetDiagramms(DiagrammCompatibility.Coerce(rebarMat.Type, DiagrammType.L2))![calc];

        var solver = new ShellStrainSolver(section, cDiag, rDiag);
        var target = new[] { f.Nx, f.Ny, f.Nxy, f.Mx, f.My, f.Mxy };
        var res = solver.SolveRobust(target, concreteMat, rebarMat, calc);
        Assert.True(res.Converged, $"Слоистая модель не сошлась: {res.Iterations} ит., Δ={res.Residual:G3}");
        return res;
    }

    static IReadOnlyList<ShellCrackStripResult> LayeredCrackStrips(
        Forces f, ShellStrainState st, double phi1, bool softening,
        WplGammaMethod gamma = WplGammaMethod.Sp63)
    {
        var section = Fixture.Section(softening);
        var concreteMat = Fixture.Concrete();
        var rebarMat = Fixture.Rebar();
        var shell = new ShellLoadItem { Nx = f.Nx, Ny = f.Ny, Nxy = f.Nxy, Mx = f.Mx, My = f.My, Mxy = f.Mxy };

        // П. 8.2.18: σs,crc — та же задача при M = Mcrc, поэтому решателю передаётся способ
        // пересчитать НДС для состояния образования трещин.
        var cDiag = concreteMat.GetDiagramms(DiagrammType.L3)![CalcType.N];
        var rDiag = rebarMat.GetDiagramms(
            DiagrammCompatibility.Coerce(rebarMat.Type, DiagrammType.L2))![CalcType.N];
        ShellStrainState? SolveAt(double[] target)
        {
            var res = new ShellStrainSolver(section, cDiag, rDiag)
                .SolveRobust(target, concreteMat, rebarMat, CalcType.N);
            return res.Converged ? res.StrainState : null;
        }

        return ShellLayeredCrackWidth.ComputeAll(
            section, shell, st, concreteMat.chars[CalcType.N], rebarMat.chars[CalcType.N],
            phi1, 0.5, SigmaSCrcMethod.ReleasedConcrete8137, gamma, SolveAt);
    }

    static (double Long, double Short) LayeredCrackWidths(bool softening,
        WplGammaMethod gamma = WplGammaMethod.Sp63)
    {
        var stFull = LayeredState(SlsFull, CalcType.N, softening).StrainState;
        var stLong = LayeredState(SlsLong, CalcType.N, softening).StrainState;

        var full10 = LayeredCrackStrips(SlsFull, stFull, 1.0, softening, gamma);
        var long10 = LayeredCrackStrips(SlsLong, stLong, 1.0, softening, gamma);
        var long14 = LayeredCrackStrips(SlsLong, stLong, 1.4, softening, gamma);

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

    static ShellLayeredCheck.Result LayeredUls(Forces f, bool softening, double lambda = 1.0)
    {
        var shell = new ShellLoadItem
        {
            Nx = f.Nx * lambda, Ny = f.Ny * lambda, Nxy = f.Nxy * lambda,
            Mx = f.Mx * lambda, My = f.My * lambda, Mxy = f.Mxy * lambda
        };
        return ShellLayeredCheck.CheckUls(Fixture.Section(softening), shell, Fixture.Concrete(), Fixture.Rebar(),
            CalcType.C, DiagrammType.L3, out _, out _, out _);
    }

    // Предельные усилия слоистой модели вдоль луча пропорционального нагружения
    // (все шесть компонентов масштабируются одним λ): бисекция по λ до η(λ) = 1
    // (деформационный критерий п. 8.1.30). Даёт Кисп на ТОЙ ЖЕ основе (усилие/усилие),
    // что Вуд-Армер и Капра-Мори, — а не деформация/деформация.
    static double LayeredForceUtilisation(Forces f, bool softening)
    {
        double UtilAt(double lambda)
        {
            var r = LayeredUls(f, softening, lambda);
            return r.Converged ? r.Utilization : double.PositiveInfinity;
        }

        double lo = 1.0, hi = 1.0;
        double uLo = UtilAt(lo), uHi = uLo;

        if (uLo >= 1.0)
        {
            // Уже при λ=1 предел превышен — ищем λ_ult вниз.
            while (uLo >= 1.0)
            {
                hi = lo; lo *= 0.5;
                uLo = UtilAt(lo);
                Assert.True(lo > 1e-4, "Не удалось найти предел по усилиям (λ→0)");
            }
        }
        else
        {
            while (uHi < 1.0)
            {
                lo = hi; uLo = uHi;
                hi *= 1.5;
                uHi = UtilAt(hi);
                Assert.True(hi < 100.0, "Не удалось найти предел по усилиям (λ→∞)");
            }
        }

        for (int i = 0; i < 60; i++)
        {
            double mid = 0.5 * (lo + hi);
            double uMid = UtilAt(mid);
            if (double.IsInfinity(uMid) || uMid > 1.0) hi = mid; else lo = mid;
        }

        double lambdaUlt = 0.5 * (lo + hi);
        return 1.0 / lambdaUlt;   // Кисп = усилие / предельное усилие = 1 / λ_ult
    }

    // ── Прочность (ПС1) ─────────────────────────────────────────────────────────

    // Эталон автора: Кисп = 0,951 (остаточный запас 4,9%).
    //
    // Вуд-Армер OpenCS даёт 1,000, и решает у него полоса "y, верх" — вертикальное
    // направление, у которого ShellSimplSolver.WaMembrane ОБНУЛИЛ обжатие: комбинация
    // Nx=+500 / Ny=−1200 / Nxy=100 сводится к Nx,des=+508, Ny,des=0 (правило Вуда для
    // мембранных усилий отбрасывает сжатие). Поэтому здесь Вуд-Армер считает вертикаль
    // как чистый изгиб M = My + |Mxy| = 72,8 — в запас, и совпадение с эталоном
    // в пределах 5% получается "не по той причине".
    [Fact]
    public void WoodArmer_Uls_ClosesToAuthor()
    {
        var r = Simpl(Uls, WaUls);
        Assert.InRange(r.EtaMax!.Value, 0.7, 1.15);
    }

    // Капра-Мори переносит N в направление площадки со знаком (N_n = Nx·c² + Ny·s² + 2Nxy·cs),
    // поэтому обжатие не теряется. Критическая площадка — α ≈ 10,5° от оси X (горизонталь),
    // то есть почти горизонтальное направление с растяжением N_n ≈ +479 кН/м; η = 0,974.
    [Fact]
    public void CapraMori_Uls_ClosesToAuthor()
    {
        var r = Simpl(Uls, CapriUls);
        Assert.InRange(r.EtaMax!.Value, 0.7, 1.15);

        var crit = r.CapriDirs!.Where(d => !d.Strip.NoRebar).MaxBy(d => d.Strip.Eta)!;
        // Автор даёт "расчётный угол наклона главной площадки = 80°" и ПОДТВЕРДИЛ (переписка
        // 18.09.2026), что его угол = 90° − угол OpenCS. То есть 90° − 80° = 10° против наших
        // 10,5° при шаге перебора 0,5° — оба расчёта нашли одну и ту же критическую площадку,
        // а не только близкий Кисп.
        Assert.InRange(crit.Alpha_deg, 8.0, 13.0);
    }

    // Деформационный критерий (п. 8.1.30) не сравним 1:1 с плоской пластической схемой
    // (Вуд/Капра-Мори, п. 8.1.9/8.1.14/8.1.19): при тех же усилиях η заметно ниже —
    // сечение ещё далеко от предельных деформаций. То же расхождение задокументировано
    // в ShellSimplExternalPlateTests и ShellSimplLiraSupportSectionTests.
    //
    // softening=true — критерий двухосного НДС по Vecchio-Collins: прочность сжатого бетона
    // снижается поперечным растяжением, β = 1/(0,8 + 170·ε₁). Для этого сечения он включается
    // в полную силу: растяжение по горизонтали (Nx = +500 кН/м) и есть то самое поперечное
    // растяжение для вертикального обжатия Ny = −1200.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Layered_Uls_Converges(bool softening)
    {
        var r = LayeredUls(Uls, softening);
        Assert.True(r.Converged, r.Description);
        Assert.InRange(r.Utilization, 0.05, 1.1);
    }

    // Кисп слоистой модели на той же основе, что у эталона (усилие/предельное усилие):
    // без β-снижения 0,907, с Vecchio-Collins 0,934 — против 0,951 автора,
    // 0,974 Капра-Мори и 1,000 Вуд-Армер. β-снижение добавляет всего +3%, хотя в самом
    // нагруженном слое β падает до 0,626 (ε₁ = 0,0047 у наружной грани): предел здесь
    // набирается не раздавливанием бетона, а растяжением арматуры.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Layered_ForceBasedUtilisation_ClosesToAuthor(bool softening)
    {
        double kisp = LayeredForceUtilisation(Uls, softening);
        Assert.InRange(kisp, 0.5, 1.15);
    }

    // β-снижение по Vecchio-Collins не может увеличить несущую способность: сжатый бетон
    // при поперечном растяжении только слабеет, поэтому Кисп с ним обязан быть не меньше.
    [Fact]
    public void VecchioCollins_DoesNotIncreaseCapacity()
    {
        double plain = LayeredForceUtilisation(Uls, softening: false);
        double soft = LayeredForceUtilisation(Uls, softening: true);
        Assert.True(soft >= plain - 1e-9,
            $"Кисп с β-снижением {soft:F4} меньше, чем без него {plain:F4}");
    }

    // ── Трещиностойкость (ПС2) ──────────────────────────────────────────────────

    // Автор получает acrc,long = 0,048 мм и acrc,short = 0,085 мм, OpenCS — существенно больше.
    // Расхождение сосредоточено в горизонтальном направлении, где мембранное усилие
    // растягивающее (Nx = +350 кН/м длительное, +400 полное): после образования трещины
    // эти 350 кН/м воспринимает только арматура, и даже при работе ОБОИХ рядов
    // σs ≥ 350/(2·1131·10⁻⁶) = 155 МПа (см. HorizontalDirection_SigmaS_AtLeastEquilibriumMinimum).
    //
    // 18.09.2026, после введения поправки (8.154) п. 8.2.28 и схемы сквозного растяжения в
    // ComputeStripSls три метода OpenCS сошлись между собой: 0,279 (Вуд-Армер), 0,309
    // (Капра-Мори) и 0,317 (слоистая) против прежних 0,273 / 0,161 / 0,317 — формульный путь
    // больше не занижает σs в растянутом направлении. С расчётом автора расхождение осталось
    // (0,048); непроверенные кандидаты — коэффициент приведения арматуры (п. 8.2.15 требует
    // Eb,red = Rb,ser/0,0015 = 12 333 МПа, α = 16,2; в исходных данных автора фигурирует только
    // начальный α = Es/Eb0 = 6,667), φ3 = 1,2 для внецентренно растянутых элементов и база ls.
    // Числа ниже — регрессионные, не эталонные.
    [Fact]
    public void WoodArmer_CrackWidths_Regression()
    {
        var (aLong, aShort) = WaCrackWidths();
        Assert.InRange(aLong, 0.20, 0.35);
        Assert.InRange(aShort, 0.30, 0.45);
    }

    [Fact]
    public void CapraMori_CrackWidths_Regression()
    {
        var (aLong, aShort) = CapriCrackWidths();
        Assert.InRange(aLong, 0.23, 0.27);
        Assert.InRange(aShort, 0.28, 0.32);
    }

    // На ПС2 β-снижение здесь не влияет вообще: 0,31742/0,40581 без него и 0,31741/0,40585
    // с ним. Критическая полоса — горизонтальное направление у наружной грани, она работает
    // на растяжении, где β не участвует (ConcreteStress применяет β только к сжатой ветви).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Layered_CrackWidths_Regression(bool softening)
    {
        var (aLong, aShort) = LayeredCrackWidths(softening);
        Assert.InRange(aLong, 0.20, 0.24);
        Assert.InRange(aShort, 0.25, 0.30);
    }

    // Вертикальное направление не трещит ни по одному методу, который честно учитывает
    // знак N: обжатие Ny = −900 кН/м (длительное) поднимает Mcrc до ≈45 кН·м/м против
    // My = 40,1. Физическая опора для вывода выше: если эталонные 0,048 мм получены для
    // направления около вертикали, то это направление у OpenCS вообще без трещин, а
    // решает горизонтальное — с растяжением.
    [Fact]
    public void VerticalDirection_NotCracked_UnderCompression()
    {
        var st = LayeredState(SlsLong, CalcType.N, softening: false).StrainState;
        var strips = LayeredCrackStrips(SlsLong, st, 1.4, softening: false);

        var vertical = strips.Where(s => s.Direction == "y").ToList();
        Assert.NotEmpty(vertical);
        Assert.All(vertical, s =>
        {
            Assert.False(s.Cracked, $"{s.LayerName}/y: Mcrc={s.Mcrc:F2} vs M={s.MDes:F2}");
            Assert.True(s.Mcrc > Math.Abs(SlsLong.My), $"Mcrc={s.Mcrc:F2} ≤ My={SlsLong.My:F2}");
        });

        // Капра-Мори — то же самое по площадке α = 90° (вдоль вертикали).
        var dir90 = Simpl(SlsLong, CapriSls, 1.4).CapriDirs!
            .First(d => Math.Abs(d.Alpha_deg - 90.0) < 1e-9);
        Assert.False(dir90.Strip.Cracked);
    }

    // Угол трещины у слоистой модели считается по НДС слоя (перпендикуляр к главной
    // растягивающей деформации) и служит независимой перекрёстной проверкой вывода о том,
    // какое направление критично: у наружной грани трещина крутая (−68,5° от горизонтали
    // для длительного сочетания, −62,7° для полного), то есть идёт почти вертикально и
    // пересекает ГОРИЗОНТАЛЬНУЮ арматуру. Капра-Мори приходит к тому же другим путём:
    // критическая площадка α = 28° даёт линию трещины −62,0°.
    [Fact]
    public void LayeredCrackAngle_IsSteep_CrossingHorizontalRebar()
    {
        var st = LayeredState(SlsLong, CalcType.N, softening: false).StrainState;
        var outer = LayeredCrackStrips(SlsLong, st, 1.4, softening: false)
            .Where(c => c.Cracked && c.AcrcMm > 0.0)
            .MaxBy(c => c.AcrcMm)!;

        Assert.Equal("x", outer.Direction);
        Assert.True(Math.Abs(outer.CrackAngleDeg) > 45.0,
            $"Угол трещины {outer.CrackAngleDeg:F2}° — трещина не крутая, вывод о критичности " +
            "горизонтального направления не подтверждается");

        // Та же величина у Капра-Мори: линия трещины = нормаль критической площадки + 90°.
        var crit = Simpl(SlsLong, CapriSls, 1.4).CapriDirs!
            .Where(d => !d.Strip.NoRebar).MaxBy(d => d.Strip.Acrc_mm)!;
        double capriLine = crit.Alpha_deg + 90.0;
        while (capriLine > 90.0) capriLine -= 180.0;
        Assert.InRange(Math.Abs(capriLine - outer.CrackAngleDeg), 0.0, 15.0);
    }

    // П. 8.2.18: σs,crc — напряжение в арматуре сразу после образования трещин, «определяемое
    // по 8.2.16, принимая M = M_crc». Для слоистой модели это не формула, а то же решение НДМ
    // 6×6 при моменте рассматриваемого направления, заменённом на M_crc. Эталон в тесте
    // считается независимо — прямым вызовом решателя, поэтому тест задаёт смысл «как есть из
    // НДМ», а не закрепляет подогнанное число.
    [Fact]
    public void Layered_SigmaSCrc_ComesFromStrainSolveAtCrackingMoment()
    {
        var section = Fixture.Section();
        var concreteMat = Fixture.Concrete();
        var rebarMat = Fixture.Rebar();
        var cDiag = concreteMat.GetDiagramms(DiagrammType.L3)![CalcType.N];
        var rDiag = rebarMat.GetDiagramms(
            DiagrammCompatibility.Coerce(rebarMat.Type, DiagrammType.L2))![CalcType.N];

        ShellStrainState? SolveAt(double[] target)
        {
            var res = new ShellStrainSolver(section, cDiag, rDiag)
                .SolveRobust(target, concreteMat, rebarMat, CalcType.N);
            return res.Converged ? res.StrainState : null;
        }

        var st = LayeredState(SlsLong, CalcType.N, softening: false).StrainState;
        var shell = new ShellLoadItem
        {
            Nx = SlsLong.Nx, Ny = SlsLong.Ny, Nxy = SlsLong.Nxy,
            Mx = SlsLong.Mx, My = SlsLong.My, Mxy = SlsLong.Mxy
        };
        var strips = ShellLayeredCrackWidth.ComputeAll(
            section, shell, st, concreteMat.chars[CalcType.N], rebarMat.chars[CalcType.N],
            1.4, 0.5, SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63, SolveAt);

        var outer = strips.First(s => s.Direction == "x" && s.Z > 0);
        Assert.True(outer.Cracked);

        // Эталон: то же решение, но Mx = Mcrc этой полосы.
        var stCrc = SolveAt([SlsLong.Nx, SlsLong.Ny, SlsLong.Nxy, outer.Mcrc, SlsLong.My, SlsLong.Mxy]);
        Assert.NotNull(stCrc);
        double epsS = stCrc!.EpsX(outer.Z);
        double expected = Math.Min(rebarMat.chars[CalcType.N].E * epsS,
                                   Math.Abs(rebarMat.chars[CalcType.N].Ft));

        Assert.Equal(expected / 1000.0, outer.SigmaSCrc / 1000.0, 1);
    }

    // Равновесный минимум напряжения в горизонтальной арматуре: при образовании трещины
    // растяжение Nx воспринимает только арматура двух рядов. Это нижняя граница, ниже
    // которой не может оказаться никакой корректный расчёт σs для этого направления.
    [Fact]
    public void HorizontalDirection_SigmaS_AtLeastEquilibriumMinimum()
    {
        double sigmaMin_MPa = SlsLong.Nx / (2.0 * Fixture.As) / 1000.0;   // кН/м / м² → кПа → МПа
        Assert.InRange(sigmaMin_MPa, 154.0, 156.0);                       // 154,7 МПа

        var dir0 = Simpl(SlsLong, CapriSls, 1.4).CapriDirs!
            .First(d => Math.Abs(d.Alpha_deg) < 1e-9);
        Assert.True(dir0.Strip.Cracked, "Горизонтальное направление обязано трещать при Nx = +350 кН/м");
        Assert.True(dir0.Strip.Sigma_s_MPa >= sigmaMin_MPa,
            $"σs={dir0.Strip.Sigma_s_MPa:F1} МПа < равновесного минимума {sigmaMin_MPa:F1} МПа");
    }

    // ── Выгрузка чисел для сверки ───────────────────────────────────────────────

    [Fact]
    public void Dump()
    {
        string? path = Environment.GetEnvironmentVariable("SHELL_SIMPL_WALL_DUMP");
        if (string.IsNullOrEmpty(path)) return;

        var ci = CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        void Row(string label, double v) => sb.AppendLine($"{label} = {v.ToString("F5", ci)}");

        sb.AppendLine("=== ПС1, прочность (сочетание 1) ===");
        var wa = Simpl(Uls, WaUls);
        Row("WA eta_max", wa.EtaMax!.Value);
        foreach (var s in wa.WaStrips!)
            sb.AppendLine($"  WA[{s.Name}]: M_des={s.M_des:F3} N_des={s.N_des:F2} x={s.Xm:F5} " +
                          $"M_ult={s.M_ult:F3} demand={s.Demand:F3} eta={s.Eta:F4} [{s.Case}]");

        var capri = Simpl(Uls, CapriUls);
        Row("Capri eta_max", capri.EtaMax!.Value);
        var critUls = capri.CapriDirs!.Where(d => !d.Strip.NoRebar).MaxBy(d => d.Strip.Eta)!;
        sb.AppendLine($"  Capri крит.: alpha={critUls.Alpha_deg:F2} top={critUls.Top} M_n={critUls.M_n:F3} " +
                      $"N_n={critUls.N_n:F2} x={critUls.Strip.Xm:F5} M_ult={critUls.Strip.M_ult:F3} " +
                      $"demand={critUls.Strip.Demand:F3} eta={critUls.Strip.Eta:F4} [{critUls.Strip.Case}]");

        foreach (bool soft in new[] { false, true })
        {
            string tag = soft ? "Layered+VC" : "Layered";
            var lay = LayeredUls(Uls, soft);
            Row($"{tag} eta (деформационный, п.8.1.30)", lay.Utilization);
            sb.AppendLine($"  {tag}: {lay.Formula}, {lay.Description}, ит.={lay.Iterations}, Δ={lay.Residual:G3}");
            Row($"{tag} Кисп (по усилиям, бисекция по lambda)", LayeredForceUtilisation(Uls, soft));

            // Минимальный β по толщине и слой, где он достигается (ULS-сочетание).
            var section = Fixture.Section(soft);
            var st = LayeredState(Uls, CalcType.C, soft).StrainState;
            double betaMin = 1.0, zAtMin = 0.0, eps1AtMin = 0.0;
            for (int i = 0; i < section.NLayers; i++)
            {
                double dz = section.H / section.NLayers;
                double z = -section.H / 2.0 + dz * (i + 0.5);
                PlateSection.PrincipalStrains2D(st.EpsX(z), st.EpsY(z), st.GammaXY(z),
                    out double e1, out _, out _);
                double b = e1 > 0.0 ? Math.Min(1.0, 1.0 / (0.8 + 170.0 * e1)) : 1.0;
                if (b < betaMin) { betaMin = b; zAtMin = z; eps1AtMin = e1; }
            }
            sb.AppendLine($"  {tag}: beta_min={(soft ? betaMin : 1.0):F4} при z={zAtMin:F4} " +
                          $"(eps1={eps1AtMin:G4}); формула Vecchio-Collins по eps1 того же НДС " +
                          $"даёт {betaMin:F4}" + (soft ? "" : " — но в расчёт НЕ входит"));
        }

        sb.AppendLine();
        sb.AppendLine("=== ПС2, трещины (сочетания 2 и 3) ===");
        var (waLong, waShort) = WaCrackWidths();
        Row("WA acrc_long", waLong);
        Row("WA acrc_short", waShort);
        var (capriLong, capriShort) = CapriCrackWidths();
        Row("Capri acrc_long", capriLong);
        Row("Capri acrc_short", capriShort);
        var (layLong, layShort) = LayeredCrackWidths(softening: false);
        Row("Layered acrc_long", layLong);
        Row("Layered acrc_short", layShort);
        var (layVcLong, layVcShort) = LayeredCrackWidths(softening: true);
        Row("Layered+VC acrc_long", layVcLong);
        Row("Layered+VC acrc_short", layVcShort);

        sb.AppendLine();
        sb.AppendLine("=== Промежуточные величины критической полосы (длительное, φ1=1,4) ===");
        {
            var strips = Simpl(SlsLong, WaSls, 1.4).WaStrips!;
            foreach (var c in strips)
                sb.AppendLine($"WA[{c.Name}]: M_des={c.M_des:F4} N_des={c.N_des:F2} h0={c.H0:F4} Mcrc={c.Mcrc:F4} " +
                              $"cracked={c.Cracked} xm={c.Xm:F5} zs={c.Zs:F5} sigma_s={c.Sigma_s_MPa:F3} " +
                              $"sigma_s_crc={c.Sigma_s_crc_MPa:F3} psi_s={c.Psi_s:F5} ls={c.Ls_m:F5} " +
                              $"gamma={c.Gamma:F3} acrc={c.Acrc_mm:F5}");
        }
        {
            var dirs = Simpl(SlsLong, CapriSls, 1.4).CapriDirs!;
            var c = dirs.Where(d => !d.Strip.NoRebar).MaxBy(d => d.Strip.Acrc_mm)!;
            sb.AppendLine($"Capri[alpha={c.Alpha_deg:F2} top={c.Top}]: M_n={c.M_n:F4} N_n={c.N_n:F2} " +
                          $"h0={c.Strip.H0:F4} Mcrc={c.Strip.Mcrc:F4} cracked={c.Strip.Cracked} " +
                          $"xm={c.Strip.Xm:F5} zs={c.Strip.Zs:F5} sigma_s={c.Strip.Sigma_s_MPa:F3} " +
                          $"sigma_s_crc={c.Strip.Sigma_s_crc_MPa:F3} psi_s={c.Strip.Psi_s:F5} " +
                          $"ls={c.Strip.Ls_m:F5} gamma={c.Strip.Gamma:F3} acrc={c.Strip.Acrc_mm:F5}");
        }
        {
            var stLong = LayeredState(SlsLong, CalcType.N, softening: false).StrainState;
            var strips = LayeredCrackStrips(SlsLong, stLong, 1.4, softening: false);
            foreach (var c in strips)
                sb.AppendLine($"Layered[{c.LayerName}/{c.Direction}, z={c.Z:F4}]: M_des={c.MDes:F4} " +
                              $"N_des={c.NDes:F2} Mcrc={c.Mcrc:F4} cracked={c.Cracked} " +
                              $"sigma_s={c.SigmaS / 1000.0:F3} sigma_s_crc={c.SigmaSCrc / 1000.0:F3} " +
                              $"psi_s={c.PsiS:F5} ls={c.LsM:F5} angle={c.CrackAngleDeg:F2} acrc={c.AcrcMm:F5}");
        }

        sb.AppendLine();
        sb.AppendLine("=== Влияние коэффициента пластичности γ в Wpl = γ·Wred ===");
        sb.AppendLine("γ=1,3 — СП 63 ф. (8.122); γ=1,75 — СНиП 2.03.01-84* (Гвоздев-Дмитриев)");
        foreach (var (tag, g) in new[] { ("γ=1,30", WplGammaMethod.Sp63), ("γ=1,75", WplGammaMethod.Snip2030184) })
        {
            var (wl, ws) = WaCrackWidths(g);
            var (cl, cs2) = CapriCrackWidths(g);
            var (ll, ls2) = LayeredCrackWidths(false, g);
            sb.AppendLine($"  {tag}:  WA {wl:F4}/{ws:F4}   Capri {cl:F4}/{cs2:F4}   Layered {ll:F4}/{ls2:F4}");
        }
        foreach (var (tag, g) in new[] { ("γ=1,30", WplGammaMethod.Sp63), ("γ=1,75", WplGammaMethod.Snip2030184) })
        {
            var stL = LayeredState(SlsLong, CalcType.N, softening: false).StrainState;
            var c = LayeredCrackStrips(SlsLong, stL, 1.4, false, g).MaxBy(x => x.AcrcMm)!;
            sb.AppendLine($"  Layered крит. {tag}: Mcrc={c.Mcrc:F3} sigma_s={c.SigmaS / 1000.0:F2} " +
                          $"sigma_s_crc={c.SigmaSCrc / 1000.0:F2} psi_s={c.PsiS:F4} " +
                          $"cracked={c.Cracked} acrc={c.AcrcMm:F4}");
        }
        {
            var d13 = Simpl(SlsLong, CapriSls, 1.4, WplGammaMethod.Sp63).CapriDirs!
                .Where(d => !d.Strip.NoRebar).MaxBy(d => d.Strip.Acrc_mm)!;
            var d175 = Simpl(SlsLong, CapriSls, 1.4, WplGammaMethod.Snip2030184).CapriDirs!
                .Where(d => !d.Strip.NoRebar).MaxBy(d => d.Strip.Acrc_mm)!;
            foreach (var (tag, d) in new[] { ("γ=1,30", d13), ("γ=1,75", d175) })
                sb.AppendLine($"  Capri крит. {tag}: alpha={d.Alpha_deg:F2} Mcrc={d.Strip.Mcrc:F3} " +
                              $"sigma_s={d.Strip.Sigma_s_MPa:F2} sigma_s_crc={d.Strip.Sigma_s_crc_MPa:F2} " +
                              $"psi_s={d.Strip.Psi_s:F4} acrc={d.Strip.Acrc_mm:F4}");
        }

        sb.AppendLine();
        sb.AppendLine("=== Разложение acrc по п. 8.2.7 (критическая полоса каждого метода) ===");
        sb.AppendLine("acrc1 — длительный набор, φ1=1,4;  acrc2 — полный набор, φ1=1,0;");
        sb.AppendLine("acrc3 — длительный набор, φ1=1,0;  acrc,long = acrc1;  acrc,short = acrc1+acrc2−acrc3");
        {
            void Head(string method, string strip) =>
                sb.AppendLine($"-- {method}, критическая полоса: {strip}");
            void Line(string tag, double m, double n, double mcrc, double x,
                      double sig, double sigCrc, double psi, double ls, double a) =>
                sb.AppendLine($"   {tag}: M={m,7:F3} N={n,8:F2} Mcrc={mcrc,6:F2} " +
                              $"x={(double.IsNaN(x) ? "     —" : x.ToString("F5", ci)),7} " +
                              $"sigma_s={sig,7:F2} sigma_s_crc={sigCrc,7:F2} psi_s={psi:F4} " +
                              $"ls={ls:F3} acrc={a:F4}");

            // Вуд-Армер
            {
                var a1 = Simpl(SlsLong, WaSls, 1.4).WaStrips!;
                var a2 = Simpl(SlsFull, WaSls, 1.0).WaStrips!;
                var a3 = Simpl(SlsLong, WaSls, 1.0).WaStrips!;
                int k = Enumerable.Range(0, a1.Count)
                    .MaxBy(i => a1[i].Acrc_mm + a2[i].Acrc_mm - a3[i].Acrc_mm);
                Head("Вуд-Армер", a1[k].Name);
                Line("acrc1", a1[k].M_des, a1[k].N_des, a1[k].Mcrc, a1[k].Xm, a1[k].Sigma_s_MPa, a1[k].Sigma_s_crc_MPa, a1[k].Psi_s, a1[k].Ls_m, a1[k].Acrc_mm);
                Line("acrc2", a2[k].M_des, a2[k].N_des, a2[k].Mcrc, a2[k].Xm, a2[k].Sigma_s_MPa, a2[k].Sigma_s_crc_MPa, a2[k].Psi_s, a2[k].Ls_m, a2[k].Acrc_mm);
                Line("acrc3", a3[k].M_des, a3[k].N_des, a3[k].Mcrc, a3[k].Xm, a3[k].Sigma_s_MPa, a3[k].Sigma_s_crc_MPa, a3[k].Psi_s, a3[k].Ls_m, a3[k].Acrc_mm);
            }

            // Капра-Мори
            {
                var a1 = Simpl(SlsLong, CapriSls, 1.4).CapriDirs!;
                var a2 = Simpl(SlsFull, CapriSls, 1.0).CapriDirs!;
                var a3 = Simpl(SlsLong, CapriSls, 1.0).CapriDirs!;
                int k = Enumerable.Range(0, a1.Count).Where(i => !a1[i].Strip.NoRebar)
                    .MaxBy(i => a1[i].Strip.Acrc_mm + a2[i].Strip.Acrc_mm - a3[i].Strip.Acrc_mm);
                Head("Капра-Мори", $"площадка alpha={a1[k].Alpha_deg:F2}, грань {(a1[k].Top ? "наружн." : "внутр.")}");
                foreach (var (tag, d) in new[] { ("acrc1", a1[k]), ("acrc2", a2[k]), ("acrc3", a3[k]) })
                    Line(tag, d.M_n, d.N_n, d.Strip.Mcrc, d.Strip.Xm, d.Strip.Sigma_s_MPa, d.Strip.Sigma_s_crc_MPa, d.Strip.Psi_s, d.Strip.Ls_m, d.Strip.Acrc_mm);
            }

            // Слоистая
            {
                var stL = LayeredState(SlsLong, CalcType.N, softening: false).StrainState;
                var stF = LayeredState(SlsFull, CalcType.N, softening: false).StrainState;
                var a1 = LayeredCrackStrips(SlsLong, stL, 1.4, softening: false);
                var a2 = LayeredCrackStrips(SlsFull, stF, 1.0, softening: false);
                var a3 = LayeredCrackStrips(SlsLong, stL, 1.0, softening: false);
                int k = Enumerable.Range(0, a1.Count)
                    .MaxBy(i => a1[i].AcrcMm + a2[i].AcrcMm - a3[i].AcrcMm);
                Head("Слоистая", $"{a1[k].LayerName}/{a1[k].Direction}, z={a1[k].Z:F4}");
                foreach (var (tag, c) in new[] { ("acrc1", a1[k]), ("acrc2", a2[k]), ("acrc3", a3[k]) })
                    Line(tag, c.MDes, c.NDes, c.Mcrc, double.NaN, c.SigmaS / 1000.0, c.SigmaSCrc / 1000.0, c.PsiS, c.LsM, c.AcrcMm);

                // Есть ли вообще сжатая зона по направлению x? (по решённой плоскости деформаций)
                foreach (var (name, st) in new[] { ("длительное", stL), ("полное", stF) })
                {
                    double h = Fixture.Section().H;
                    double eTop = st.EpsX(h / 2), eBot = st.EpsX(-h / 2);
                    string where = eTop > 0 && eBot > 0
                        ? "всё сечение растянуто по x — сжатой зоны НЕТ"
                        : $"нейтральная ось по x при z={(-eBot * h / (eTop - eBot) - h / 2):F5} м";
                    sb.AppendLine($"   eps_x: верх={eTop:G4} низ={eBot:G4} ({name}) → {where}");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== Угол трещин по слоистой модели ===");
        sb.AppendLine("angle — направление трещины от оси X (горизонтали); трещина перпендикулярна");
        sb.AppendLine("направлению главной растягивающей деформации eps1 в этом же слое.");
        foreach (var (name, f) in new[] { ("полное (соч. 2)", SlsFull), ("длительное (соч. 3)", SlsLong) })
        {
            var st = LayeredState(f, CalcType.N, softening: false).StrainState;
            foreach (var c in LayeredCrackStrips(f, st, 1.4, softening: false))
            {
                PlateSection.PrincipalStrains2D(st.EpsX(c.Z), st.EpsY(c.Z), st.GammaXY(c.Z),
                    out double e1, out double e2, out double theta);
                sb.AppendLine($"{name} [{c.LayerName}/{c.Direction}, z={c.Z:F4}]: " +
                              $"angle={c.CrackAngleDeg,7:F2} (eps1={e1:G4} под {theta * 180.0 / Math.PI:F2} град, " +
                              $"eps2={e2:G4}) cracked={c.Cracked} acrc={c.AcrcMm:F4}");
            }
        }
        {
            // Для сопоставления: у Капра-Мори критическая площадка задаётся нормалью alpha,
            // трещина в ней идёт перпендикулярно — по alpha + 90 град.
            var crit = Simpl(SlsLong, CapriSls, 1.4).CapriDirs!
                .Where(d => !d.Strip.NoRebar).MaxBy(d => d.Strip.Acrc_mm)!;
            double line = crit.Alpha_deg + 90.0;
            while (line > 90.0) line -= 180.0;
            sb.AppendLine($"Капра-Мори (длительное): крит. площадка alpha={crit.Alpha_deg:F2} град → " +
                          $"трещина по {line:F2} град от горизонтали, acrc={crit.Strip.Acrc_mm:F4}");
        }

        sb.AppendLine();
        sb.AppendLine("=== Профиль по направлениям (Капра-Мори), полный полуоборот, шаг 5° ===");
        sb.AppendLine("alpha — нормаль площадки от оси X (X — горизонталь, Y — вертикаль).");
        sb.AppendLine("acrc1 — длит. набор φ1=1,4;  acrc2 — полный набор φ1=1,0;  acrc3 — длит. набор φ1=1,0;");
        sb.AppendLine("acrc,short = acrc1+acrc2−acrc3.  Грань: В — наружная (верх), Н — внутренняя (низ).");
        sb.AppendLine("ВНИМАНИЕ: M_n/N_n/Mcrc/sigma_s/psi_s — по ДЛИТЕЛЬНОМУ сочетанию 3;");
        sb.AppendLine("последняя колонка Кисп — по сочетанию 1 (ПС1), её усилия в таблице не показаны.");
        sb.AppendLine("alpha  грань     M_n      N_n    Mcrc  трещ  sigma_s   psi_s   acrc1  acrc,short   Кисп(ПС1)");
        {
            var uls = Simpl(Uls, CapriUls).CapriDirs!;
            var a1 = Simpl(SlsLong, CapriSls, 1.4).CapriDirs!;
            var a2 = Simpl(SlsFull, CapriSls, 1.0).CapriDirs!;
            var a3 = Simpl(SlsLong, CapriSls, 1.0).CapriDirs!;

            double bestLong = a1.Where(d => !d.Strip.NoRebar).Max(d => d.Strip.Acrc_mm);
            double bestShort = Enumerable.Range(0, a1.Count).Where(i => !a1[i].Strip.NoRebar)
                .Max(i => a1[i].Strip.Acrc_mm + a2[i].Strip.Acrc_mm - a3[i].Strip.Acrc_mm);

            for (int deg = 0; deg < 180; deg += 5)
            {
                int i = (int)Math.Round(deg / StepDeg);
                var d = a1[i];
                double shortSum = d.Strip.Acrc_mm + a2[i].Strip.Acrc_mm - a3[i].Strip.Acrc_mm;
                string mark = "";
                if (Math.Abs(d.Strip.Acrc_mm - bestLong) < 1e-9) mark += "  <= max acrc,long";
                if (Math.Abs(shortSum - bestShort) < 1e-9) mark += "  <= max acrc,short";
                sb.AppendLine(
                    $"{deg,4}°    {(d.Top ? "В" : "Н")}  {d.M_n,8:F2} {d.N_n,8:F1} {d.Strip.Mcrc,7:F2} " +
                    $"{(d.Strip.Cracked ? " да " : " нет"),4} {d.Strip.Sigma_s_MPa,8:F2} {d.Strip.Psi_s,7:F3} " +
                    $"{d.Strip.Acrc_mm,7:F4} {shortSum,10:F4} {uls[i].Strip.Eta,10:F4}{mark}");
            }
            sb.AppendLine($"Максимумы: acrc,long = {bestLong:F4}, acrc,short = {bestShort:F4}");
        }

        sb.AppendLine();
        sb.AppendLine("Ref (Excel Г. Апхадзе): Кисп = 0.951 (запас 4.9%), угол главной площадки = 80 град; " +
                      "acrc,long = 0.048 мм, acrc,short = 0.085 мм");

        System.IO.File.WriteAllText(path, sb.ToString());
    }
}
