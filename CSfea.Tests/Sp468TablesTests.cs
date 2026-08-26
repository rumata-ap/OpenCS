using CScore.Fire;

namespace CSfea.Tests;

/// <summary>Проверка табличных данных СП 468 (с Изм. № 1): 5.1, 5.3, 5.5, 5.6, 5.7.</summary>
public static class Sp468TablesTests
{
    public static void RunAll()
    {
        TestHarness.Section("Sp468Tables: нормативные таблицы");
        GammaBt_NodeValues();
        GammaBt_SilicateIsZeroAt800();
        GammaBt_LightweightHasOwnRow();
        GammaSt_NodeValues();
        GammaStE_NodeValues();
        GammaSt_NonMonotoneRowReproduced();
        Interpolation_IsLinearBetweenNodes();
        OutOfRange_ClampsToEdgeValues();
        AlphaTables_NodeValues();
        EpsB2_NodeValuesAndRangeFlag();
    }

    static void GammaBt_NodeValues()
    {
        double[] t = [20, 200, 300, 400, 500, 600, 700, 800];
        double[] silicate = [1.0, 0.98, 0.95, 0.85, 0.80, 0.60, 0.20, 0.0];
        double[] carbonate = [1.0, 1.0, 0.95, 0.90, 0.85, 0.65, 0.30, 0.15];

        bool okS = true, okC = true;
        for (int i = 0; i < t.Length; i++)
        {
            okS &= Math.Abs(Sp468Tables.GammaBt("silicate", t[i]) - silicate[i]) < 1e-9;
            okC &= Math.Abs(Sp468Tables.GammaBt("carbonate", t[i]) - carbonate[i]) < 1e-9;
        }

        TestHarness.Check("Sp468Tables_GammaBt_Silicate_Nodes", okS);
        TestHarness.Check("Sp468Tables_GammaBt_Carbonate_Nodes", okC);
    }

    static void GammaBt_SilicateIsZeroAt800()
        => TestHarness.Check("Sp468Tables_GammaBt_SilicateZeroAt800",
            Sp468Tables.GammaBt("silicate", 800.0) == 0.0);

    static void GammaBt_LightweightHasOwnRow()
    {
        double light = Sp468Tables.GammaBt("lightweight", 600.0);
        double silicate = Sp468Tables.GammaBt("silicate", 600.0);
        TestHarness.Check("Sp468Tables_GammaBt_LightweightDiffers",
            Math.Abs(light - 0.70) < 1e-9 && Math.Abs(light - silicate) > 1e-6,
            $"lightweight={light:F2}, silicate={silicate:F2}");
    }

    static void GammaSt_NodeValues()
    {
        double[] t = [20, 200, 300, 400, 500, 600, 700, 800];
        double[] a240 = [1.0, 1.0, 1.0, 0.85, 0.60, 0.37, 0.22, 0.10];
        double[] wire = [1.0, 1.0, 0.90, 0.65, 0.35, 0.15, 0.05, 0.02];

        bool okA = true, okW = true;
        for (int i = 0; i < t.Length; i++)
        {
            okA &= Math.Abs(Sp468Tables.GammaSt(FireRebarClass.A240A500, t[i]) - a240[i]) < 1e-9;
            okW &= Math.Abs(Sp468Tables.GammaSt(FireRebarClass.WireRope, t[i]) - wire[i]) < 1e-9;
        }

        TestHarness.Check("Sp468Tables_GammaSt_A240A500_Nodes", okA);
        TestHarness.Check("Sp468Tables_GammaSt_WireRope_Nodes", okW);
    }

    static void GammaStE_NodeValues()
    {
        double[] t = [20, 200, 300, 400, 500, 600, 700, 800];
        double[] a240e = [1.0, 0.92, 0.90, 0.85, 0.80, 0.77, 0.72, 0.65];

        bool ok = true;
        for (int i = 0; i < t.Length; i++)
            ok &= Math.Abs(Sp468Tables.GammaStE(FireRebarClass.A240A500, t[i]) - a240e[i]) < 1e-9;

        TestHarness.Check("Sp468Tables_GammaStE_A240A500_Nodes", ok);
    }

    static void GammaSt_NonMonotoneRowReproduced()
    {
        // А600С марки 18Г2СФ: 0,76 при 400 °C и 0,82 при 500 °C — так в норме.
        double at400 = Sp468Tables.GammaSt(FireRebarClass.A600C18G2SF, 400.0);
        double at500 = Sp468Tables.GammaSt(FireRebarClass.A600C18G2SF, 500.0);
        TestHarness.Check("Sp468Tables_GammaSt_NonMonotonePreserved",
            Math.Abs(at400 - 0.76) < 1e-9 && Math.Abs(at500 - 0.82) < 1e-9 && at500 > at400,
            $"400C={at400:F2}, 500C={at500:F2}");
    }

    static void Interpolation_IsLinearBetweenNodes()
    {
        // Силикатный: 0,80 при 500 и 0,60 при 600 -> 0,70 при 550.
        double mid = Sp468Tables.GammaBt("silicate", 550.0);
        TestHarness.Check("Sp468Tables_Interp_Linear", Math.Abs(mid - 0.70) < 1e-9, $"value={mid:F4}");
    }

    static void OutOfRange_ClampsToEdgeValues()
    {
        bool low = Math.Abs(Sp468Tables.GammaBt("silicate", -50.0) - 1.0) < 1e-9;
        bool high = Sp468Tables.GammaBt("silicate", 1500.0) == 0.0;
        bool unknown = Math.Abs(Sp468Tables.GammaBt("unknown", 500.0)
                              - Sp468Tables.GammaBt("silicate", 500.0)) < 1e-9;
        TestHarness.Check("Sp468Tables_Clamp_Low", low);
        TestHarness.Check("Sp468Tables_Clamp_High", high);
        TestHarness.Check("Sp468Tables_UnknownAggregateFallsBackToSilicate", unknown);
    }

    static void AlphaTables_NodeValues()
    {
        bool bt = Math.Abs(Sp468Tables.AlphaBt("silicate", 300.0) - 8e-6) < 1e-12
               && Math.Abs(Sp468Tables.AlphaBt("carbonate", 500.0) - 12e-6) < 1e-12
               && Math.Abs(Sp468Tables.AlphaBt("lightweight", 500.0) - 5.5e-6) < 1e-12;
        bool st = Math.Abs(Sp468Tables.AlphaSt(20.0) - 11.5e-6) < 1e-12
               && Math.Abs(Sp468Tables.AlphaSt(800.0) - 15.5e-6) < 1e-12;
        TestHarness.Check("Sp468Tables_AlphaBt_Nodes", bt);
        TestHarness.Check("Sp468Tables_AlphaSt_Nodes", st);
    }

    static void EpsB2_NodeValuesAndRangeFlag()
    {
        double at20 = Sp468Tables.EpsB2Silicate(20.0, out bool r20);
        double at500 = Sp468Tables.EpsB2Silicate(500.0, out bool r500);
        double at700 = Sp468Tables.EpsB2Silicate(700.0, out bool r700);

        TestHarness.Check("Sp468Tables_EpsB2_Nodes",
            Math.Abs(at20 - 0.0035) < 1e-12 && Math.Abs(at500 - 0.0158) < 1e-12 && !r20 && !r500,
            $"20C={at20:F4}, 500C={at500:F4}");
        TestHarness.Check("Sp468Tables_EpsB2_OutOfRangeFlagged",
            r700 && Math.Abs(at700 - 0.0158) < 1e-12,
            $"700C={at700:F4}, outOfRange={r700}");
    }
}
