using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>Проверки порядка обхода волокон для сопоставления с записями OpenSees.</summary>
public sealed class RecordedFiberIndexingTests
{
    static CrossSection BuildSection()
    {
        var concrete = TestMaterials.Concrete("B25");
        var steel = TestMaterials.Rebar("A500");
        var concreteArea = new MaterialArea
        {
            Tag = "concrete",
            Category = AreaCategory.Region,
            Material = concrete,
            MaterialId = concrete.Id,
            DiagrammType = DiagrammType.L2,
            Fibers =
            [
                new Fiber { X = 0, Y = 0.1, Area = 0.01, TypeFiber = FiberType.tri },
                new Fiber { X = 0.1, Y = 0.1, Area = 0.01, TypeFiber = FiberType.poly },
                new Fiber { X = 0.2, Y = 0.1, Area = 0.01, TypeFiber = FiberType.tri },
            ],
        };
        var rebarArea = new MaterialArea
        {
            Tag = "rebar",
            Category = AreaCategory.RebarGroup,
            Material = steel,
            MaterialId = steel.Id,
            DiagrammType = DiagrammType.L2,
            Fibers =
            [
                Fiber.CreatePoint(0.012, -0.05, -0.2),
                Fiber.CreatePoint(0.012, 0.05, -0.2),
            ],
        };
        var section = new CrossSection { Areas = [concreteArea, rebarArea] };
        section.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: false);
        return section;
    }

    [Fact]
    public void Enumerate_IndexesMeshFibersThenPoints_InAreaOrder()
    {
        var section = BuildSection();

        var list = section.EnumerateRecordedFibers(new Kurvature(), CalcType.C).ToList();

        Assert.Equal(5, list.Count);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, list.Select(x => x.Index));
        Assert.All(list.Take(3), x => Assert.NotEqual(FiberType.point, x.Fiber.TypeFiber));
        Assert.All(list.Skip(3), x => Assert.Equal(FiberType.point, x.Fiber.TypeFiber));
        Assert.Equal(section.Areas[0], list[0].Area);
        Assert.Equal(section.Areas[1], list[3].Area);
    }

    [Fact]
    public void Enumerate_AreaWithoutDiagram_OccupiesIndexes_ButIsSkipped()
    {
        var section = BuildSection();
        section.Areas[0].Diagramms.Clear();

        var list = section.EnumerateRecordedFibers(new Kurvature(), CalcType.C).ToList();

        Assert.Equal(2, list.Count);
        Assert.Equal(new[] { 3, 4 }, list.Select(x => x.Index));
        Assert.All(list, x => Assert.Equal(FiberType.point, x.Fiber.TypeFiber));
    }
}
