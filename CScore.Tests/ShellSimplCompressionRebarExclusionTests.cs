using System;
using Xunit;
using CScore;

namespace CScore.Tests;

// Сжатая арматура при x ≤ 2a' в формульной проверке прочности полосы плиты (ПС1).
//
// СП 63.13330.2018 оговаривает этот случай только для изгиба, п. 8.1.13: «Если вычисленная без
// учёта сжатой арматуры (A's = 0) высота сжатой зоны x < 2a', в формулу (8.9) подставляют
// вместо a' значение x/2». В п. 8.1.14 (внецентренное сжатие, ф. 8.10–8.13) и в п. 8.1.19
// (внецентренное растяжение, ф. 8.24–8.25) такой оговорки нет, и буквальная подстановка Rsc·A's
// при x в несколько миллиметров занижает несущую способность примерно на 20 % против той же
// полосы при N = 0. В плите |N| практически никогда не равна нулю, поэтому решала именно эта
// заниженная ветвь.
//
// Принятая трактовка: при x ≤ 2a' условие считается второй раз при A's = 0, берётся большая
// несущая способность. Обе схемы статически допустимы (напряжение в сжатой арматуре от 0 до
// Rsc), поэтому большая из двух нижних оценок остаётся нижней оценкой.
//
// Сечение: h = 200 мм, привязка 40 мм, B25 (Rb = 14,5), A500 (Rs = 435, Rsc = 400).
public class ShellSimplCompressionRebarExclusionTests
{
    const double H = 0.2, APrime = 0.04, H0 = H - APrime, Arm = H0 - APrime;
    const double Rb = 14_500.0, Rs = 435_000.0, Rsc = 400_000.0;

    static MaterialChars Concrete() => new(CalcType.C)
    {
        Type = MatType.Concrete, E = 30_000_000.0, Fc = -Rb, Ft = 1_050.0,
    };

    static MaterialChars Rebar() => new(CalcType.C)
    {
        Type = MatType.ReSteelF, E = 200_000_000.0, Fc = -Rsc, Ft = Rs,
    };

    static ShellSimplStripResult Strip(double m, double n, double asT, double asC) =>
        ShellSimplSolver.ComputeStripUls(m, n, H, H0, APrime, asT, asC, Concrete(), Rebar());

    // КЭ 1654 плиты схемы 1-lin: Mx = 18,29, Nx = −3,6, подобранная арматура 2,66 см²/м при
    // минимальной 2,6 см²/м у сжатой грани.
    //   с A's:  x = (3,6 + 115,71 − 104,0)/14500 = 1,06 мм,
    //           M_ult = 15,31·(0,160 − 0,0005) + 104,0·0,12 = 14,92 → η = 18,51/14,92 = 1,24
    //   без A's: x = (3,6 + 115,71)/14500 = 8,23 мм,
    //           M_ult = 119,31·(0,160 − 0,0041) = 18,60 → η = 18,51/18,60 = 0,995
    [Fact]
    public void EccentricCompression_SmallX_UsesCapacityWithoutCompressionRebar()
    {
        const double m = 18.29, nC = 3.6, asT = 2.66e-4, asC = 2.6e-4;

        var r = Strip(m, -nC, asT, asC);

        double x = (nC + Rs * asT) / Rb;
        double mUlt = Rb * x * (H0 - 0.5 * x);
        double demand = nC * (m / nC + Arm / 2.0);

        Assert.Equal(x, r.Xm, 9);
        Assert.Equal(mUlt, r.M_ult, 6);
        Assert.Equal(demand / mUlt, r.Eta, 9);
        Assert.InRange(r.Eta, 0.99, 1.0);
        Assert.Contains("без A′s", r.Case);
    }

    // При N → 0 внецентренное сжатие обязано переходить в изгиб без скачка: там при x ≤ 2a'
    // сжатая арматура исключена по п. 8.1.13.
    [Fact]
    public void EccentricCompression_TendsToBendingAsForceVanishes()
    {
        const double m = 18.29, asT = 2.66e-4, asC = 2.6e-4;

        double bending = Strip(m, 0.0, asT, asC).Eta;
        double compression = Strip(m, -0.01, asT, asC).Eta;

        Assert.InRange(compression / bending, 0.995, 1.005);
    }

    // Когда сжатая зона больше 2a', сжатая арматура работает полностью — ф. (8.10) без изменений.
    [Fact]
    public void EccentricCompression_LargeX_KeepsCompressionRebar()
    {
        const double m = 60.0, nC = 1500.0, asT = 11.31e-4, asC = 11.31e-4;

        var r = Strip(m, -nC, asT, asC);

        // При привязке 40 мм и h = 200 мм x > 2a' означает ξ > ξR, то есть x — по (8.13).
        double x = r.Xm;
        Assert.True(x > 2.0 * APrime, $"x = {x:F4}");
        double mUlt = Rb * x * (H0 - 0.5 * x) + Rsc * asC * Arm;

        Assert.Equal(mUlt, r.M_ult, 6);
        Assert.DoesNotContain("без A′s", r.Case);
    }

