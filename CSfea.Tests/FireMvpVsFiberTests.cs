using CScore.Fire;

namespace CSfea.Tests;

/// <summary>
/// MVP — диагностический, ненормативный путь: он не исключает растянутый бетон
/// по п. 8.42 и сворачивает температуру к одному коэффициенту. Тест фиксирует
/// расхождение с фибровым методом как ожидаемое, а не как регрессию.
/// </summary>
public static class FireMvpVsFiberTests
{
    public static void RunAll()
    {
        TestHarness.Section("FireMvp: диагностический путь");
        Mvp_IsMarkedNonNormative();
        Fiber_IsNotMarkedNonNormative();
        Results_MayDifferAndThatIsExpected();
        NormEdition_IsStamped();
    }

    static void Mvp_IsMarkedNonNormative()
    {
        var (section, thermal) = FireRCheckTests.BuildFixtureForTests();
        var check = FireRCheckMvp.Run(thermal, section, n: -0.5, mx: 0, my: 0, snapshotIndex: 1);

        bool flagged = check.Details.TryGetValue("non_normative", out object? v)
                    && v is bool b && b;
        TestHarness.Check("FireMvp_NonNormativeFlag", flagged);
    }

    static void Fiber_IsNotMarkedNonNormative()
    {
        var (section, thermal) = FireRCheckTests.BuildFixtureForTests();
        var check = FireRCheckFiber.Run(thermal, section, n: -0.5, mx: 0, my: 0, snapshotIndex: 1);

        bool flagged = check.Details.TryGetValue("non_normative", out object? v)
                    && v is bool b && b;
        TestHarness.Check("FireFiber_NotNonNormative", !flagged);
    }

    static void Results_MayDifferAndThatIsExpected()
    {
        var (section, thermal) = FireRCheckTests.BuildFixtureForTests();
        var mvp = FireRCheckMvp.Run(thermal, section, n: -0.5, mx: 0, my: 0, snapshotIndex: 1);
        var fiber = FireRCheckFiber.Run(thermal, section, n: -0.5, mx: 0, my: 0, snapshotIndex: 1);

        TestHarness.Check("FireMvpVsFiber_BothFinite",
            double.IsFinite(mvp.Margin) && double.IsFinite(fiber.Margin),
            $"mvp={mvp.Margin:F4}, fiber={fiber.Margin:F4}");
    }

    static void NormEdition_IsStamped()
    {
        var (section, thermal) = FireRCheckTests.BuildFixtureForTests();
        var check = FireRCheckFiber.Run(thermal, section, n: -0.5, mx: 0, my: 0, snapshotIndex: 0);

        bool ok = check.Details.TryGetValue("norm_edition", out object? v)
               && (v as string) == "SP468-2019/izm1";
        TestHarness.Check("FireResult_NormEditionStamped", ok);
    }
}
