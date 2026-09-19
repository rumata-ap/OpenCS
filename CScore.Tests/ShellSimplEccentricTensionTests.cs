using System;
using System.Linq;
using Xunit;
using CScore;

namespace CScore.Tests;

// Внецентренное растяжение в упрощённых (формульных) проверках плитного сечения.
// Нормативная база — СП 63.13330.2018:
//   ПС1, п. 8.1.19, рисунок 8.4:
//     а) N между равнодействующими усилий в арматуре S и S':
//        N·e  ≤ M_ult  = Rs·A's·(h0−a')   (8.20), (8.22)
//        N·e' ≤ M'_ult = Rs·As ·(h0−a')   (8.21), (8.23)
//        где e — расстояние от N до As, e' — от N до A's (рисунок 8.4, а). То есть плечо
//        до ОДНОГО стержня спаривается с несущей способностью ДРУГОГО.
//     б) N за пределами этого расстояния: условие (8.20) с M_ult по (8.24) и
//        x = (Rs·As − Rsc·A's − N)/(Rb·b)  (8.25).
//   ПС2, п. 8.2.16 и 8.2.28:
//        x_m = x_M ± I_red·N/(A_red·M)     (8.154), знак «−» при растягивающей N,
//        где x_M — высота сжатой зоны ИЗГИБАЕМОГО элемента по (8.149)–(8.152),
//        а I_red, A_red — характеристики полного сечения (без учёта трещин).
//        σs = [M·(h0−y_c)/I_red ± N/A_red]·αs1  (8.134), y_c = x_m.
//
// Сечение всюду: h = 200 мм, B25, A500 (Rs = 435, Rsc = 400, Rs,ser = 500), привязка 35 мм.
public class ShellSimplEccentricTensionTests
{
    const double Cover = 0.035;
    const double H = 0.2;
    const double H0 = H - Cover;          // 0,165
    const double Arm = H0 - Cover;        // h0 − a' = 0,130
    const double As12s100 = 1131e-6;      // ⌀12 шаг 100, м²/м

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

    static PlateSection Section(double asTop, double asBot) => new()
    {
        H = H, NLayers = 40, PlateModel = "layered", ConcreteDiagramType = DiagrammType.L3,
        TensionConcrete = false,
        RebarLayers =
        [
            new PlateRebarLayer { Name = "низ", InputMode = "direct", Asx = asBot, Asy = asBot,
                                  Zsx = -(H / 2 - Cover), Zsy = -(H / 2 - Cover), DiameterX = 0.012, DiameterY = 0.012 },
            new PlateRebarLayer { Name = "верх", InputMode = "direct", Asx = asTop, Asy = asTop,
                                  Zsx = H / 2 - Cover, Zsy = H / 2 - Cover, DiameterX = 0.012, DiameterY = 0.012 },
        ],
    };

    // Полоса "x, верх" Вуд-Армера при усилиях только по x: Mxy = 0 и Ny = Nxy = 0, поэтому
    // приведение Вуда вырождается в тождество и в полосу попадают ровно M и N.
    static ShellSimplStripResult XTopStrip(double m, double n, string kind,
        double asTop = As12s100, double asBot = As12s100, double phi1 = 1.0)
    {
        var r = ShellSimplSolver.Solve(
            new ShellSimplSolver.SolveParams(n, 0, 0, m, 0, 0, kind, 0.5, 0.3, phi1, 0.5),
            Section(asTop, asBot), Concrete(), Rebar(),
            kind.EndsWith("uls") ? CalcType.C : CalcType.N);
        return r.WaStrips!.First(s => s.Name == "x, верх");
    }

    // ── ПС1, п. 8.1.19 ──────────────────────────────────────────────────────────

    // Ветвь 8.1.19б при x ≤ 0: формула (8.25) даёт отрицательную высоту сжатой зоны, то есть
    // сжатой зоны нет и предпосылка (8.24) с Rsc·A's не выполняется. Норма этот случай не
    // оговаривает; физически сечение работает как в случае «а» — оба стержня растянуты, и
    // несущая способность определяется условием (8.21): N·e' ≤ Rs·As·(h0−a').
    //
    // Усилия — площадка α = 15° стены Апхадзе (сочетание 1): M = 33,93, N = +436,1.
    // x по (8.25) = (435·1131e-6·10³ − 400·1131e-6·10³ − 436,1)/14500 = −0,0273 < 0.
    // Равновесие: F_t + F_c = 436,1, (F_t − F_c)·0,065 = 33,93 → F_t = 479 ≤ Rs·As = 492 кН/м.
    // Условие (8.21): N·(e0 + arm/2) = 436,1·0,1428 = 62,28 ≤ Rs·As·arm = 63,96 → η = 0,974.
    [Fact]
    public void Uls_8119b_NoCompressionZone_FallsBackToCondition821()
    {
        var s = XTopStrip(m: 33.93, n: 436.1, kind: "shell_simpl_wa_uls");

        Assert.InRange(s.Eta, 0.96, 0.99);
    }

