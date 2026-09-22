using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;
using CScore;
using CScore.Fem;

namespace CScore.Tests;

// Сверка трёх методов OpenCS (Вуд-Армер, Капра-Мори, слоистая модель) с двумя теориями
// ЛИРА-САПР 2024 (модуль армирования "Оболочка", СП 63.13330.2012/2018): теория Вуда и
// теория Карпенко. Источник: проект "Тест_стена_244", элемент 1 — КЭ сжатой стены в
// диагональной зоне (переписка с Георгием Апхадзе, 21.09.2026). Заменяет стену 177
// (ShellSimplLiraWall177Tests), где Nxy, Mxy ≈ 0 и все методы вырождались в одну полосу X:
// здесь сдвиг Txy и кручение Mxy заметны, и методы приведения усилий реально расходятся.
//
// Стена h = 200 мм, B25, A500 (Rs = Rsc = 435 МПа — как в Лире). У обеих граней, по X и
// по Y: ⌀12 шаг 200 (As = 5,65 см²/м), привязка центра арматуры 35 мм — армирование
// ПОЛНОСТЬЮ изотропно, поэтому перестановка X↔Y или общая смена знака моментов только
// переставляет подписи "верх/низ" у одного и того же экстремума. Важно лишь сохранить
// пары Nx↔Mx и Ny↔My (Mx и Nx дают σx — и в Лире, и в ShellSimplSolver).
//
// Пересчёт единиц: Лира выдаёт Nx, Ny, Txy в кН/м² — это напряжения (усилие на единичную
// толщину), их умножаем на h = 0,2 м (уточнение автора). Моменты — кН·м/м, как у нас.
// Знаки: N как есть (сжатие с минусом и там и там); моменты Лиры растягивают НИЖНЮЮ грань
// при M > 0, у OpenCS — верхнюю, поэтому M = −M_Лира (по изотропии на результат не влияет).
//
// Сравнение ведётся по коэффициенту использования Кисп (≤ 1 — проходит), а не по К.З Лиры:
//   Вуд-Армер, Капра-Мори — Кисп = demand / M_ult из ShellSimplSolver при фактических усилиях;
//   слоистая модель      — Кисп = M / M_пред при НЕИЗМЕННЫХ N (бисекция по множителю
//                          моментов до η(п. 8.1.30) = 1), та же основа "момент/момент";
//   трещины (все методы) — Кисп = max(acrc,long / 0,3; acrc,short / 0,4).
// Лира пересчитана как 1/К.З — только для ориентира: её К.З — запас по лучу ВСЕХ усилий,
// поэтому 1/К.З не тождественен Кисп.
//
// γb1: в отчёте Лиры для группы А (постоянные и длительные) стоит 0,9, для группы В — 1,0.
// Основной вариант здесь — γb1 = 1,0; вариант 0,9 выводится в Dump для справки.
public class ShellSimplLiraWall244Tests
{
    const double StepDeg = 0.5;
    const double H = 0.2;
    const double AcrcUltLong = 0.3;    // мм, п. 8.2.6 СП 63
    const double AcrcUltShort = 0.4;   // мм

    static class Fixture
    {
        public const double As = 565e-6;    // м²/м, ⌀12 шаг 200 (5,65 см²/м — из отчёта Лиры)
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

        /// <param name="gammaB1">γb1 к расчётным Rb, Rbt (в Лире 0,9 — для группы А).</param>
        public static Material Concrete(double gammaB1 = 1.0)
        {
            // B25 из отчёта Лиры: Rb=14.50, Rbt=1.05 (расчётные); Rbn=18.50, Rbtn=1.55 (норм.).
            var m = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0 };
            m.C = ConcreteChars(CalcType.C, 14_500.0 * gammaB1, 1_050.0 * gammaB1);
            m.CL = ConcreteChars(CalcType.CL, 14_500.0 * gammaB1, 1_050.0 * gammaB1);
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

        /// <summary>Снижение прочности сжатого бетона поперечным растяжением по Vecchio-Collins
        /// (только для слоистой модели). Переключается в Dump; тесты класса идут последовательно.</summary>
        public static bool Softening;

        public static PlateSection Section() => new()
        {
            H = H, NLayers = 40, PlateModel = "layered", ConcreteDiagramType = DiagrammType.L3,
            TensionConcrete = false,
            SofteningModel = Softening ? "vecchio_collins" : "",
            RebarLayers =
            [
                Layer("низ", -(H / 2 - Cover), As),
                Layer("верх", H / 2 - Cover, As),
            ],
        };

        static PlateRebarLayer Layer(string name, double z, double As_) => new()
        {
            Name = name, InputMode = "direct", Asx = As_, Asy = As_,
            Zsx = z, Zsy = z, DiameterX = 0.012, DiameterY = 0.012,
        };
    }

    readonly record struct Forces(double Nx, double Ny, double Nxy, double Mx, double My, double Mxy)
    {
        public Forces Scale(double k) => new(Nx * k, Ny * k, Nxy * k, Mx * k, My * k, Mxy * k);

        /// <summary>Строка РСУ Лиры: Nx, Ny, Txy в кН/м² (× h), моменты с обратным знаком.</summary>
        public static Forces FromLira(double nx, double ny, double txy, double mx, double my, double mxy)
            => new(nx * H, ny * H, txy * H, -mx, -my, -mxy);
    }