    // Исключение сжатой арматуры не должно снижать несущую способность там, где (8.10) с A's
    // даёт больше: мощная сжатая арматура при x, близкой к 2a'.
    [Fact]
    public void EccentricCompression_NeverLowersCapacity()
    {
        foreach (double nC in new[] { 1.0, 50.0, 200.0, 400.0, 800.0 })
            foreach (double asC in new[] { 2.6e-4, 11.31e-4, 20e-4 })
            {
                const double asT = 11.31e-4;
                var r = Strip(40.0, -nC, asT, asC);

                double x = Math.Max(0.0, (nC + Rs * asT - Rsc * asC) / Rb);
                if (x / H0 > r.Xi_R) continue;      // ветвь (8.13) — вне предмета теста
                double withRebar = Rb * x * (H0 - 0.5 * x) + Rsc * asC * Arm;

                Assert.True(r.M_ult >= withRebar - 1e-9, $"N = −{nC}, A's = {asC}: {r.M_ult} < {withRebar}");
            }
    }

    // Внецентренное растяжение, п. 8.1.19б, сжатая зона есть, но x ≤ 2a'.
    // Верх 11,31 см²/м, низ 2,6 см²/м, M = 50, N = +50: e0 = 1,0 м.
    //   с A's:  x = (491,99 − 104,0 − 50)/14500 = 23,3 мм, M_ult = 50,14 + 12,48 = 62,62
    //   без A's: x = (491,99 − 50)/14500 = 30,5 мм,        M_ult = 441,99·(0,160 − 0,0152) = 63,98
    [Fact]
    public void EccentricTension_SmallX_UsesCapacityWithoutCompressionRebar()
    {
        const double m = 50.0, n = 50.0, asT = 11.31e-4, asC = 2.6e-4;

        var r = Strip(m, n, asT, asC);

        double x = (Rs * asT - n) / Rb;
        double mUlt = Rb * x * (H0 - 0.5 * x);
        double demand = n * (m / n - Arm / 2.0);

        Assert.Equal(mUlt, r.M_ult, 6);
        Assert.Equal(demand / mUlt, r.Eta, 9);
        Assert.Contains("без A′s", r.Case);
    }

    // Ф. (8.25) с Rsc·A's дала x ≤ 0, но без сжатой арматуры сжатая зона бетона есть. По образцу
    // п. 8.1.13 момент берётся относительно её равнодействующей: N·e″ ≤ Rs·As·(h0 − x/2).
    // Симметрично 11,31 см²/м, M = 40, N = +300:
    //   (8.25): x = (491,99 − 452,4 − 300)/14500 < 0 → (8.21): 300·(0,1333 + 0,06)/(491,99·0,12) = 0,982
    //   без A's: x = (491,99 − 300)/14500 = 13,2 мм,
    //            300·(0,1333 + 0,06 + 0,04 − 0,0066)/(491,99·(0,160 − 0,0066)) = 0,901
    [Fact]
    public void EccentricTension_NoCompressionZoneWithRebar_UsesConcreteResultant()
    {
        const double m = 40.0, n = 300.0, asT = 11.31e-4;

        var r = Strip(m, n, asT, asT);

        double x = (Rs * asT - n) / Rb;
        double mUlt = Rs * asT * (H0 - 0.5 * x);
        double demand = n * (m / n + Arm / 2.0 + APrime - 0.5 * x);
        double eta821 = n * (m / n + Arm / 2.0) / (Rs * asT * Arm);

        Assert.Equal(x, r.Xm, 9);
        Assert.Equal(demand / mUlt, r.Eta, 9);
        Assert.True(r.Eta < eta821);
        Assert.InRange(r.Eta, 0.89, 0.91);
    }

    // Та же запись тождественна условию (8.20) с M_ult по (8.24) при A's = 0: граница η = 1
    // у обеих — одна и та же. Момент подобран на эту границу.
    [Fact]
    public void EccentricTension_ConcreteResultantForm_EquivalentToCondition820WithoutRebar()
    {
        const double n = 300.0, asT = 11.31e-4;
        double x = (Rs * asT - n) / Rb;
        double mLimit = Rb * x * (H0 - 0.5 * x) + n * Arm / 2.0;   // N·e = M_ult при A's = 0

        Assert.Equal(1.0, Strip(mLimit, n, asT, asT).Eta, 9);
        Assert.True(Strip(mLimit * 0.99, n, asT, asT).Eta < 1.0);
        Assert.True(Strip(mLimit * 1.01, n, asT, asT).Eta > 1.0);
    }

    // N ≥ Rs·As: сжатой зоны нет и без сжатой арматуры — остаётся условие (8.21).
    [Fact]
    public void EccentricTension_ForceAboveTensionRebarCapacity_KeepsCondition821()
    {
        const double n = 600.0, asT = 11.31e-4;
        double m = n * 0.07;                       // e0 = 0,07 > arm/2 = 0,06

        var r = Strip(m, n, asT, asT);

        Assert.Equal(n * (0.07 + Arm / 2.0) / (Rs * asT * Arm), r.Eta, 9);
        Assert.Contains("8.21", r.Case);
    }
}
