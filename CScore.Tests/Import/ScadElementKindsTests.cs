using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

public class ScadElementKindsTests
{
    [Theory]
    [InlineData(10, 2, ScadElementKind.Beam)]
    [InlineData(5, 2, ScadElementKind.Beam)]
    [InlineData(42, 3, ScadElementKind.Shell)]
    [InlineData(44, 4, ScadElementKind.Shell)]
    [InlineData(11, 4, ScadElementKind.Shell)]
    [InlineData(51, 1, ScadElementKind.Skip)]
    [InlineData(51, 2, ScadElementKind.Skip)]
    [InlineData(100, 3, ScadElementKind.Skip)]
    [InlineData(200, 2, ScadElementKind.Skip)]
    [InlineData(21, 4, ScadElementKind.Skip)]
    [InlineData(30, 3, ScadElementKind.Skip)]
    [InlineData(10, 3, ScadElementKind.Skip)]
    [InlineData(44, 8, ScadElementKind.Skip)]
    public void Classify_ModelTypes(int type, int nodeCount, ScadElementKind expected) =>
        Assert.Equal(expected, ScadElementKinds.Classify(type, nodeCount));

    [Fact]
    public void ToPerimeterOrder_ZigzagSquare_BecomesNonSelfIntersectingContour()
    {
        // Квадрат в порядке SCAD (КЭ 55459 модели музея): (0;0) (1;0) (0;1) (1;1).
        var xy = new Dictionary<int, (double X, double Y)>
        {
            [1] = (0, 0), [2] = (1, 0), [3] = (0, 1), [4] = (1, 1),
        };
        int[] scad = [1, 2, 3, 4];

        int[] contour = ScadElementKinds.ToPerimeterOrder(scad);

        Assert.Equal([1, 2, 4, 3], contour);
        // Площадь контура по формуле Гаусса = 1 только у несамопересекающегося обхода.
        Assert.Equal(1.0, SignedArea(contour, xy), 12);
        Assert.Equal(0.0, SignedArea(scad, xy), 12);
        // Нормаль (p2−p1)×(p3−p1) знака не меняет.
        Assert.Equal(Math.Sign(Cross(scad, xy)), Math.Sign(Cross(contour, xy)));
    }

    [Fact]
    public void ToPerimeterOrder_TriangleAndBar_Unchanged()
    {
        Assert.Equal([4, 5, 6], ScadElementKinds.ToPerimeterOrder([4, 5, 6]));
        Assert.Equal([1, 2], ScadElementKinds.ToPerimeterOrder([1, 2]));
    }

    static double SignedArea(int[] ids, Dictionary<int, (double X, double Y)> xy)
    {
        double a = 0;
        for (int i = 0; i < ids.Length; i++)
        {
            var p = xy[ids[i]];
            var q = xy[ids[(i + 1) % ids.Length]];
            a += p.X * q.Y - q.X * p.Y;
        }
        return a / 2;
    }

    static double Cross(int[] ids, Dictionary<int, (double X, double Y)> xy)
    {
        var p1 = xy[ids[0]]; var p2 = xy[ids[1]]; var p3 = xy[ids[2]];
        return (p2.X - p1.X) * (p3.Y - p1.Y) - (p2.Y - p1.Y) * (p3.X - p1.X);
    }
}
