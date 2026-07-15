using CScore;

namespace OpenCS.OpenSees.Tests.Fixtures;

internal static class CrossSectionFixtures
{
    public static (CrossSection Section, Material Concrete, Material Steel) RectangularSection()
    {
        Material concrete = CreateMaterial(10, MatType.Concrete, "B25", 30_000, 1_500, -20_000);
        Material steel = CreateMaterial(20, MatType.ReSteelF, "A500", 200_000, 500_000, -500_000);

        MaterialArea concreteArea = new()
        {
            Id = 1,
            Tag = "concrete-area",
            Material = concrete,
            MaterialId = concrete.Id,
            DiagrammType = DiagrammType.L2,
            Fibers =
            [
                new Fiber { X = 0.2, Y = 0.3, Area = 0.01, TypeFiber = FiberType.tri },
                new Fiber { X = -0.2, Y = -0.3, Area = 0.02, TypeFiber = FiberType.poly }
            ]
        };

        MaterialArea rebarArea = new()
        {
            Id = 2,
            Tag = "rebar-area",
            Material = steel,
            MaterialId = steel.Id,
            DiagrammType = DiagrammType.L2,
            HostArea = concreteArea,
            HostAreaId = concreteArea.Id,
            Fibers =
            [
                new Fiber { X = 0.2, Y = -0.35, Area = 0.0002, TypeFiber = FiberType.point }
            ]
        };

        return (new CrossSection { Areas = [concreteArea, rebarArea] }, concrete, steel);
    }

    public static Dictionary<int, Material> Materials(Material concrete, Material steel) =>
        new()
        {
            [concrete.Id] = concrete,
            [steel.Id] = steel
        };

    private static Material CreateMaterial(
        int id,
        MatType type,
        string tag,
        double e,
        double tension,
        double compression)
    {
        Material material = new()
        {
            Id = id,
            Tag = tag,
            Type = type,
            E = e
        };

        foreach (CalcType calc in Enum.GetValues<CalcType>())
        {
            MaterialChars chars = new()
            {
                Type = type,
                TypeCalc = calc,
                E = e,
                Ft = tension,
                Fc = compression,
                Ry = tension,
                Ru = tension,
                Ec1Red = -0.0015,
                Ec2 = -0.003,
                Et1Red = 0.0001,
                Et2 = 0.02
            };

            switch (calc)
            {
                case CalcType.C: material.C = chars; break;
                case CalcType.CL: material.CL = chars; break;
                case CalcType.N: material.N = chars; break;
                case CalcType.NL: material.NL = chars; break;
            }
        }

        return material;
    }
}