    // РСУ из отчёта Лиры (photo_138/139), расчётные значения — прочность (одно РСУ, 1 А1).
    // Qx = 23,0, Qy = −23,3 кН/м в методы приведения не входят и здесь не используются.
    static readonly Forces Uls1 = Forces.FromLira(-381.1412, -5264.4229, 197.4130, -29.3161, -47.6200, -15.8141);
    static readonly Forces[] UlsCombos = [Uls1];

    // Нормативные значения — трещины. По легенде Лиры: «А — сочетания с загружениями,
    // обладающими длительностью» (длительное), «В — все загружения» (полное).
    static readonly Forces SlsLong = Forces.FromLira(-277.1936, -3828.6702, 143.5730, -17.7262, -29.6378, -9.0564); // 1 А2
    static readonly Forces SlsFull = Forces.FromLira(-340.7172, -4706.0747, 176.4752, -25.9072, -42.1532, -13.9331); // 2 В2

    // Лира: 1/К.З из отчёта (Вуд — 1,056 / 1,091; Карпенко — 0,840 / 1,159).
    const double LiraWoodUls = 1.0 / 1.056, LiraWoodSls = 1.0 / 1.091;
    const double LiraKarpenkoUls = 1.0 / 0.840, LiraKarpenkoSls = 1.0 / 1.159;

    const string WaUls = "shell_simpl_wa_uls";
    const string CapriUls = "shell_simpl_capri_uls";
    const string WaSls = "shell_simpl_wa_sls";
    const string CapriSls = "shell_simpl_capri_sls";

    // ── Вызовы методов ──────────────────────────────────────────────────────────

    static ShellSimplSolver.SolveResult Simpl(Forces f, string kind, double phi1 = 1.0, double gammaB1 = 1.0)
        => ShellSimplSolver.Solve(
            new ShellSimplSolver.SolveParams(f.Nx, f.Ny, f.Nxy, f.Mx, f.My, f.Mxy, kind, StepDeg, AcrcUltLong, phi1, 0.5),
            Fixture.Section(), Fixture.Concrete(gammaB1), Fixture.Rebar(),
            kind.EndsWith("uls") ? CalcType.C : CalcType.N);

    static double SimplUlsEta(Forces f, string kind, double gammaB1 = 1.0)
        => Simpl(f, kind, 1.0, gammaB1).EtaMax ?? double.PositiveInfinity;

    static ShellLayeredCheck.Result LayeredUls(Forces f, double gammaB1 = 1.0)
    {
        var shell = new ShellLoadItem { Nx = f.Nx, Ny = f.Ny, Nxy = f.Nxy, Mx = f.Mx, My = f.My, Mxy = f.Mxy };
        return ShellLayeredCheck.CheckUls(Fixture.Section(), shell, Fixture.Concrete(gammaB1), Fixture.Rebar(),
            CalcType.C, DiagrammType.L3, out _, out _, out _);
    }

    static double LayeredUlsEta(Forces f, double gammaB1 = 1.0)
    {
        var r = LayeredUls(f, gammaB1);
        return r.Converged ? r.Utilization : double.PositiveInfinity;
    }

    // acrc,long = acrc(long, φ1=1.4); acrc,short = acrc(full,φ1=1.0) − acrc(long,φ1=1.0) + acrc(long,φ1=1.4)
    // (п. 8.2.5 СП 63), поэлементно по полосам/направлениям.
    static (double Long, double Short) Combine(IReadOnlyList<double> full10, IReadOnlyList<double> long10,
        IReadOnlyList<double> long14)
    {
        double aLong = 0, aShort = 0;
        for (int i = 0; i < full10.Count; i++)
        {
            double l = long14[i];
            double sh = full10[i] - long10[i] + l;
            if (l > aLong) aLong = l;
            if (sh > aShort) aShort = sh;
        }
        return (aLong, aShort);
    }

    static (double Long, double Short) WaCrackWidths(double k = 1.0)
    {
        List<double> A(Forces f, double phi1) => Simpl(f.Scale(k), WaSls, phi1).WaStrips!.Select(s => s.Acrc_mm).ToList();
        return Combine(A(SlsFull, 1.0), A(SlsLong, 1.0), A(SlsLong, 1.4));
    }

    static (double Long, double Short) CapriCrackWidths(double k = 1.0)
    {
        List<double> A(Forces f, double phi1) => Simpl(f.Scale(k), CapriSls, phi1).CapriDirs!
            .Select(d => d.Strip.NoRebar ? 0.0 : d.Strip.Acrc_mm).ToList();
        return Combine(A(SlsFull, 1.0), A(SlsLong, 1.0), A(SlsLong, 1.4));
    }

