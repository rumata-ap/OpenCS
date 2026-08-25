using CScore;
using CScore.Sp63Shear;
using Xunit;

namespace CScore.Tests;

/// <summary>Приведённая площадь ветвей элементов поперечного армирования по плоскостям сдвига.</summary>
public sealed class StirrupBranchAreaTests
{
    const double Asw = 0.0000503;

    [Fact]
    public void VerticalCut_ContributesToVyOnly()
    {
        var element = Cut(0.0, -0.2, 0.0, 0.2);

        var (vy, vx) = StirrupResolver.BranchAreas(element);

        Assert.Equal(Asw, vy, 12);
        Assert.Equal(0.0, vx, 12);
    }

    [Fact]
    public void HorizontalCut_ContributesToVxOnly()
    {
        var element = Cut(-0.1, 0.0, 0.1, 0.0);

        var (vy, vx) = StirrupResolver.BranchAreas(element);

        Assert.Equal(0.0, vy, 12);
        Assert.Equal(Asw, vx, 12);
    }

    [Fact]
    public void ClosedRectangularStirrup_GivesTwoBranchesInEachPlane()
    {
        var element = new StirrupElement
        {
            CenterlineContour = new Contour(
                [-0.1, 0.1, 0.1, -0.1, -0.1],
                [-0.2, -0.2, 0.2, 0.2, -0.2], "хомут"),
            BarAreaM2 = Asw,
            BarDiameterM = 0.008
        };

        var (vy, vx) = StirrupResolver.BranchAreas(element);

        Assert.Equal(2 * Asw, vy, 12);
        Assert.Equal(2 * Asw, vx, 12);
    }

    [Fact]
    public void InclinedCut_SplitsBetweenPlanesByProjection()
    {
        var element = Cut(0.0, 0.0, 0.3, 0.4);

        var (vy, vx) = StirrupResolver.BranchAreas(element);

        Assert.Equal(Asw * 0.4 / 0.5, vy, 12);
        Assert.Equal(Asw * 0.3 / 0.5, vx, 12);
    }

    static StirrupElement Cut(double x0, double y0, double x1, double y1) => new()
    {
        CenterlineContour = Contour.Polyline([x0, x1], [y0, y1], "срез"),
        BarAreaM2 = Asw,
        BarDiameterM = 0.008
    };
}
