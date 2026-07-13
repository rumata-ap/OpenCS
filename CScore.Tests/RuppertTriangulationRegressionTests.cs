using CSTriangulation;
using Xunit;

namespace CScore.Tests;

/// <summary>Регрессия: Ruppert-триангуляция (диалог полигональных материальных областей)
/// игнорировала maxAngl (всегда), и maxTrgArea при наличии отверстия в области.</summary>
public class RuppertTriangulationRegressionTests
{
    static MaterialArea BuildSquareArea(double side)
    {
        double h = side / 2;
        var hull = new Contour(new[] { -h, h, h, -h, -h }, new[] { -h, -h, h, h, -h }, "sq")
        { Type = ContourType.Hull };
        var area = new MaterialArea
        {
            Id = 1, Tag = "sq", Category = AreaCategory.Region,
            Contours = [hull]
        };
        area.Hull = hull;
        return area;
    }

    static MaterialArea BuildSquareWithHoleArea()
    {
        var hull = new Contour(new[] { -5.0, 5, 5, -5, -5 }, new[] { -5.0, -5, 5, 5, -5 }, "outer")
        { Type = ContourType.Hull };
        var hole = new Contour(new[] { -1.0, 1, 1, -1, -1 }, new[] { -1.0, -1, 1, 1, -1 }, "hole")
        { Type = ContourType.Hole };
        var area = new MaterialArea
        {
            Id = 1, Tag = "h", Category = AreaCategory.Region, Contours = [hull, hole]
        };
        area.Hull = hull;
        return area;
    }

    [Fact]
    public void Ruppert_SmallerMaxArea_ProducesMoreTriangles_WithHole()
    {
        var coarseFibers = Geo.Triangulation(BuildSquareWithHoleArea(), maxTrgArea: 0.2, maxAngl: 25, scale: 8,
            method: TriangulationMethod.Ruppert);
        var fineFibers = Geo.Triangulation(BuildSquareWithHoleArea(), maxTrgArea: 0.01, maxAngl: 25, scale: 8,
            method: TriangulationMethod.Ruppert);

        Assert.True(fineFibers.Length > coarseFibers.Length * 3,
            $"coarse={coarseFibers.Length}, fine={fineFibers.Length} — сетка с отверстием не реагирует на maxTrgArea");
    }

    [Fact]
    public void Ruppert_StricterMinAngle_ProducesMoreTriangles()
    {
        var laxFibers = Geo.Triangulation(BuildSquareArea(1.0), maxTrgArea: 0.05, maxAngl: 5, scale: 8,
            method: TriangulationMethod.Ruppert);
        var strictFibers = Geo.Triangulation(BuildSquareArea(1.0), maxTrgArea: 0.05, maxAngl: 33, scale: 8,
            method: TriangulationMethod.Ruppert);

        Assert.True(strictFibers.Length > laxFibers.Length,
            $"lax(angle=5)={laxFibers.Length}, strict(angle=33)={strictFibers.Length} — сетка не реагирует на maxAngl");
    }
}