    static ShellStrainSolverResult LayeredState(Forces f, CalcType calc)
    {
        var concreteMat = Fixture.Concrete();
        var rebarMat = Fixture.Rebar();
        var cDiag = concreteMat.GetDiagramms(DiagrammType.L3)![calc];
        var rDiag = rebarMat.GetDiagramms(DiagrammCompatibility.Coerce(rebarMat.Type, DiagrammType.L2))![calc];
        var solver = new ShellStrainSolver(Fixture.Section(), cDiag, rDiag);
        return solver.SolveRobust([f.Nx, f.Ny, f.Nxy, f.Mx, f.My, f.Mxy], concreteMat, rebarMat, calc);
    }

    static IReadOnlyList<ShellCrackStripResult> LayeredCrackStrips(Forces f, ShellStrainState st, double phi1)
    {
        var section = Fixture.Section();
        var concreteMat = Fixture.Concrete();
        var rebarMat = Fixture.Rebar();
        var shell = new ShellLoadItem { Nx = f.Nx, Ny = f.Ny, Nxy = f.Nxy, Mx = f.Mx, My = f.My, Mxy = f.Mxy };
        var cDiag = concreteMat.GetDiagramms(DiagrammType.L3)![CalcType.N];
        var rDiag = rebarMat.GetDiagramms(DiagrammCompatibility.Coerce(rebarMat.Type, DiagrammType.L2))![CalcType.N];
        var crackingSolver = new ShellCrackingSolver(section, cDiag, rDiag);
        return ShellLayeredCrackWidth.ComputeAll(
            section, shell, st, concreteMat.chars[CalcType.N], rebarMat.chars[CalcType.N],
            phi1, 0.5, SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63,
            (target, alongX) => crackingSolver.Solve(target, alongX));
    }

    /// <summary>Ширины трещин слоистой модели; null — НДС при нормативных усилиях не найдено.</summary>
    static (double Long, double Short)? LayeredCrackWidths(double k = 1.0)
    {
        var full = SlsFull.Scale(k);
        var lng = SlsLong.Scale(k);
        var rFull = LayeredState(full, CalcType.N);
        var rLong = LayeredState(lng, CalcType.N);
        if (!rFull.Converged || !rLong.Converged) return null;

        List<double> A(Forces f, ShellStrainState st, double phi1)
            => LayeredCrackStrips(f, st, phi1).Select(s => s.AcrcMm).ToList();
        return Combine(A(full, rFull.StrainState, 1.0), A(lng, rLong.StrainState, 1.0),
            A(lng, rLong.StrainState, 1.4));
    }

    static double CrackUtil((double Long, double Short)? a)
        => a is { } v ? Math.Max(v.Long / AcrcUltLong, v.Short / AcrcUltShort) : double.PositiveInfinity;

    // ── Кисп ────────────────────────────────────────────────────────────────────

    static double WaUlsKisp(double gammaB1 = 1.0) => UlsCombos.Max(f => SimplUlsEta(f, WaUls, gammaB1));
    static double CapriUlsKisp(double gammaB1 = 1.0) => UlsCombos.Max(f => SimplUlsEta(f, CapriUls, gammaB1));

    /// <summary>
    /// Кисп слоистой модели = M / M_пред при неизменных N: бисекция по множителю моментов
    /// до η(п. 8.1.30) = 1. Потеря сходимости трактуется как исчерпание.
    /// </summary>
    static double LayeredMomentKisp(Forces f, double gammaB1 = 1.0)
    {
        double Util(double k)
            => LayeredUlsEta(f with { Mx = f.Mx * k, My = f.My * k, Mxy = f.Mxy * k }, gammaB1);

        double lo = 1.0, hi = 1.0;
        if (Util(1.0) >= 1.0)
        {
            do { hi = lo; lo *= 0.7; Assert.True(lo > 1e-3, "Предел по моменту не найден (k→0)"); }
            while (Util(lo) >= 1.0);
        }
        else
        {
            do { lo = hi; hi *= 1.3; Assert.True(hi < 50.0, "Предел по моменту не найден (k→∞)"); }
            while (Util(hi) < 1.0);
        }
        for (int i = 0; i < 40; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (Util(mid) >= 1.0) hi = mid; else lo = mid;
        }
        return 2.0 / (lo + hi);
    }

    static double LayeredUlsKisp(double gammaB1 = 1.0) => UlsCombos.Max(f => LayeredMomentKisp(f, gammaB1));

    static double WaSlsKisp() => CrackUtil(WaCrackWidths());
    static double CapriSlsKisp() => CrackUtil(CapriCrackWidths());
    static double LayeredSlsKisp() => CrackUtil(LayeredCrackWidths());

    // Сводка Кисп (21.09.2026), РСУ 1:
    //                 Лира Вуд  Лира Карпенко  Вуд-Армер  Капра-Мори  Слоистая  СП 63 упрощ.
    //                  (1/К.З)     (1/К.З)
    //   прочность       0,947       1,190        1,252       0,901      0,850       0,898
    //   трещины         0,917       0,863        0,993       0,735      0,531       0,417
    //
    // Вуд-Армер здесь самый строгий: к Mx добавляется весь |Mxy| (k = 1: 29,3 → 45,1), а
    // обжатие полосы x уменьшается на |Nxy| (−76 → −37 кН/м). Условие (8.100) допускает любое
    // k, и оптимальное k ≈ 0,38 переносит кручение в направление Y с большим запасом — отсюда
    // 0,898. Капра-Мори решает площадка α = 17°.

