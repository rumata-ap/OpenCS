using CSfea.Torsion;

namespace CSfea.Tests;

public static class TorsionTests
{
    public static void SmokePropsConstruction()
    {
        TestHarness.Section("TorsionProps: конструктор и TauMax");
        var props = new TorsionProps { It = 1.5, TauUnitMax = 2.0 };
        TestHarness.CheckRel("It", props.It, 1.5, 1e-9);
        // τ_max = G·Θ·τ_unit = 1000·0.01·2.0 = 20
        TestHarness.CheckRel("TauMax", props.TauMax(1000.0, 0.01), 20.0, 1e-9);
    }
}