    // На границе e0 = arm/2 ветви «а» и «б» обязаны сходиться: при e0 → arm/2 условие (8.21)
    // одно и то же с обеих сторон. Сейчас demand ветви «б» равен N·(e0 − arm/2) → 0, из-за чего
    // η падает скачком в десять раз на соседних градусах перебора Капра-Мори.
    [Fact]
    public void Uls_8119_EtaIsContinuousAcrossBranchBoundary()
    {
        const double n = 436.1;
        double mBoundary = n * Arm / 2.0;          // e0 = arm/2 ровно

        double etaInside = XTopStrip(mBoundary * 0.99, n, "shell_simpl_wa_uls").Eta;   // ветвь «а»
        double etaOutside = XTopStrip(mBoundary * 1.01, n, "shell_simpl_wa_uls").Eta;  // ветвь «б»

        Assert.InRange(Math.Abs(etaOutside - etaInside), 0.0, 0.05);
    }

    // Случай «а», п. 8.1.19: плечо до одного стержня спаривается с несущей способностью
    // ДРУГОГО (рисунок 8.4, а: N·e ≤ Rs·A's·(h0−a'), N·e' ≤ Rs·As·(h0−a')).
    // Асимметричное армирование: верх ⌀12 шаг 100, низ вдвое меньше.
    // N = 300, e0 = 0,05 < arm/2 = 0,065:
    //   N·e  = 300·0,015 = 4,5   ≤ Rs·As_низ·arm = 31,98  → η = 0,141
    //   N·e' = 300·0,115 = 34,5  ≤ Rs·As_верх·arm = 63,96 → η = 0,539  ← определяющее
    [Fact]
    public void Uls_8119a_PairsLeverArmWithOppositeRebar()
    {
        var s = XTopStrip(m: 15.0, n: 300.0, kind: "shell_simpl_wa_uls",
                          asTop: As12s100, asBot: As12s100 / 2.0);

        Assert.InRange(s.Eta, 0.53, 0.55);
    }

    // ── ПС2, п. 8.2.16 / 8.2.28 ────────────────────────────────────────────────

    // Ф. (8.154): растягивающая N уменьшает высоту сжатой зоны.
    // x_M = 0,05649 (изгиб, ф. 8.151); I_red/A_red = 3,4716e-3 м² (полное сечение, αs1 = 16,216).
    // При N = 100, M = 16,9: x_m = 0,05649 − 3,4716e-3·100/16,9 = 0,03595.
    [Fact]
    public void Sls_CompressionZoneHeight_ReducedByTensileN_Formula8154()
    {
        double xBending = XTopStrip(m: 16.9, n: 0.0, kind: "shell_simpl_wa_sls").Xm;
        double xTension = XTopStrip(m: 16.9, n: 100.0, kind: "shell_simpl_wa_sls").Xm;

        Assert.InRange(xBending, 0.0560, 0.0570);          // чистый изгиб — как сейчас
        // Допуск сужен намеренно, чтобы различить коэффициент приведения в I_red/A_red:
        // αs1 = Es/Eb,red = 16,216 даёт I_red/A_red = 3472 мм² и x_m = 0,03595, а начальный
        // α = Es/Eb = 6,667 дал бы 3396 мм² и 0,03640. Принят αs1 — п. 8.2.16 предписывает
        // определять x_m «согласно 8.2.28, принимая αs2 = αs1», а (8.154) входит в этот расчёт.
        Assert.InRange(xTension, 0.0357, 0.0362);          // с поправкой (8.154)
    }

    // Когда (8.154) даёт x_m ≤ 0, сжатой зоны нет: сечение растянуто насквозь, бетон из работы
    // выключен, и оба ряда арматуры работают на растяжение. Напряжение определяется равновесием
    // двух рядов: F_t + F_c = N, (F_t − F_c)·arm = 2M.
    // M = 16,9, N = +350: F_t = 350/2 + 16,9/0,130 = 305,0 кН/м → σs = 305,0/11,31 см² = 269,7 МПа.
    [Fact]
    public void Sls_SigmaS_FullyCrackedSection_MatchesTwoLayerEquilibrium()
    {
        var s = XTopStrip(m: 16.9, n: 350.0, kind: "shell_simpl_wa_sls");

        Assert.True(s.Cracked, "Сечение обязано трещать при N = +350 кН/м");
        Assert.InRange(s.Sigma_s_MPa, 265.0, 275.0);
    }

    // ── σs,crc по п. 8.2.18 ────────────────────────────────────────────────────