    // ── Упрощённый расчёт по СП 63 без разделения на слои (пп. 8.1.54, 8.1.57, 8.1.59) ──
    //
    // Прочность из плоскости стены — как для плиты по условиям (8.100)–(8.103), где Mx,ult и
    // My,ult — предельные моменты нормальных сечений с учётом продольной силы (п. 8.1.57):
    // здесь обе полосы сжаты, поэтому п. 8.1.14, ф. (8.10)–(8.13) при η = 1, записанные
    // независимо от ShellSimplSolver. Момент берётся относительно середины толщины (при
    // симметричном армировании — ц.т. сечения): M_ult(N) = Rb·b·x·(h0−x/2) + Rsc·A's·(h0−a') − N·(h0−a')/2.
    // Mxy,ult = min(0,1·Rb·b²·h; 0,5·Rs·(Asx+Asy)·h0) — (8.104), (8.105); для полосы единичной
    // ширины b = толщина стены, h = 1 м. Кисп = 1/λ, где λ — наибольший множитель моментов
    // (N неизменны), при котором выполняются все четыре условия.

    const double B1 = 1.0;   // ширина полосы, м

    /// <summary>П. 8.1.14, (8.10)–(8.13), η = 1: предельный момент сжатой полосы относительно середины толщины.</summary>
    static double Sp63MultCompressed(double nCompression, double rb)
    {
        Assert.True(nCompression > 0, "Упрощённый расчёт в тесте реализован только для сжатых полос");
        const double rs = 435_000.0, rsc = 435_000.0, es = 200_000_000.0;
        double As = Fixture.As, AsC = Fixture.As, h0 = H - Fixture.Cover, a = Fixture.Cover;
        double xiR = 0.8 / (1.0 + rs / es / 0.0035);                              // (8.1)
        double x = (nCompression + rs * As - rsc * AsC) / (rb * B1);               // (8.12)
        if (x / h0 > xiR)
            x = (nCompression + rs * As * (1 + xiR) / (1 - xiR) - rsc * AsC)
                / (rb * B1 + 2 * rs * As / (h0 * (1 - xiR)));                       // (8.13)
        double rhs = rb * B1 * x * (h0 - 0.5 * x) + rsc * AsC * (h0 - a);         // (8.10), правая часть
        return rhs - nCompression * (h0 - a) / 2.0;                               // e = e0 + (h0−a')/2
    }

    static (double Kisp, double MxUlt, double MyUlt, double MxyUlt) Sp63SimplifiedUls(Forces f, double gammaB1 = 1.0)
    {
        double rb = 14_500.0 * gammaB1;
        double mxUlt = Sp63MultCompressed(-f.Nx, rb);
        double myUlt = Sp63MultCompressed(-f.Ny, rb);
        double h0 = H - Fixture.Cover;
        double mbxy = 0.1 * rb * H * H * B1;                                      // (8.104)
        double msxy = 0.5 * 435_000.0 * (Fixture.As + Fixture.As) * h0;           // (8.105)
        double mxyUlt = Math.Min(mbxy, msxy);

        double mx = Math.Abs(f.Mx), my = Math.Abs(f.My), mxy = Math.Abs(f.Mxy);
        bool Ok(double l) =>
            (mxUlt - l * mx) * (myUlt - l * my) - l * l * mxy * mxy >= 0      // (8.100)
            && mxUlt >= l * mx && myUlt >= l * my && mxyUlt >= l * mxy;       // (8.101)–(8.103)

        double lo = 0.0, hi = 1.0;
        while (Ok(hi)) { lo = hi; hi *= 2; Assert.True(hi < 1e3); }
        for (int i = 0; i < 60; i++) { double mid = 0.5 * (lo + hi); if (Ok(mid)) lo = mid; else hi = mid; }
        return (1.0 / lo, mxUlt, myUlt, mxyUlt);
    }

    static double Sp63SimplifiedUlsKisp(double gammaB1 = 1.0) => UlsCombos.Max(f => Sp63SimplifiedUls(f, gammaB1).Kisp);

    /// <summary>
    /// П. 8.1.59: трещины — по изгибающим моментам БЕЗ крутящих, формулами раздела 8.2
    /// (ShellSimplSolver.ComputeStripSls — та же реализация 8.2.4–8.2.18, что у формульных
    /// проверок), для полос x и y у обеих граней с продольной силой своего направления.
    /// </summary>
    static List<double> Sp63StripAcrc(Forces f, double phi1)
    {
        var c = Fixture.Concrete().chars[CalcType.N];
        var r = Fixture.Rebar().chars[CalcType.N];
        double h0 = H - Fixture.Cover, a = Fixture.Cover;
        var list = new List<double>();
        foreach (var (m, n) in new[] { (f.Mx, f.Nx), (f.My, f.Ny) })
        {
            // Грань, растянутая моментом, и противоположная (при M = 0 трещины нет).
            list.Add(ShellSimplSolver.ComputeStripSls(Math.Max(0, m), n, H, h0, a, Fixture.As, Fixture.As, 0.012,
                c, r, phi1, 0.5, AcrcUltLong).Acrc_mm);
            list.Add(ShellSimplSolver.ComputeStripSls(Math.Max(0, -m), n, H, h0, a, Fixture.As, Fixture.As, 0.012,
                c, r, phi1, 0.5, AcrcUltLong).Acrc_mm);
        }
        return list;
    }

