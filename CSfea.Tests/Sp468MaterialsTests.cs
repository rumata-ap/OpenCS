using CScore.Fire;

namespace CSfea.Tests;

/// <summary>Тесты материалов СП 468: бетон и коэффициенты γ.</summary>
public static class Sp468MaterialsTests
{
    public static void RunAll()
    {
        TestHarness.Section("SP468 Materials: бетон");
        RunConcreteChecks();

        TestHarness.Section("SP468 Materials: коэффициенты gamma");
        RunGammaChecks();
    }

    private static void RunConcreteChecks()
    {
        var material = new Sp468ConcreteHeatMaterial("silicate", 0.025);

        CheckAbs("lambda silicate @20C", material.Conductivity(20.0), 1.60, 1e-9);
        CheckAbs("lambda silicate @400C", material.Conductivity(400.0), 1.10, 1e-9);

        double c20 = material.SpecificHeat(20.0);
        double c100 = material.SpecificHeat(100.0);
        TestHarness.Check("specific heat peak @100C", c100 > c20, $"c20={c20:F2}, c100={c100:F2}");

        CheckAbs("rho silicate @20C", material.Density(20.0), 2400.0, 1e-9);

        double rho = material.Density(400.0);
        double cp = material.SpecificHeat(400.0);
        double rhocp = material.VolumetricHeatCapacity(400.0);
        CheckAbs("rhocp = rho * c", rhocp, rho * cp, 1e-6);
    }

    private static void RunGammaChecks()
    {
        CheckAbs("GammaBt silicate @400C", FireMaterials.GammaBt("B25", "silicate", 400.0), 0.75, 1e-9);
        CheckAbs(
            "GammaSt compression @400C",
            FireMaterials.GammaSt("A500", 400.0, "compression"),
            0.85,
            1e-9);
    }

    private static void CheckAbs(string name, double value, double reference, double absTol)
    {
        bool ok = Math.Abs(value - reference) <= absTol;
        TestHarness.Check(name, ok, $"value={value:F6}, ref={reference:F6}, tol={absTol:E2}");
    }
}
