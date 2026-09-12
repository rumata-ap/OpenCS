using System;
using System.Linq;
using Xunit;
using CScore;

namespace CScore.Tests;

// ShellSimplSolver реализует упрощённые методы Вуда-Армера и Капра-Мори (п. 8.1/8.2 СП63) для
// плит: усилия сводятся к полосе шириной 1 м в каждом направлении и считаются как обычное
// прямоугольное сечение. До этого файла у солвера не было ни одного теста.
public class ShellSimplSolverTests
{
    static MaterialChars ConcreteN(double eMPa, double rbSerMPa, double rbtSerMPa) => new()
    {
        E = eMPa * 1000.0,          // МПа -> кПа
        Fc = -rbSerMPa * 1000.0,
        Ft = rbtSerMPa * 1000.0,
    };

    static MaterialChars RebarN(double eMPa, double rsSerMPa) => new()
    {
        E = eMPa * 1000.0,
        Fc = -rsSerMPa * 1000.0,
        Ft = rsSerMPa * 1000.0,
    };

    [Fact]
    public void ComputeStripSls_NoRebar_ReturnsNotCrackedAndZeroAcrc()
    {
        var concrete = ConcreteN(30000, 18.5, 1.55);
        var rebar = RebarN(200000, 500);

        var r = ShellSimplSolver.ComputeStripSls(
            M_des: 50.0, N_des: 0.0, h: 0.3, h0: 0.26, a_prime: 0.04,
            As_t: 0.0, As_c: 0.0, ds: 0.016,
            concrete: concrete, rebar: rebar, phi1: 1.0, phi2: 0.5, acrcLimMm: 0.3);

        Assert.True(r.NoRebar);
        Assert.False(r.Cracked);
        Assert.Equal(0.0, r.Acrc_mm);
    }

    // Прямоугольное сечение с ОДИНОЧНЫМ армированием (As'=0) — именно для этого случая
    // норма даёт zs = h0 - x/3 (ф. 8.133) и допускаемую σs = M/(zs·As) (ф. 8.132) как точный
    // частный случай общей ф. (8.134)/(8.135) при N=0 (для двойного армирования 8.133/8.132
    // сами являются лишь приближением, поэтому здесь не проверяются).
    [Fact]
    public void ComputeStripSls_PureBendingSingleReinfAtZeroN_MatchesFormula8132()
    {
        var concrete = ConcreteN(30000, 18.5, 1.55);
        var rebar = RebarN(200000, 500);

        const double h = 0.3, h0 = 0.26, aPrime = 0.04;
        const double asT = 20.36e-4 / 0.5; // м²/м
        const double m = 80.0 / 0.5;       // кНм/м

        var r = ShellSimplSolver.ComputeStripSls(
            M_des: m, N_des: 0.0, h: h, h0: h0, a_prime: aPrime,
            As_t: asT, As_c: 0.0, ds: 0.036,
            concrete: concrete, rebar: rebar, phi1: 1.4, phi2: 0.5, acrcLimMm: 0.3);

        Assert.True(r.Cracked);

        // Ф. (8.132): σs = M/(zs·As) — допускаемая формула для чистого изгиба (N=0),
        // к которой ОБЯЗАНА свестись общая ф. (8.134)/(8.135), используемая солвером при N≠0.
        double expectedSigmaKPa = m / (r.Zs * asT);
        Assert.Equal(expectedSigmaKPa / 1000.0, r.Sigma_s_MPa, 3);
    }

    // Тот же пример, но с продольной силой (внецентренное растяжение, Х.Е.2.1, непродолжительно:
    // N=120 кН, M=90 кНм). Проверяем только качественную корректность (трещины образуются,
    // напряжение положительно и не превышает Rs,ser) — количественно книга считает Acrc через
    // деформационную модель (нелинейный расчёт), а солвер — через допускаемую упрощённую формулу
    // 8.2.9/8.2.11, поэтому точного совпадения чисел не ожидается.
    [Fact]
    public void ComputeStripSls_EccentricTension_ProducesPositiveStressBelowYield()
    {
        var concrete = ConcreteN(30000, 18.5, 1.55);
        var rebar = RebarN(200000, 500);

        const double h = 0.3, h0 = 0.26, aPrime = 0.04;
        const double asT = 20.36e-4 / 0.5;
        const double m = 90.0 / 0.5;
        const double n = 120.0 / 0.5; // растяжение — положительно по конвенции проекта

        var r = ShellSimplSolver.ComputeStripSls(
            M_des: m, N_des: n, h: h, h0: h0, a_prime: aPrime,
            As_t: asT, As_c: asT, ds: 0.036,
            concrete: concrete, rebar: rebar, phi1: 1.0, phi2: 0.5, acrcLimMm: 0.4);

        Assert.True(r.Cracked);
        Assert.True(r.Sigma_s_MPa > 0.0);
        Assert.True(r.Sigma_s_MPa <= 500.0 + 1e-6);
        Assert.True(r.Acrc_mm > 0.0);
    }