    static (double Long, double Short) Sp63CrackWidths()
        => Combine(Sp63StripAcrc(SlsFull, 1.0), Sp63StripAcrc(SlsLong, 1.0), Sp63StripAcrc(SlsLong, 1.4));

    static double Sp63SimplifiedSlsKisp() => CrackUtil(Sp63CrackWidths());

    [Fact]
    public void Sp63Simplified_Uls()
    {
        Assert.InRange(Sp63SimplifiedUlsKisp(), SP63_ULS_LO, SP63_ULS_HI);
    }

    [Fact]
    public void Sp63Simplified_Sls()
    {
        Assert.InRange(Sp63SimplifiedSlsKisp(), SP63_SLS_LO, SP63_SLS_HI);
    }

    // ── Прочность (ПС1) ─────────────────────────────────────────────────────────

    // Вуд-Армер сохраняет обжатие сжатого направления (ShellSimplSolver.WaMembraneCandidates).
    [Fact]
    public void WoodArmer_Uls_KeepsCompression()
    {
        var wa = Simpl(Uls1, WaUls);
        Assert.All(wa.WaStrips!, s => Assert.True(s.N_des < 0, $"{s.Name}: N_des = {s.N_des}"));
        Assert.InRange(WaUlsKisp(), WA_ULS_LO, WA_ULS_HI);
    }

    [Fact]
    public void CapraMori_Uls()
    {
        Assert.InRange(CapriUlsKisp(), CAPRI_ULS_LO, CAPRI_ULS_HI);
    }

    [Fact]
    public void Layered_Uls()
    {
        Assert.InRange(LayeredUlsKisp(), LAY_ULS_LO, LAY_ULS_HI);
    }

    // ── Трещиностойкость (ПС2) ──────────────────────────────────────────────────

    [Fact]
    public void WoodArmer_Sls()
    {
        Assert.InRange(WaSlsKisp(), WA_SLS_LO, WA_SLS_HI);
    }

    [Fact]
    public void CapraMori_Sls()
    {
        Assert.InRange(CapriSlsKisp(), CAPRI_SLS_LO, CAPRI_SLS_HI);
    }

    [Fact]
    public void Layered_Sls()
    {
        Assert.InRange(LayeredSlsKisp(), LAY_SLS_LO, LAY_SLS_HI);
    }

    // ── Выгрузка чисел ──────────────────────────────────────────────────────────

