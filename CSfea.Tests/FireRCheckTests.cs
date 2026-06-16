using CScore;
using CScore.Fire;
using CSfea.Thermal;

namespace CSfea.Tests;

/// <summary>Тесты R-проверки огнестойкости (MVP и fiber).</summary>
public static class FireRCheckTests
{
    public static void RunAll()
    {
        TestHarness.Section("FireRCheck: fiber и MVP");
        Fiber_ReturnsFiniteFactor();
        Mvp_ReturnsFiniteFactor();
        Fiber_HotSnapshotLowerCapacityThanCold();
    }

    private static void Fiber_ReturnsFiniteFactor()
    {
        var (section, thermal) = BuildFixture();
        var check = FireRCheckFiber.Run(thermal, section, n: -0.5, mx: 0, my: 0, snapshotIndex: 0);
        bool ok = double.IsFinite(check.Margin) && check.Details.ContainsKey("factor");
        TestHarness.Check("FireRCheckFiber_FiniteFactor", ok,
            $"passed={check.Passed}, margin={check.Margin:F4}, factor={check.Details["factor"]}");
    }

    private static void Mvp_ReturnsFiniteFactor()
    {
        var (section, thermal) = BuildFixture();
        var check = FireRCheckMvp.Run(thermal, section, n: -0.5, mx: 0, my: 0, snapshotIndex: 0);
        bool ok = double.IsFinite(check.Margin) && check.Details.ContainsKey("gamma_bt");
        TestHarness.Check("FireRCheckMvp_FiniteFactor", ok,
            $"passed={check.Passed}, gamma_bt={check.Details["gamma_bt"]}");
    }

    private static void Fiber_HotSnapshotLowerCapacityThanCold()
    {
        var (section, thermal) = BuildFixture();
        var cold = FireRCheckFiber.Run(thermal, section, n: -0.5, mx: 0, my: 0, snapshotIndex: 0);
        var hot = FireRCheckFiber.Run(thermal, section, n: -0.5, mx: 0, my: 0, snapshotIndex: 1);
        double kCold = Convert.ToDouble(cold.Details["factor"]);
        double kHot = Convert.ToDouble(hot.Details["factor"]);
        bool ok = kHot <= kCold + 1e-6;
        TestHarness.Check("FireRCheckFiber_HotLowerCapacity", ok,
            $"k20={kCold:F4}, k400={kHot:F4}");
    }

    private static (CrossSection Section, FireThermalResult Thermal) BuildFixture()
    {
        CrossSection section = FireFiberSectionTests.CreateSectionForTests();
        FireThermalResult thermal = CreateThermalResult();
        return (section, thermal);
    }

    private static FireThermalResult CreateThermalResult()
    {
        var mesh = new HeatMesh(
            x: [0.0, 1.0, 0.0],
            y: [0.0, 0.0, 1.0],
            elements: [[0, 1, 2]]);

        var meshInfo = new FireMeshBuildResult
        {
            Mesh = mesh,
            BoundaryEdges = [],
            Rebars =
            [
                new FireRebarLocation
                {
                    Id = 0,
                    X = 0.2,
                    Y = 0.2,
                    ElementIndex = 0,
                    Xi1 = 0.6,
                    Xi2 = 0.2,
                    Xi3 = 0.2
                }
            ]
        };

        return new FireThermalResult
        {
            MeshInfo = meshInfo,
            TimesMin = [0.0, 60.0],
            Snapshots =
            [
                [20.0, 20.0, 20.0],
                [400.0, 400.0, 400.0]
            ],
            RebarTemperatureHistory = new Dictionary<int, double[]>
            {
                [0] = [20.0, 400.0]
            },
            RebarMaxTemperatures = new Dictionary<int, double> { [0] = 400.0 },
            AggregateType = "silicate"
        };
    }
}
