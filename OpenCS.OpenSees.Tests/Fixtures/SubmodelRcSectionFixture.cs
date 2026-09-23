using CScore;

namespace OpenCS.OpenSees.Tests.Fixtures;

/// <summary>
/// Железобетонное сечение для живых нелинейных тестов субмодели: квадрат 0,4 × 0,4 м (бетон — сетка
/// 10 × 10 фибр) и 4 угловых стержня Ø20 с осевым отступом 0,04 м. Сечение симметрично относительно
/// обеих осей, поэтому ориентация местных осей стержня и знак момента на результат не влияют.
/// Материалы в кПа (адаптер переводит в Па): бетон E = 30 ГПа, Rbt = 1,05 МПа, Rb = 11,5 МПа;
/// арматура E = 200 ГПа, Rs = 350 МПа; диаграммы L2.
/// </summary>
internal static class SubmodelRcSectionFixture
{
    public const double Side = 0.4, Cover = 0.04, BarArea = Math.PI * 0.02 * 0.02 / 4;
    public const double RsPa = 350e6;

    /// <summary>Оценка предельного момента: два стержня у растянутой грани, плечо между рядами арматуры.</summary>
    public static double UltimateMomentEstimate => 2 * BarArea * RsPa * (Side - 2 * Cover);

    public static (CrossSection Section, Dictionary<int, Material> Materials) Create()
    {
        var concrete = CrossSectionFixtures.CreateMaterial(10, MatType.Concrete, "B20", 3.0e7, 1.05e3, -1.15e4);
        var steel = CrossSectionFixtures.CreateMaterial(20, MatType.ReSteelF, "A400", 2.0e8, 3.5e5, -3.5e5);

        const int n = 10;
        double cell = Side / n;
        var concreteFibers = new List<Fiber>();
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                concreteFibers.Add(new Fiber
                {
                    X = -Side / 2 + (i + 0.5) * cell, Y = -Side / 2 + (j + 0.5) * cell, Area = cell * cell, TypeFiber = FiberType.poly
                });
        var concreteArea = new MaterialArea
        {
            Id = 1, Tag = "concrete", Material = concrete, MaterialId = concrete.Id, DiagrammType = DiagrammType.L2,
            Fibers = concreteFibers
        };

        double c = Side / 2 - Cover;
        var rebarArea = new MaterialArea
        {
            Id = 2, Tag = "rebar", Material = steel, MaterialId = steel.Id, DiagrammType = DiagrammType.L2,
            HostArea = concreteArea, HostAreaId = concreteArea.Id,
            Fibers =
            [
                new Fiber { X = -c, Y = -c, Area = BarArea, TypeFiber = FiberType.point },
                new Fiber { X = -c, Y = c, Area = BarArea, TypeFiber = FiberType.point },
                new Fiber { X = c, Y = -c, Area = BarArea, TypeFiber = FiberType.point },
                new Fiber { X = c, Y = c, Area = BarArea, TypeFiber = FiberType.point },
            ]
        };

        return (new CrossSection { Areas = [concreteArea, rebarArea] }, CrossSectionFixtures.Materials(concrete, steel));
    }
}
