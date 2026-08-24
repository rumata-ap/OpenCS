using CScore;

namespace OpenCS.Tests;

/// <summary>Общие сечения для тестов задачи расчёта наклонных сечений.</summary>
internal static class ShearInclinedFixtures
{
    /// <summary>Балка 300 × 600 из контрольного примера: B25, ⌀8 A240 шагом 150 мм.</summary>
    public static CrossSection Beam()
    {
        var concrete = new Material
        {
            Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0
        };
        concrete.C = new MaterialChars(CalcType.C)
        {
            Type = MatType.Concrete,
            Fc = -14_500.0,          // сжатие — «минус»
            Ft = 1_050.0,
            E = 30_000_000.0,
            Ec1Red = -0.0015,
            Ec2 = -0.0035,
            Et1Red = 0.00008,
            Et2 = 0.00015
        };

        var stirrupSteel = new Material
        {
            Id = 2, Tag = "A240", Type = MatType.ReSteelF, E = 200_000_000.0
        };
        stirrupSteel.C = new MaterialChars(CalcType.C)
        {
            Type = MatType.ReSteelF, Fc = -215_000.0, Ft = 215_000.0, E = 200_000_000.0
        };

        var rebarSteel = new Material
        {
            Id = 3, Tag = "A500", Type = MatType.ReSteelU, E = 200_000_000.0
        };
        rebarSteel.C = new MaterialChars(CalcType.C)
        {
            Type = MatType.ReSteelU, Fc = -435_000.0, Ft = 435_000.0, E = 200_000_000.0
        };

        var region = new MaterialArea
        {
            Category = AreaCategory.Region,
            Material = concrete,
            MaterialId = concrete.Id
        };
        region.Contours.Add(new Contour(
            [-0.15, 0.15, 0.15, -0.15, -0.15],
            [-0.30, -0.30, 0.30, 0.30, -0.30], "hull") { Type = ContourType.Hull });
        region.SetWKT();
        region.ClosedStirrups.Add(new ClosedStirrupGroup
        {
            MaterialId = stirrupSteel.Id,
            SpacingM = 0.15,
            Loops =
            [
                new ClosedStirrupLoop
                {
                    CenterlineContour = new Contour(
                        [-0.12, 0.12, 0.12, -0.12, -0.12],
                        [-0.27, -0.27, 0.27, 0.27, -0.27], "stirrup"),
                    BarAreaM2 = 0.0000503,
                    BarDiameterM = 0.008
                }
            ]
        });

        var rebar = new MaterialArea
        {
            Category = AreaCategory.RebarGroup,
            Material = rebarSteel,
            MaterialId = rebarSteel.Id
        };
        rebar.Fibers.Add(new Fiber { TypeFiber = FiberType.point, X = -0.08, Y = -0.25, Area = 0.000616 });
        rebar.Fibers.Add(new Fiber { TypeFiber = FiberType.point, X = 0.08, Y = -0.25, Area = 0.000616 });

        var stirrupMaterialCarrier = new MaterialArea
        {
            Category = AreaCategory.RebarGroup,
            Material = stirrupSteel,
            MaterialId = stirrupSteel.Id
        };

        var section = new CrossSection { Tag = "Б-1" };
        section.Areas.Add(region);
        section.Areas.Add(rebar);
        section.Areas.Add(stirrupMaterialCarrier);
        return section;
    }
}
