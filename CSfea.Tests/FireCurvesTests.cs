using CScore.Fire;

namespace CSfea.Tests;

/// <summary>Тесты стандартных огневых кривых (ГОСТ 30247.0 / ISO 834).</summary>
public static class FireCurvesTests
{
    public static void RunAll()
    {
        TestHarness.Section("FireCurves: ISO 834");
        RunIso834();

        TestHarness.Section("FireCurves: углеводородная кривая");
        RunHydrocarbon();

        TestHarness.Section("FireCurves: медленный нагрев");
        RunSlowHeat();

        TestHarness.Section("FireCurves: Get(name)");
        RunGet();
    }

    static void RunIso834()
    {
        CheckAbs("T(0) = 20°C", FireCurves.Iso834(0.0), 20.0, 0.5);
        CheckAbs("T(30 мин) ≈ 842°C", FireCurves.Iso834(30 * 60.0), 842.0, 2.0);
        CheckAbs("T(60 мин) ≈ 945°C", FireCurves.Iso834(60 * 60.0), 945.0, 2.0);
        CheckAbs("T(120 мин) ≈ 1049°C", FireCurves.Iso834(120 * 60.0), 1049.0, 3.0);

        bool monotonic = true;
        string detail = "";
        for (int i = 0; i < 4 * 60; i += 5)
        {
            double t0 = i * 60.0;
            double t1 = (i + 5) * 60.0;
            double a = FireCurves.Iso834(t0);
            double b = FireCurves.Iso834(t1);
            if (b < a)
            {
                monotonic = false;
                detail = $"T должна возрастать, получено {a:F1} → {b:F1}";
                break;
            }
        }
        TestHarness.Check("монотонный рост на [0, 4 ч]", monotonic, detail);
    }

    static void RunHydrocarbon()
    {
        CheckAbs("T(0) = 20°C", FireCurves.Hydrocarbon(0.0), 20.0, 0.5);
        CheckAbs("T(30 мин) ≈ 1098°C", FireCurves.Hydrocarbon(30 * 60.0), 1098.0, 5.0);
    }

    static void RunSlowHeat()
    {
        CheckAbs("T(0) = 20°C", FireCurves.SlowHeat(0.0), 20.0, 0.5);
        TestHarness.Check(
            "T(30 мин) < ISO 834",
            FireCurves.SlowHeat(30 * 60.0) < FireCurves.Iso834(30 * 60.0),
            $"slow={FireCurves.SlowHeat(30 * 60.0):F1}, iso={FireCurves.Iso834(30 * 60.0):F1}");
    }

    static void RunGet()
    {
        var iso = FireCurves.Get("iso834");
        CheckAbs("Get(\"iso834\")(60 мин)", iso(60 * 60.0), 945.0, 2.0);

        var hydro = FireCurves.Get("hydrocarbon");
        CheckAbs("Get(\"hydrocarbon\")(30 мин)", hydro(30 * 60.0), 1098.0, 5.0);

        var slow = FireCurves.Get("slow");
        TestHarness.Check(
            "Get(\"slow\")(30 мин) < ISO 834",
            slow(30 * 60.0) < FireCurves.Iso834(30 * 60.0));

        bool threw = false;
        try
        {
            FireCurves.Get("unknown");
        }
        catch (ArgumentException)
        {
            threw = true;
        }
        TestHarness.Check("неизвестное имя → ArgumentException", threw);
    }

    static void CheckAbs(string name, double value, double reference, double absTol)
    {
        bool ok = Math.Abs(value - reference) <= absTol;
        TestHarness.Check(name, ok, $"value={value:F2}, ref={reference:F2}, tol={absTol:F2}");
    }
}
