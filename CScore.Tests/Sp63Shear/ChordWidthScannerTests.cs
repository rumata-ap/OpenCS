using CScore;
using CScore.Sp63Shear;
using Xunit;

namespace CScore.Tests.Sp63Shear;

/// <summary>Определение расчётной ширины бетонного сечения по хордам.</summary>
public sealed class ChordWidthScannerTests
{
    [Fact]
    public void MinWidth_Rectangle_EqualsFullWidth()
    {
        var area = Region([(-0.15, -0.30), (0.15, -0.30), (0.15, 0.30), (-0.15, 0.30)]);

        double b = ChordWidthScanner.MinWidth([area], ShearPlane.Vy, -0.30, 0.25);

        Assert.Equal(0.30, b, 9);
    }

    [Fact]
    public void MinWidth_RangeTouchesBothFaces_EqualsFullWidth()
    {
        // Границы диапазона совпадают с гранями бетона: вырожденные хорды не должны дать 0
        var area = Region([(-0.15, -0.30), (0.15, -0.30), (0.15, 0.30), (-0.15, 0.30)]);

        Assert.Equal(0.30, ChordWidthScanner.MinWidth([area], ShearPlane.Vy, -0.30, 0.30), 9);
        Assert.Equal(0.30, ChordWidthScanner.MinWidth([area], ShearPlane.Vy, 0.30, -0.30), 9);
    }

    [Fact]
    public void MinWidth_TeeSection_EqualsWebWidth()
    {
        // Тавр: полка 0,60 м сверху (y от 0,20 до 0,30), ребро 0,20 м (y от −0,30 до 0,20)
        var area = Region(
        [
            (-0.10, -0.30), (0.10, -0.30), (0.10, 0.20), (0.30, 0.20),
            (0.30, 0.30), (-0.30, 0.30), (-0.30, 0.20), (-0.10, 0.20)
        ]);

        double b = ChordWidthScanner.MinWidth([area], ShearPlane.Vy, -0.30, 0.30);

        Assert.Equal(0.20, b, 9);
    }

    [Fact]
    public void MinWidth_NarrowNeckBetweenUniformLevels_IsNotOverestimated()
    {
        // Узкое горло шириной 0,04 м на участке y ∈ [0,001; 0,003] — между узлами
        // равномерной сетки из 50 уровней на высоте 0,60 м оно бы потерялось.
        var area = Region(
        [
            (-0.15, -0.30), (0.15, -0.30), (0.15, 0.001), (0.02, 0.001),
            (0.02, 0.003), (0.15, 0.003), (0.15, 0.30), (-0.15, 0.30),
            (-0.15, 0.003), (-0.02, 0.003), (-0.02, 0.001), (-0.15, 0.001)
        ]);

        double b = ChordWidthScanner.MinWidth([area], ShearPlane.Vy, -0.30, 0.30);

        Assert.Equal(0.04, b, 9);
    }

    [Fact]
    public void ChordLengthAt_SectionWithHole_SubtractsHole()
    {
        var area = Region([(-0.20, -0.20), (0.20, -0.20), (0.20, 0.20), (-0.20, 0.20)]);
        area.Contours.Add(new Contour(
            [-0.05, 0.05, 0.05, -0.05, -0.05],
            [-0.05, -0.05, 0.05, 0.05, -0.05], "hole") { Type = ContourType.Hole });
        area.SetWKT();

        double chord = ChordWidthScanner.ChordLengthAt([area], ShearPlane.Vy, 0.0);

        Assert.Equal(0.30, chord, 9);   // 0,40 − 0,10
    }

    [Fact]
    public void MinWidth_HorizontalPlane_MeasuresAlongY()
    {
        // Для плоскости Vx ширина меряется вдоль Y: прямоугольник 0,30 × 0,60 даёт 0,60
        var area = Region([(-0.15, -0.30), (0.15, -0.30), (0.15, 0.30), (-0.15, 0.30)]);

        double b = ChordWidthScanner.MinWidth([area], ShearPlane.Vx, -0.15, 0.15);

        Assert.Equal(0.60, b, 9);
    }

    static MaterialArea Region((double X, double Y)[] vertices)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var (x, y) in vertices) { xs.Add(x); ys.Add(y); }
        xs.Add(vertices[0].X);
        ys.Add(vertices[0].Y);

        var area = new MaterialArea { Category = AreaCategory.Region };
        area.Contours.Add(new Contour(xs, ys, "hull") { Type = ContourType.Hull });
        area.SetWKT();
        return area;
    }
}
