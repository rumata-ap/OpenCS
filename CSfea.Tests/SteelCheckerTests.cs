using CScore;

namespace CSfea.Tests;

public static class SteelCheckerTests
{
    public static void RunSimpleCompressionCheck()
    {
        TestHarness.Section("SteelChecker: Центральное сжатие двутавра 30Б1");

        // Двутавр 30Б1
        var section = SteelSection.FromIBeam(0.300, 0.126, 0.0064, 0.0108);
        // Сталь С245: fy = 245e6 Па
        section.Steel.materialChars.Add(new MaterialChars(CalcType.C)
        {
            Ry = 245e6,
            Ru = 360e6
        });

        // Усилие: N = -500 кН (сжатие)
        var forces = new InternalForces
        {
            LoadCaseName = "НК1",
            N = -500e3
        };

        // Контекст: L0x = L0y = 3 м
        var context = new DesignContext
        {
            DesignLengthX = 3.0,
            DesignLengthY = 3.0,
            GammaM = 1.025
        };

        var result = SteelChecker.Run(section, forces, context);

        TestHarness.CheckRel("Utilization", result.Utilization, 0.5, 0.30);
        System.Console.WriteLine($"  Result: {(result.IsPassed ? "PASSED" : "FAILED")}");
        System.Console.WriteLine($"  Utilization: {result.Utilization:F3}");
        System.Console.WriteLine($"  Details: {result.Details.Count} checks");
        foreach (var d in result.Details)
        {
            System.Console.WriteLine($"    [{(d.Passed ? "PASS" : "FAIL")}] {d.Formula}: {d.Description} = {d.Applied:F1} / {d.Allowable:F1} = {d.Ratio:F3}");
        }
    }
}
