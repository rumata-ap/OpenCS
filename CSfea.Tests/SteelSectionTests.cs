using CScore;

namespace CSfea.Tests;

public static class SteelSectionTests
{
    public static void RunIBeamProperties()
    {
        TestHarness.Section("SteelSection: Двутавр — геометрические свойства");

        // Двутавр 30Б1: h=300мм, b=126мм, tw=6.4мм, tf=10.8мм
        double h = 0.300, b = 0.126, tw = 0.0064, tf = 0.0108;
        var section = SteelSection.FromIBeam(h, b, tw, tf);

        // Отладка: выводим координаты контура
        Console.WriteLine($"  OuterContour points: {section.OuterContour.Count}");
        Console.WriteLine($"  GeoProps A: {section.Geo.A}");
        Console.WriteLine($"  GeoProps Ix: {section.Geo.Ix}");
        Console.WriteLine($"  GeoProps Iy: {section.Geo.Iy}");

        // Упрощённый двутавр (без скруглений r1, r2):
        // Area = 2*b*tf + (h-2*tf)*tw = 45.03 см²
        double expectedArea = 45.03e-4;
        TestHarness.CheckRel("Area", section.Area, expectedArea, 0.02);

        // Ix = (b*h³ - (b-tw)*(h-2*tf)³)/12 = 6844 см⁴
        double expectedIx = 6844e-8;
        TestHarness.CheckRel("Ix", section.Ix, expectedIx, 0.02);

        // Iy = (2*tf*b³ + (h-2*tf)*tw³)/12 = 360.7 см⁴
        double expectedIy = 360.7e-8;
        TestHarness.CheckRel("Iy", section.Iy, expectedIy, 0.02);

        // Центр тяжести ≈ (0, 0) для симметричного двутавра
        TestHarness.CheckRel("Centroid.X", section.Centroid.X, 0, 1e-6);
        TestHarness.CheckRel("Centroid.Y", section.Centroid.Y, 0, 1e-6);
    }

    public static void RunPlasticModulusRectangle()
    {
        TestHarness.Section("SteelPlasticSection: прямоугольник — Wpl=b·h²/4, ПНО на центроиде");
        double b = 0.2, h = 0.4;
        var section = new SteelSection
        {
            OuterContour =
            [
                (-b / 2, -h / 2), (b / 2, -h / 2), (b / 2, h / 2), (-b / 2, h / 2)
            ]
        };
        var r = SteelPlasticSection.Compute(section);
        double expectedWpl = b * h * h / 4.0;
        TestHarness.CheckRel("WplX = b·h²/4", r.WplX, expectedWpl, 1e-6);
        TestHarness.CheckRel("WplY = h·b²/4", r.WplY, h * b * b / 4.0, 1e-6);
        TestHarness.Check("YPna ≈ 0 (центроид, симметрия)", Math.Abs(r.YPna) < 1e-6, $"YPna={r.YPna:E3}");
        TestHarness.Check("XPna ≈ 0 (центроид, симметрия)", Math.Abs(r.XPna) < 1e-6, $"XPna={r.XPna:E3}");
        // Форм-фактор для прямоугольника ровно 1.5 (Wpl/Wel = (b h²/4)/(b h²/6) = 1.5).
        double wel = b * h * h / 6.0;
        TestHarness.CheckRel("Форм-фактор Wpl/Wel = 1.5", r.WplX / wel, 1.5, 1e-6);
    }

    public static void RunPlasticModulusIBeam()
    {
        TestHarness.Section("SteelPlasticSection: двутавр — сверка с ручной формулой Wpl = b·tf·(h−tf) + tw·hw²/4");
        double h = 0.300, b = 0.126, tw = 0.0064, tf = 0.0108;
        var section = SteelSection.FromIBeam(h, b, tw, tf);
        var r = SteelPlasticSection.Compute(section);

        double hw = h - 2 * tf;
        double expectedWplX = b * tf * (h - tf) + tw * hw * hw / 4.0;
        TestHarness.CheckRel("WplX (двутавр)", r.WplX, expectedWplX, 1e-3);
        TestHarness.Check("YPna ≈ 0 (симметрия сечения)", Math.Abs(r.YPna) < 1e-6, $"YPna={r.YPna:E3}");
        TestHarness.Check("WplX > Wel,x (форм-фактор > 1)", r.WplX > section.Wx);
    }

    /// <summary>
    /// Прямая проверка GeoProps на простом прямоугольнике.
    /// </summary>
    public static void RunGeoPropsDirect()
    {
        TestHarness.Section("GeoProps: Прямоугольник 0.1×0.2");

        // Прямоугольник 100×200 мм (5 точек: 4 вершины + замыкающая)
        var outerX = new List<double> { -0.05, 0.05, 0.05, -0.05, -0.05 };
        var outerY = new List<double> { -0.10, -0.10, 0.10, 0.10, -0.10 };
        var contour = new Contour(outerX, outerY, "rect");
        var gp = new GeoProps(contour);

        double expectedArea = 0.1 * 0.2; // 0.02 м²
        double expectedIx = 0.1 * 0.2 * 0.2 * 0.2 / 12.0; // bh³/12
        double expectedIy = 0.2 * 0.1 * 0.1 * 0.1 / 12.0; // hb³/12

        Console.WriteLine($"  GeoProps A: {gp.A} (expected {expectedArea})");
        Console.WriteLine($"  GeoProps Ix: {gp.Ix} (expected {expectedIx})");
        Console.WriteLine($"  GeoProps Iy: {gp.Iy} (expected {expectedIy})");

        TestHarness.CheckRel("Area", gp.A, expectedArea, 0.01);
        TestHarness.CheckRel("Ix", gp.Ix, expectedIx, 0.01);
        TestHarness.CheckRel("Iy", gp.Iy, expectedIy, 0.01);
    }
}