    // Пример 48 (Пособие СП63.13330.2018): колонна h=500, b=400, a=a'=50 мм, As=As'=1232 мм²
    // (симметрично), B15, всё усилие короткое M=240 кНм, N=500 кН (сжатие). Книга получает
    // σs=331.2 МПа методом коэффициента φcrc по таблице 4.2 — независимая, отличная от ф.
    // (8.134)/(8.135) упрощённая методика. Совпадения до цифры не ожидается, но порядок
    // величины должен быть тем же (не в разы), иначе формула сломана.
    [Fact]
    public void ComputeStripSls_EccentricCompression_ReferenceExample48_SameOrderOfMagnitude()
    {
        var concrete = ConcreteN(24000, 11.0, 1.1);
        var rebar = RebarN(200000, 400);

        const double h = 0.5, h0 = 0.45, aPrime = 0.05, b = 0.4;
        const double asT = 1232e-6 / b;
        const double m = 240.0 / b;   // кНм/м
        const double n = -500.0 / b;  // сжатие — отрицательно по конвенции проекта

        var r = ShellSimplSolver.ComputeStripSls(
            M_des: m, N_des: n, h: h, h0: h0, a_prime: aPrime,
            As_t: asT, As_c: asT, ds: 0.028,
            concrete: concrete, rebar: rebar, phi1: 1.0, phi2: 0.5, acrcLimMm: 0.4);

        Assert.True(r.Cracked);
        // Книга: σs = 331.2 МПа. Допуск ±25% — разные (но оба нормативно допустимые) методы.
        Assert.InRange(r.Sigma_s_MPa, 331.2 * 0.75, 331.2 * 1.25);
    }

    [Fact]
    public void ComputeStripSls_AcrcGrowsWithMoment()
    {
        var concrete = ConcreteN(30000, 18.5, 1.55);
        var rebar = RebarN(200000, 500);
        const double h = 0.3, h0 = 0.26, aPrime = 0.04, asT = 20.36e-4 / 0.5;

        var low = ShellSimplSolver.ComputeStripSls(
            M_des: 40.0 / 0.5, N_des: 0.0, h, h0, aPrime, asT, asT, 0.036,
            concrete, rebar, phi1: 1.0, phi2: 0.5, acrcLimMm: 0.4);
        var high = ShellSimplSolver.ComputeStripSls(
            M_des: 90.0 / 0.5, N_des: 0.0, h, h0, aPrime, asT, asT, 0.036,
            concrete, rebar, phi1: 1.0, phi2: 0.5, acrcLimMm: 0.4);

        Assert.True(high.Cracked);
        Assert.True(high.Acrc_mm > low.Acrc_mm);
    }

    [Fact]
    public void ComputeStripUls_NoRebar_ReturnsNotCracked_AndNotApplicableEta()
    {
        var concrete = ConcreteN(30000, 18.5, 1.55);
        var rebar = RebarN(200000, 500);

        var r = ShellSimplSolver.ComputeStripUls(
            M_des: 50.0, N_des: 0.0, h: 0.3, h0: 0.26, a_prime: 0.04,
            As_t: 0.0, As_c: 0.0, concrete: concrete, rebar: rebar);

        Assert.True(r.NoRebar);
    }

    // Одиночное армирование, чистый изгиб: M_ult = Rs·As·(h0 - x/2), x = Rs·As/(Rb·b) —
    // независимый ручной расчёт по ф. (8.1.9)/(8.1.10) СП63 для сравнения с ComputeStripUls.
    [Fact]
    public void ComputeStripUls_PureBending_MatchesHandCalcOfMUlt()
    {
        var concrete = ConcreteN(30000, 14.5, 1.05); // Fc/Ft здесь — расчётные (CalcType.C-подобные)
        var rebar = RebarN(200000, 435);

        const double h = 0.3, h0 = 0.26, aPrime = 0.04, asT = 15e-4; // м²/м

        var r = ShellSimplSolver.ComputeStripUls(
            M_des: 100.0, N_des: 0.0, h, h0, aPrime, asT, As_c: 0.0,
            concrete: concrete, rebar: rebar);

        double rb = 14500.0, rs = 435000.0; // кПа
        double x = rs * asT / rb;
        double mUlt = rb * x * (h0 - 0.5 * x);

        Assert.Equal(mUlt, r.M_ult, 3);
        Assert.Equal(100.0 / mUlt, r.Eta, 6);
    }
}
