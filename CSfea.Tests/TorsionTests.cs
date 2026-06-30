using CSfea.Torsion;
using CScore;

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

    public static void BoundaryFromMaterialArea()
    {
        TestHarness.Section("TorsionBoundary: из MaterialArea с отверстием");
        // Внешний контур 10×10 (CCW), отверстие 2×2 по центру (CW)
        var area = new MaterialArea();
        var hull = new Contour(
            new[] { new StressPoint(0.0, 0.0), new StressPoint(10.0, 0.0),
                    new StressPoint(10.0, 10.0), new StressPoint(0.0, 10.0) }, "hull");
        hull.Type = ContourType.Hull;
        area.Contours.Add(hull);
        var hole = new Contour(
            new[] { new StressPoint(4.0, 4.0), new StressPoint(6.0, 4.0),
                    new StressPoint(6.0, 6.0), new StressPoint(4.0, 6.0) }, "hole");
        hole.Type = ContourType.Hole;
        area.Contours.Add(hole);

        var b = area.FromMaterialArea();
        TestHarness.Check("Outer не null", b.OuterX != null && b.OuterX.Length == 4);
        TestHarness.Check("Holes есть", b.Holes != null && b.Holes.Count == 1);
        TestHarness.Check("Hole[0] размер", b.Holes![0].X.Length == 4);
    }
}