    // П. 8.2.18: ψs = 1 − 0,8·σs,crc/σs (8.137), где σs,crc — «напряжение в продольной растянутой
    // арматуре в сечении с трещиной сразу после образования нормальных трещин, определяемое
    // ПО 8.2.16, ПРИНИМАЯ В СООТВЕТСТВУЮЩИХ ФОРМУЛАХ ЗНАЧЕНИЯ M = M_crc». То есть σs,crc — не
    // самостоятельная формула, а та же σs с подстановкой Mcrc; продольная сила при этом остаётся.
    //
    // Полоса M = 16,9, N = +350: Mcrc = 14,710 − 350·0,03394 = 2,831 кН·м/м, сечение растянуто
    // насквозь, поэтому σs,crc считается тем же равновесием двух рядов, что и σs:
    //     F = (Mcrc + N·(h/2 − a'))/(h0 − a') = (2,831 + 22,75)/0,130 = 196,8 кН/м
    //     σs,crc = 196,8 / 11,31 см² = 174,0 МПа
    // и тогда ψs = 1 − 0,8·174,0/269,7 = 0,484.
    [Fact]
    public void Sls_SigmaSCrc_IsStressAtCrackingMoment()
    {
        var s = XTopStrip(m: 16.9, n: 350.0, kind: "shell_simpl_wa_sls");

        Assert.InRange(s.Mcrc, 2.80, 2.87);
        Assert.InRange(s.Sigma_s_crc_MPa, 172.0, 176.0);
        Assert.InRange(s.Psi_s, 0.478, 0.490);
    }

    // При чистом изгибе (N = 0) подстановка M = Mcrc в ту же упругую модель сечения с трещиной
    // вырождает отношение σs,crc/σs в Mcrc/M, то есть ψs совпадает с упрощённой ф. (8.138)
    // «для изгибаемых элементов». Это внутренняя проверка согласованности определения.
    [Fact]
    public void Sls_SigmaSCrc_PureBending_DegeneratesToMomentRatio()
    {
        var s = XTopStrip(m: 30.0, n: 0.0, kind: "shell_simpl_wa_sls");

        Assert.True(s.Cracked);
        double psiByMoment = 1.0 - 0.8 * s.Mcrc / 30.0;
        Assert.Equal(psiByMoment, s.Psi_s, 3);
    }

    // ── Вторая грань при сквозном растяжении ───────────────────────────────────

    // Пока есть сжатая зона, трещит только грань, растягиваемая моментом, и выбор её по знаку
    // M_n верен. Но при x_m ≤ 0 растянуты ОБА ряда, и трещина по второй грани существует —
    // причём если она армирована слабее, то именно она и решает.
    //
    // Асимметрия: верх ⌀12 шаг 100 (11,31 см²/м), низ 4,0 см²/м. M = 5, N = +350:
    //   F_t (верх) = (M + N·(h/2 − a'))/arm = (5 + 22,75)/0,130 = 213,5 кН/м → σs = 188,8 МПа
    //   F_c (низ)  = N − F_t = 136,5 кН/м   → σs = 136,5/4,0 см² = 341,3 МПа  ← решает
    [Fact]
    public void Sls_ThroughTension_SecondFaceGoverns_WhenReinforcedLess()
    {
        var capri = ShellSimplSolver.Solve(
            new ShellSimplSolver.SolveParams(350.0, 0, 0, 5.0, 0, 0,
                "shell_simpl_capri_sls", 0.5, 0.3, 1.0, 0.5),
            Section(asTop: As12s100, asBot: 4.0e-4), Concrete(), Rebar(), CalcType.N);

        var along = capri.CapriDirs!.First(d => Math.Abs(d.Alpha_deg) < 1e-9);

        Assert.True(along.Strip.Cracked);
        Assert.InRange(along.Strip.Sigma_s_MPa, 330.0, 350.0);
    }

    // Проверка того, что вторая грань не «перехватывает» управление там, где её нет: при
    // наличии сжатой зоны (малая N, тот же момент) решает грань, растянутая моментом.
    [Fact]
    public void Sls_WithCompressionZone_MomentFaceStillGoverns()
    {
        var capri = ShellSimplSolver.Solve(
            new ShellSimplSolver.SolveParams(0, 0, 0, 20.0, 0, 0,
                "shell_simpl_capri_sls", 0.5, 0.3, 1.0, 0.5),
            Section(asTop: As12s100, asBot: 4.0e-4), Concrete(), Rebar(), CalcType.N);

        var along = capri.CapriDirs!.First(d => Math.Abs(d.Alpha_deg) < 1e-9);

        Assert.True(along.Top, "при чистом изгибе с M > 0 решать обязана верхняя грань");
        Assert.True(along.Strip.Xm > 0.0, $"сжатая зона обязана быть: x_m = {along.Strip.Xm:F4}");
    }

    // Нижняя граница, не зависящая от модели сечения: при центральном растяжении всю силу
    // несут оба ряда арматуры, σs = N/(As + A's) = 350/(2·11,31 см²) = 154,7 МПа.
    [Fact]
    public void Sls_SigmaS_PureTension_EqualsForceOverTotalRebar()
    {
        var s = XTopStrip(m: 0.001, n: 350.0, kind: "shell_simpl_wa_sls");

        Assert.InRange(s.Sigma_s_MPa, 153.0, 157.0);
    }
}
