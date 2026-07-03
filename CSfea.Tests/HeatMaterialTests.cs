namespace CSfea.Tests;

/// <summary>Тесты теплофизических материалов CSfea.Thermal.</summary>
public static class HeatMaterialTests
{
    public static void RunAll()
    {
        TestHarness.Section("HeatMaterial: ConstantHeatMaterial");
        var m = new CSfea.Thermal.Materials.ConstantHeatMaterial(lambda: 1.6, rhocp: 2.4e6);
        TestHarness.Check("Conductivity@20", m.Conductivity(20) == 1.6, $"λ={m.Conductivity(20)}");
        TestHarness.Check("Conductivity@500", m.Conductivity(500) == 1.6);
        TestHarness.Check("ρc@20", m.VolumetricHeatCapacity(20) == 2.4e6);
    }
}
