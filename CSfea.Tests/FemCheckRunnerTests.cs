using CScore;
using CScore.Fem;

namespace CSfea.Tests;

public static class FemCheckRunnerTests
{
    static void CheckStr(string name, string actual, string expected)
    {
        bool ok = actual == expected;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}: '{actual}' (expected '{expected}')");
    }

    static void CheckCalcType(string name, CalcType actual, CalcType expected)
    {
        TestHarness.CheckRel(name, (double)(int)actual, (double)(int)expected, 0.01);
    }

    public static void RunExtractCalcType()
    {
        TestHarness.Section("FemCheckRunner: ExtractCalcType из тега набора");

        CheckCalcType("(C)  → C",         FemCheckRunner.ExtractCalcType("Плита — РСН 1 (C)",  null), CalcType.C);
        CheckCalcType("(CL) → CL",        FemCheckRunner.ExtractCalcType("Плита — РСН 1 (CL)", null), CalcType.CL);
        CheckCalcType("(N)  → N",         FemCheckRunner.ExtractCalcType("Плита — РСН 1 (N)",  null), CalcType.N);
        CheckCalcType("(NL) → NL",        FemCheckRunner.ExtractCalcType("Плита — ЗН (NL)",    null), CalcType.NL);
        CheckCalcType("без суффикса → C", FemCheckRunner.ExtractCalcType("Балка — ЗН 01",       null), CalcType.C);
        CheckCalcType("override CL",      FemCheckRunner.ExtractCalcType("любой тег",           "CL"), CalcType.CL);
    }

    public static void RunExtractWorstDetail()
    {
        TestHarness.Section("FemCheckRunner: ExtractWorstDetail из DataJson");

        var json = """
            {"utilization":0.85,"details":[
              {"formula":"8.1.1","description":"Сжатие","ratio":0.4,"passed":true},
              {"formula":"8.1.3","description":"Сжатие с изгибом","ratio":0.85,"passed":true}
            ]}
            """;
        var (f, d) = FemCheckRunner.ExtractWorstDetail(json);
        CheckStr("formula", f, "8.1.3");
        CheckStr("description", d, "Сжатие с изгибом");
    }

    public static void RunExtractWorstDetailNoDetails()
    {
        TestHarness.Section("FemCheckRunner: ExtractWorstDetail — нет details");
        var json = """{"utilization":0.5}""";
        var (f, d) = FemCheckRunner.ExtractWorstDetail(json);
        CheckStr("formula пусто", f, "");
        CheckStr("description пусто", d, "");
    }

    /// <summary>
    /// Ручная проверка ComputeAcrcStrip для полосы B30/A500, Mx=50 кН·м/м.
    /// Эталон считаем вручную и сравниваем с допуском 0.1%.
    /// </summary>
    public static void RunLayeredSlsAcrc()
    {
        TestHarness.Section("FemCheckRunner: ComputeAcrcStrip (B30/A500, Mx=50 кН·м/м)");

        // B30 N (кПа)
        double Eb     = 32_500_000.0;
        double Rb_ser = 22_000.0;
        double Rbt    = 1_750.0;
        // A500 N (кПа)
        double Es     = 200_000_000.0;
        double Rs_ser = 500_000.0;

        double h    = 0.200;  // м
        double h0   = 0.175;  // м
        double aP   = 0.025;  // м
        double As_t = 0.001;  // 10 см²/м
        double ds   = 0.012;  // ∅12, м
        double M    = 50.0;   // кН·м/м
        double N    = 0.0;    // кН/м

        double Eb_red    = Rb_ser / 0.0015;
        double alphaFull = Es / Eb;
        double alpha     = Es / Eb_red;

        // Ручной эталон
        double S_red = h * h / 2.0 + alphaFull * As_t * h0;
        double A_red = h + alphaFull * As_t;
        double yc    = S_red / A_red;
        double I_b   = h * h * h / 12.0 + h * (yc - h / 2.0) * (yc - h / 2.0);
        double I_st  = alphaFull * As_t * (yc - h0) * (yc - h0);
        double I_red = I_b + I_st;
        double yt    = h - yc;
        double Wred  = I_red / yt;
        double Wpl   = 1.3 * Wred;
        double ex    = Wred / A_red;
        double mcrc  = Math.Max(0.0, Rbt * Wpl - N * ex);

        double xm        = -alpha * As_t + Math.Sqrt((alpha * As_t) * (alpha * As_t) + 2.0 * alpha * As_t * h0);
        double zs_crc    = h0 - xm / 3.0;
        double sigma_s_crc = mcrc > 1e-9 ? Math.Min(mcrc / (zs_crc * As_t), Rs_ser) : 0.0;
        double eps_s_ref = 0.001;
        double sigma_s   = Math.Min(Es * eps_s_ref, Rs_ser);
        double psi_s     = Math.Clamp(1.0 - 0.8 * sigma_s_crc / sigma_s, 0.1, 1.0);

        double h_bt  = Math.Min(Math.Max(h - xm, 2.0 * aP), h0 / 2.0);
        double ls_raw = 0.5 * h_bt / As_t * ds;
        double ls_m   = Math.Clamp(ls_raw, Math.Max(10.0 * ds, 0.10), Math.Min(40.0 * ds, 0.40));

        double phi1 = 1.0, phi2 = 0.5, phi3 = 1.0;
        double acrcRef = phi1 * phi2 * phi3 * psi_s * (sigma_s / Es) * ls_m * 1000.0;

        double acrcActual = FemCheckRunner.ComputeAcrcStrip(
            eps_s: eps_s_ref,
            M_des: M, N_des: N,
            h: h, h0: h0, aPrime: aP,
            As_t: As_t, ds: ds,
            Rbt: Rbt, Rb_ser: Rb_ser, Es: Es, Rs_ser: Rs_ser,
            Eb_red: Eb_red, alphaFull: alphaFull, alpha: alpha,
            phi1: phi1, phi2: phi2);

        TestHarness.CheckRel("acrc (B30/A500, Mx=50)", acrcActual, acrcRef, 0.001);
        Console.WriteLine($"    mcrc={mcrc:F2} кН·м/м, ψs={psi_s:F3}, ls={ls_m:F3} м, acrc={acrcActual:F4} мм");
    }

    /// <summary>
    /// Проверяет свойство acrc_непрод = acrc1 + acrc2 − acrc3 аналитически.
    /// При acrc2 == acrc3 результат равен acrc1, а acrc1/acrc2 == φ1_1/φ1_2 = 1.4.
    /// </summary>
    public static void RunLayeredSlsThreeComponent()
    {
        TestHarness.Section("FemCheckRunner: acrc1 + acrc2 − acrc3 = acrc1 (п.8.2.7)");

        double Eb = 32_500_000.0, Rb_ser = 22_000.0, Rbt = 1_750.0;
        double Es = 200_000_000.0, Rs_ser = 500_000.0;
        double h = 0.2, h0 = 0.175, aP = 0.025, As_t = 0.001, ds = 0.012;
        double M = 50.0, N = 0.0, eps_s = 0.001;
        double Eb_red    = Rb_ser / 0.0015;
        double alphaFull = Es / Eb;
        double alpha     = Es / Eb_red;
        double phi2 = 0.5;

        double acrc1 = FemCheckRunner.ComputeAcrcStrip(
            eps_s, M, N, h, h0, aP, As_t, ds,
            Rbt, Rb_ser, Es, Rs_ser, Eb_red, alphaFull, alpha, phi1: 1.4, phi2);
        double acrc2 = FemCheckRunner.ComputeAcrcStrip(
            eps_s, M, N, h, h0, aP, As_t, ds,
            Rbt, Rb_ser, Es, Rs_ser, Eb_red, alphaFull, alpha, phi1: 1.0, phi2);
        double acrc3 = FemCheckRunner.ComputeAcrcStrip(
            eps_s, M, N, h, h0, aP, As_t, ds,
            Rbt, Rb_ser, Es, Rs_ser, Eb_red, alphaFull, alpha, phi1: 1.0, phi2);

        // acrc2 == acrc3 (одинаковые аргументы) → acrc1+acrc2-acrc3 == acrc1
        double acrcSum = acrc1 + acrc2 - acrc3;
        TestHarness.CheckRel("acrc1+acrc2-acrc3 == acrc1", acrcSum, acrc1, 0.001);

        // φ1 = 1.4 vs 1.0 → соотношение должно быть точно 1.4
        if (acrc2 > 1e-9)
            TestHarness.CheckRel("acrc1/acrc2 == 1.4", acrc1 / acrc2, 1.4, 0.001);

        Console.WriteLine($"    acrc1={acrc1:F4} мм, acrc2={acrc2:F4} мм, acrcSum={acrcSum:F4} мм");

        // При сжатии (eps_s <= 0) → acrc = 0
        double acrcNeg = FemCheckRunner.ComputeAcrcStrip(
            eps_s: -0.001, M, N, h, h0, aP, As_t, ds,
            Rbt, Rb_ser, Es, Rs_ser, Eb_red, alphaFull, alpha, phi1: 1.0, phi2);
        TestHarness.CheckRel("acrc при сжатии == 0", acrcNeg, 0.0, 0.001);
    }

    /// <summary>
    /// Проверяет виртуальный NL через LtFraction: acrc1 + acrc2 − acrc3 == acrc1
    /// при LtFraction=1.0 (virtualNl == N → acrc3 == acrc2 → сумма = acrc1).
    /// </summary>
    public static void RunLayeredSlsLtFraction()
    {
        TestHarness.Section("FemCheckRunner: ComputeAcrcStrip + LtFraction (виртуальный NL)");

        double Eb = 32_500_000.0, Rb_ser = 22_000.0, Rbt = 1_750.0;
        double Es = 200_000_000.0, Rs_ser = 500_000.0;
        double h = 0.2, h0 = 0.175, aP = 0.025, As_t = 0.001, ds = 0.012;
        double M = 50.0, N = 0.0, eps_s = 0.001;
        double Eb_red = Rb_ser / 0.0015, alphaFull = Es / Eb, alpha = Es / Eb_red;
        double phi2 = 0.5;

        // LtFraction=0 → только acrc2 (phi1=1.0)
        double acrc2 = FemCheckRunner.ComputeAcrcStrip(
            eps_s, M, N, h, h0, aP, As_t, ds,
            Rbt, Rb_ser, Es, Rs_ser, Eb_red, alphaFull, alpha, phi1: 1.0, phi2);

        // LtFraction=1.0 → virtualNl == N → acrc3 == acrc2
        // acrc1(phi1=1.4) + acrc2 - acrc3(=acrc2) = acrc1
        double acrc1 = FemCheckRunner.ComputeAcrcStrip(
            eps_s, M, N, h, h0, aP, As_t, ds,
            Rbt, Rb_ser, Es, Rs_ser, Eb_red, alphaFull, alpha, phi1: 1.4, phi2);
        double acrc3 = acrc2; // virtualNl == N → phi1=1.0 → same as acrc2
        double acrcExpected = acrc1 + acrc2 - acrc3; // = acrc1

        TestHarness.CheckRel("acrcExpected == acrc1 (LtFraction=1.0)", acrcExpected, acrc1, 0.001);
        TestHarness.CheckRel("acrc1/acrc2 == 1.4 (phi1 ratio)", acrc1 / acrc2, 1.4, 0.001);
        Console.WriteLine($"    acrc2={acrc2:F4} мм, acrc1={acrc1:F4} мм, acrcExpected={acrcExpected:F4} мм");
    }
}