    [Fact]
    public void Dump()
    {
        string? path = Environment.GetEnvironmentVariable("SHELL_SIMPL_WALL244_DUMP");
        if (string.IsNullOrEmpty(path)) return;

        var ci = CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        void Row(string label, double v) => sb.AppendLine($"{label} = {v.ToString("F4", ci)}");

        sb.AppendLine("=== Лира, 1/К.З ===");
        Row("Lira Wood ULS", LiraWoodUls);
        Row("Lira Wood SLS", LiraWoodSls);
        Row("Lira Karpenko ULS", LiraKarpenkoUls);
        Row("Lira Karpenko SLS", LiraKarpenkoSls);

        sb.AppendLine();
        sb.AppendLine("=== ПС1, Кисп (РСУ 1), γb1 = 1,0 ===");
        Row("WA ULS", WaUlsKisp());
        Row("Capri ULS", CapriUlsKisp());
        Row("Layered ULS", LayeredUlsKisp());
        Row("SP63 simplified ULS", Sp63SimplifiedUlsKisp());
        foreach (var (f, name) in new[] { (Uls1, "РСУ1") })
        {
            var s = Sp63SimplifiedUls(f);
            sb.AppendLine($"  SP63 {name}: Кисп={s.Kisp:F4} Mx={Math.Abs(f.Mx):F3} Mx,ult={s.MxUlt:F3} " +
                          $"My={Math.Abs(f.My):F3} My,ult={s.MyUlt:F3} Mxy={Math.Abs(f.Mxy):F3} Mxy,ult={s.MxyUlt:F3}");
        }

        sb.AppendLine();
        sb.AppendLine("=== Слоистая с β-снижением по Vecchio-Collins ===");
        Fixture.Softening = true;
        try
        {
            Row("Layered+VC ULS (M/Mпред)", LayeredUlsKisp());
            foreach (var (f, name) in new[] { (Uls1, "РСУ1") })
                sb.AppendLine($"  {name}: Кисп(M/Mпред)={LayeredMomentKisp(f):F4} eta(деформ.)={LayeredUls(f).Utilization:F4}");
            var lv = LayeredCrackWidths();
            sb.AppendLine(lv is { } v2 ? $"Layered+VC SLS Кисп={CrackUtil(lv):F4} acrc_long={v2.Long:F4} acrc_short={v2.Short:F4}" : "Layered+VC SLS: не сошлось");
        }
        finally { Fixture.Softening = false; }

        sb.AppendLine();
        sb.AppendLine("=== ПС1, Кисп, γb1 = 0,9 ===");
        Row("WA ULS", WaUlsKisp(0.9));
        Row("Capri ULS", CapriUlsKisp(0.9));
        Row("Layered ULS", LayeredUlsKisp(0.9));
        Row("SP63 simplified ULS", Sp63SimplifiedUlsKisp(0.9));

        sb.AppendLine();
        sb.AppendLine("=== Вуд-Армер: чувствительность к N в паре с M + |Mxy| ===");
        {
            double nxy = Math.Abs(Uls1.Nxy);
            foreach (var (label, f) in new[]
            {
                ("как есть", Uls1),
                ("Nxy = 0 (N = Nx)", Uls1 with { Nxy = 0 }),
                ("Nxy = 0, Nx → Nx − |Nxy|", Uls1 with { Nxy = 0, Nx = Uls1.Nx - nxy, Ny = Uls1.Ny - nxy }),
                ("Nxy = 0, Nx → Nx + |Nxy|", Uls1 with { Nxy = 0, Nx = Uls1.Nx + nxy, Ny = Uls1.Ny + nxy }),
            })
            {
                var wa = Simpl(f, WaUls);
                var s = wa.WaStrips!.MaxBy(t => t.Eta)!;
                sb.AppendLine($"  {label}: Кисп={wa.EtaMax:F4} [{s.Name}] M={s.M_des:F3} N={s.N_des:F2} M_ult={s.M_ult:F3} demand={s.Demand:F3}");
            }
            // Запас по лучу, как К.З Лиры: все усилия (и N) × λ до η = 1.
            foreach (var kind in new[] { WaUls, CapriUls })
            {
                double lo = 0.1, hi = 5.0;
                for (int i = 0; i < 50; i++) { double mid = 0.5 * (lo + hi); if (SimplUlsEta(Uls1.Scale(mid), kind) >= 1.0) hi = mid; else lo = mid; }
                sb.AppendLine($"  {kind}: К(луч)={lo:F4} → 1/К={1.0 / lo:F4}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== Чувствительность к знаку Mxy относительно Nxy (Mxy без инверсии) ===");
        {
            var u = Uls1 with { Mxy = -Uls1.Mxy };
            var capri = Simpl(u, CapriUls);
            var c = capri.CapriDirs!.Where(d => !d.Strip.NoRebar).MaxBy(d => d.Strip.Eta)!;
            sb.AppendLine($"  Capri ULS Кисп={capri.EtaMax:F4} alpha={c.Alpha_deg:F1} M_n={c.M_n:F3} N_n={c.N_n:F2}");
            sb.AppendLine($"  Layered ULS Кисп(M/Mпред)={LayeredMomentKisp(u):F4}");
            sb.AppendLine($"  SP63 ULS Кисп={Sp63SimplifiedUls(u).Kisp:F4}; WA ULS Кисп={Simpl(u, WaUls).EtaMax:F4}");
            List<double> A(Forces f, double phi1) => Simpl(f with { Mxy = -f.Mxy }, CapriSls, phi1).CapriDirs!
                .Select(d => d.Strip.NoRebar ? 0.0 : d.Strip.Acrc_mm).ToList();
            var cc = Combine(A(SlsFull, 1.0), A(SlsLong, 1.0), A(SlsLong, 1.4));
            sb.AppendLine($"  Capri SLS Кисп={CrackUtil(cc):F4} long={cc.Long:F4} short={cc.Short:F4}");
            var full = SlsFull with { Mxy = -SlsFull.Mxy }; var lng = SlsLong with { Mxy = -SlsLong.Mxy };
            var rF = LayeredState(full, CalcType.N); var rL = LayeredState(lng, CalcType.N);
            if (rF.Converged && rL.Converged)
            {
                List<double> B(Forces f, ShellStrainState st, double phi1) => LayeredCrackStrips(f, st, phi1).Select(x => x.AcrcMm).ToList();
                var lc = Combine(B(full, rF.StrainState, 1.0), B(lng, rL.StrainState, 1.0), B(lng, rL.StrainState, 1.4));
                sb.AppendLine($"  Layered SLS Кисп={CrackUtil(lc):F4} long={lc.Long:F4} short={lc.Short:F4}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== ПС1 по РСУ ===");
        foreach (var (f, name) in new[] { (Uls1, "РСУ1") })
        {
            var wa = Simpl(f, WaUls);
            sb.AppendLine($"{name}: WA Кисп={wa.EtaMax:F4}");
            foreach (var s in wa.WaStrips!)
                sb.AppendLine($"  WA[{s.Name}]: M_des={s.M_des:F3} N_des={s.N_des:F2} x={s.Xm:F5} " +
                              $"M_ult={s.M_ult:F3} demand={s.Demand:F3} eta={s.Eta:F4} [{s.Case}]");
            var capri = Simpl(f, CapriUls);
            var c = capri.CapriDirs!.Where(d => !d.Strip.NoRebar).MaxBy(d => d.Strip.Eta)!;
            sb.AppendLine($"{name}: Capri Кисп={capri.EtaMax:F4} alpha={c.Alpha_deg:F1} top={c.Top} " +
                          $"M_n={c.M_n:F3} N_n={c.N_n:F2} [{c.Strip.Case}]");
            var lay = LayeredUls(f);
            sb.AppendLine($"{name}: Layered Кисп(M/Mпред)={LayeredMomentKisp(f):F4} " +
                          $"eta(деформ.)={lay.Utilization:F4} {lay.Formula}, {lay.Description}");
        }

        sb.AppendLine();
        sb.AppendLine("=== ПС2, Кисп = max(acrc,long/0,3; acrc,short/0,4) ===");
        var (waL, waS) = WaCrackWidths();
        sb.AppendLine($"WA Кисп={WaSlsKisp():F4} acrc_long={waL:F4} acrc_short={waS:F4}");
        var (cL, cS) = CapriCrackWidths();
        sb.AppendLine($"Capri Кисп={CapriSlsKisp():F4} acrc_long={cL:F4} acrc_short={cS:F4}");
        var lw = LayeredCrackWidths();
        sb.AppendLine(lw is { } v ? $"Layered Кисп={LayeredSlsKisp():F4} acrc_long={v.Long:F4} acrc_short={v.Short:F4}"
                                  : "Layered: не сошлось");
        var (spL, spS) = Sp63CrackWidths();
        sb.AppendLine($"SP63 simplified Кисп={Sp63SimplifiedSlsKisp():F4} acrc_long={spL:F4} acrc_short={spS:F4}");

        sb.AppendLine();
        sb.AppendLine("=== Критические полосы по трещинам (полное φ1=1,0 и длительное φ1=1,4) ===");
        foreach (var (f, phi1, tag) in new[] { (SlsFull, 1.0, "полное"), (SlsLong, 1.4, "длит.") })
        {
            var w = Simpl(f, WaSls, phi1).WaStrips!.MaxBy(s => s.Acrc_mm)!;
            sb.AppendLine($"  WA {tag}[{w.Name}]: M={w.M_des:F3} N={w.N_des:F2} Mcrc={w.Mcrc:F3} xm={w.Xm:F4} " +
                          $"sigma_s={w.Sigma_s_MPa:F2} sigma_s_crc={w.Sigma_s_crc_MPa:F2} psi_s={w.Psi_s:F4} ls={w.Ls_m:F3} acrc={w.Acrc_mm:F4}");
            var cd = Simpl(f, CapriSls, phi1).CapriDirs!.Where(d => !d.Strip.NoRebar).MaxBy(d => d.Strip.Acrc_mm)!;
            var c = cd.Strip;
            sb.AppendLine($"  Capri {tag}[alpha={cd.Alpha_deg:F1} top={cd.Top}]: M={c.M_des:F3} N={c.N_des:F2} Mcrc={c.Mcrc:F3} xm={c.Xm:F4} " +
                          $"sigma_s={c.Sigma_s_MPa:F2} sigma_s_crc={c.Sigma_s_crc_MPa:F2} psi_s={c.Psi_s:F4} ls={c.Ls_m:F3} acrc={c.Acrc_mm:F4}");
            {
                var cN = Fixture.Concrete().chars[CalcType.N];
                var rN = Fixture.Rebar().chars[CalcType.N];
                var p = ShellSimplSolver.ComputeStripSls(Math.Abs(f.Mx), f.Nx, H, H - Fixture.Cover, Fixture.Cover,
                    Fixture.As, Fixture.As, 0.012, cN, rN, phi1, 0.5, AcrcUltLong);
                sb.AppendLine($"  SP63 8.1.59 {tag}[x]: M={p.M_des:F3} N={p.N_des:F2} Mcrc={p.Mcrc:F3} xm={p.Xm:F4} " +
                              $"sigma_s={p.Sigma_s_MPa:F2} sigma_s_crc={p.Sigma_s_crc_MPa:F2} psi_s={p.Psi_s:F4} ls={p.Ls_m:F3} acrc={p.Acrc_mm:F4}");
            }
            var st = LayeredState(f, CalcType.N).StrainState;
            var l = LayeredCrackStrips(f, st, phi1).MaxBy(s => s.AcrcMm)!;
            sb.AppendLine($"  Layered {tag}[{l.LayerName}/{l.Direction}]: M={l.MDes:F3} N={l.NDes:F2} Mcrc={l.Mcrc:F3} " +
                          $"sigma_s={l.SigmaS / 1000:F2} sigma_s_crc={l.SigmaSCrc / 1000:F2} psi_s={l.PsiS:F4} ls={l.LsM:F3} " +
                          $"angle={l.CrackAngleDeg:F1} acrc={l.AcrcMm:F4}");
        }

        sb.AppendLine();
        sb.AppendLine("=== Слоистая, РСУ1 на пределе по моменту ===");
        {
            double k = 1.0 / LayeredMomentKisp(Uls1);
            var lim = LayeredUls(Uls1 with { Mx = Uls1.Mx * k * 0.999, My = Uls1.My * k * 0.999, Mxy = Uls1.Mxy * k * 0.999 });
            sb.AppendLine($"  k={k:F4}: Mx={Uls1.Mx * k:F2} My={Uls1.My * k:F2} Кисп(деф.)={lim.Utilization:F4} {lim.Formula}, {lim.Description}");

            // Предел по одному направлению: масштабируется только Mx (или только My), остальное — РСУ1.
            foreach (bool alongX in new[] { true, false })
            {
                double U(double s) => LayeredUlsEta(alongX ? Uls1 with { Mx = Uls1.Mx * s } : Uls1 with { My = Uls1.My * s });
                double lo = 1.0, hi = 1.0;
                do { lo = hi; hi *= 1.2; } while (U(hi) < 1.0 && hi < 20);
                for (int i = 0; i < 40; i++) { double mid = 0.5 * (lo + hi); if (U(mid) >= 1.0) hi = mid; else lo = mid; }
                sb.AppendLine(alongX
                    ? $"  только Mx: Mx,пред={Math.Abs(Uls1.Mx) * lo:F2} (Mx,ult по СП 63 = {Sp63SimplifiedUls(Uls1).MxUlt:F2})"
                    : $"  только My: My,пред={Math.Abs(Uls1.My) * lo:F2} (My,ult по СП 63 = {Sp63SimplifiedUls(Uls1).MyUlt:F2})");
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== ПС2: запас по лучу (все усилия × λ) и обратный ход acrc = acrc,ult/К ===");
        {
            (double Long, double Short) Sp63At(double k)
                => Combine(Sp63StripAcrc(SlsFull.Scale(k), 1.0), Sp63StripAcrc(SlsLong.Scale(k), 1.0), Sp63StripAcrc(SlsLong.Scale(k), 1.4));
            var methods = new (string Name, Func<double, (double Long, double Short)?> At)[]
            {
                ("WA", k => WaCrackWidths(k)),
                ("Capri", k => CapriCrackWidths(k)),
                ("SP63 8.1.59", k => Sp63At(k)),
                ("Layered", k => LayeredCrackWidths(k)),
            };
            foreach (var (name, at) in methods)
            {
                double U(double k) => CrackUtil(at(k));
                double lo = 1.0, hi = 1.0;
                if (U(1.0) >= 1.0) { do { hi = lo; lo *= 0.8; } while (U(lo) >= 1.0); }
                else { do { lo = hi; hi *= 1.25; } while (U(hi) < 1.0 && hi < 20); }
                for (int i = 0; i < 30; i++) { double mid = 0.5 * (lo + hi); if (U(mid) >= 1.0) hi = mid; else lo = mid; }
                double kRay = 0.5 * (lo + hi);
                var a1 = at(1.0)!.Value; var aK = at(kRay)!.Value;
                var above = at(hi);
                sb.AppendLine($"  {name}: при λ={hi:F4} " + (above is { } ab ? $"long={ab.Long:F4} short={ab.Short:F4}" : "НДС не найдено"));
                sb.AppendLine($"  {name}: К(луч)={kRay:F4}; при λ=К long={aK.Long:F4} short={aK.Short:F4}; " +
                              $"факт при λ=1 long={a1.Long:F4} short={a1.Short:F4}; обратный ход 0,3/К={0.3 / kRay:F4} 0,4/К={0.4 / kRay:F4}");
            }
            foreach (var (name, k) in new[] { ("Lira Wood", 1.091), ("Lira Karpenko", 1.159) })
                sb.AppendLine($"  {name}: К={k:F3}; обратный ход 0,3/К={0.3 / k:F4} 0,4/К={0.4 / k:F4}");
        }

        sb.AppendLine();
        sb.AppendLine("=== Полосы Вуда, длительное, φ1=1,4 ===");
        foreach (var c in Simpl(SlsLong, WaSls, 1.4).WaStrips!)
            sb.AppendLine($"  WA[{c.Name}]: M_des={c.M_des:F3} N_des={c.N_des:F2} Mcrc={c.Mcrc:F3} cracked={c.Cracked} " +
                          $"sigma_s={c.Sigma_s_MPa:F2} psi_s={c.Psi_s:F4} ls={c.Ls_m:F4} acrc={c.Acrc_mm:F4}");

        File.WriteAllText(path, sb.ToString());
    }

    // Регрессионные диапазоны (±~3% от чисел сводки выше).
    const double WA_ULS_LO = 1.21, WA_ULS_HI = 1.29, CAPRI_ULS_LO = 0.87, CAPRI_ULS_HI = 0.93;
    const double LAY_ULS_LO = 0.82, LAY_ULS_HI = 0.88;
    const double WA_SLS_LO = 0.96, WA_SLS_HI = 1.03, CAPRI_SLS_LO = 0.71, CAPRI_SLS_HI = 0.76;
    // 22.09.2026: трещинообразование слоистой модели — по лучу всех моментов (ShellCrackingSolver):
    // было 0,259 — порог по одному Mx пропускал трещину от длительного сочетания (acrc,long = 0),
    // а σs,crc при Mx = M_crc и полном Mxy давала ψs = 0,32 вместо 0,57.
    const double LAY_SLS_LO = 0.50, LAY_SLS_HI = 0.55;
    const double SP63_ULS_LO = 0.87, SP63_ULS_HI = 0.93, SP63_SLS_LO = 0.40, SP63_SLS_HI = 0.43;
}
