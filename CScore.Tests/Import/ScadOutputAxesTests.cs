using CScore.Import;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Import;

public class ScadOutputAxesTests
{
    static readonly PlanarVector3[] Plate =
        [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(1, 1, 0)]; // зигзаг SCAD

    static double? Angle(byte type, PlanarVector3[] pts, double x, double y, double z) =>
        ScadOutputAxes.AngleDeg(type, [x, y, z], pts);

    [Fact]
    public void HorizontalPlate_GlobalX_Zero() =>
        Assert.Equal(0.0, Angle(16, Plate, 1, 0, 0)!.Value, 9);

    [Fact]
    public void PlateRotated30InPlan_GlobalX_Minus30()
    {
        double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6);
        PlanarVector3[] pts = [new(0, 0, 0), new(c, s, 0), new(-s, c, 0)];
        Assert.Equal(-30.0, Angle(16, pts, 1, 0, 0)!.Value, 9);
    }

    [Fact]
    public void WallInXZ_GlobalZ_SignFollowsNormal()
    {
        // Нормаль (1,0,0)×(0,0,1) = (0,−1,0): поворот e1 = X к Z вокруг −Y — +90°.
        PlanarVector3[] wall = [new(0, 0, 0), new(1, 0, 0), new(0, 0, 1)];
        Assert.Equal(90.0, Angle(16, wall, 0, 0, 1)!.Value, 9);

        // Третий узел ниже — нормаль (0,+1,0), тот же X1 = Z даёт −90°.
        PlanarVector3[] flippedX = [new(0, 0, 0), new(1, 0, 0), new(0, 0, -1)];
        Assert.Equal(-90.0, Angle(16, flippedX, 0, 0, 1)!.Value, 9);
    }

    [Fact]
    public void Y1Vector_GlobalY_OnPlate_Zero() =>
        Assert.Equal(0.0, Angle(19, Plate, 0, 1, 0)!.Value, 9);

    [Fact]
    public void PointFromNode1_AndFromCenter()
    {
        // От узла 1 (0,0,0) на точку (5,5,0) — 45°.
        Assert.Equal(45.0, Angle(17, Plate, 5, 5, 0)!.Value, 9);
        // От центра (0,5;0,5;0) на точку (0,5;3;0) — вдоль Y, 90°.
        Assert.Equal(90.0, Angle(18, Plate, 0.5, 3, 0)!.Value, 9);
        // Y1 от узла 1 на (0,1,0): Y1 = Y → X1 = X, 0°.
        Assert.Equal(0.0, Angle(20, Plate, 0, 1, 0)!.Value, 9);
        // Y1 от центра на (−3; 0,5; 0): Y1 = −X → X1 = Y, 90°.
        Assert.Equal(90.0, Angle(21, Plate, -3, 0.5, 0)!.Value, 9);
    }

    [Fact]
    public void DirectionNormalToPlate_Null() =>
        Assert.Null(Angle(16, Plate, 0, 0, 1));

    [Fact]
    public void NonPlateTypeOrShortData_Null()
    {
        Assert.Null(Angle(15, Plate, 1, 0, 0));
        Assert.Null(Angle(22, Plate, 1, 0, 0));
        Assert.Null(ScadOutputAxes.AngleDeg(16, [1, 0], Plate));
    }

    [Theory]
    [InlineData(1.0, 2.0, 0.3)]
    [InlineData(-1.0, 0.5, -0.2)]
    [InlineData(0.2, -3.0, 1.0)]
    public void Inverse_PlateLayoutFormula_RestoresDirection(double vx, double vy, double vz)
    {
        // Наклонный КЭ общего положения.
        PlanarVector3[] pts = [new(1, 2, 3), new(2, 2.5, 3.4), new(0.7, 3, 3.9)];
        double a = Angle(16, pts, vx, vy, vz)!.Value * Math.PI / 180.0;

        var e1 = (pts[1] - pts[0]).Normalize();
        var n = (pts[1] - pts[0]).Cross(pts[2] - pts[0]).Normalize();
        var x1 = e1 * Math.Cos(a) + n.Cross(e1) * Math.Sin(a); // как в PlateLayoutSectionSource

        var v = new PlanarVector3(vx, vy, vz);
        var expected = (v - n * v.Dot(n)).Normalize();
        Assert.Equal(expected.X, x1.X, 9);
        Assert.Equal(expected.Y, x1.Y, 9);
        Assert.Equal(expected.Z, x1.Z, 9);
    }
}
