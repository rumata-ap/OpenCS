using CScore.Planar;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class PlateStripGeometryBuilderTests
{
    [Fact]
    public void SupportLocus_StoresFrameAndStructuralMode()
    {
        var frame = Frame3D.Identity with { Origin = new PlanarVector3(2, 5, 0) };
        var locus = new SupportLocus { Frame = frame, StructuralMode = BeamJunctionMode.Support };

        Assert.Equal(frame, locus.Frame);
        Assert.Equal(BeamJunctionMode.Support, locus.StructuralMode);
    }

    [Fact]
    public void PlateStripGeometry_StoresPointsAndLength()
    {
        var geometry = new PlateStripGeometry
        {
            CenterLine = [new PlanarPoint2D(2, 5), new PlanarPoint2D(8, 5)],
            LeftBoundary = [new PlanarPoint2D(2, 6), new PlanarPoint2D(8, 6)],
            RightBoundary = [new PlanarPoint2D(2, 4), new PlanarPoint2D(8, 4)],
            Polygon = [new PlanarPoint2D(2, 4), new PlanarPoint2D(8, 4), new PlanarPoint2D(8, 6), new PlanarPoint2D(2, 6)],
            LengthM = 6
        };

        Assert.Equal(2, geometry.CenterLine.Count);
        Assert.Equal(4, geometry.Polygon.Count);
        Assert.Equal(6, geometry.LengthM);
    }

    [Fact]
    public void PlateStripBeamAnalogy_StoresAllFields()
    {
        var start = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(2, 5, 0) } };
        var end = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(8, 5, 0) } };
        var geometry = new PlateStripGeometry { LengthM = 6 };

        var analogy = new PlateStripBeamAnalogy
        {
            Id = "strip-1",
            SourceRegionId = 77,
            StartSupportLocus = start,
            EndSupportLocus = end,
            StripFrame = Frame3D.Identity with { Origin = new PlanarVector3(2, 5, 0) },
            Geometry = geometry,
            ExplicitWidthM = 2,
            Fingerprint = "abc"
        };

        Assert.Equal("strip-1", analogy.Id);
        Assert.Equal(77, analogy.SourceRegionId);
        Assert.Equal(start, analogy.StartSupportLocus);
        Assert.Equal(6, analogy.Geometry.LengthM);
        Assert.Equal(2, analogy.ExplicitWidthM);
        Assert.Equal("abc", analogy.Fingerprint);
    }

    [Fact]
    public void Build_HappyPath_ProducesRectangleStripInsideHull()
    {
        var region = Region();
        var start = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(2, 5, 0) } };
        var end = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(8, 5, 0) } };

        var result = PlateStripGeometryBuilder.Build("strip-1", region, start, end, 2.0);

        Assert.True(result.IsCalculable, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
        var analogy = result.Analogy!;
        Assert.Equal(6.0, analogy.Geometry.LengthM, 9);
        Assert.Equal(new[] { new PlanarPoint2D(2, 5), new PlanarPoint2D(8, 5) }, analogy.Geometry.CenterLine);
        Assert.Equal(new PlanarVector3(2, 5, 0), analogy.StripFrame.Origin);
        Assert.Equal(new PlanarVector3(1, 0, 0), analogy.StripFrame.LocalX);
        Assert.Equal(new PlanarVector3(0, 1, 0), analogy.StripFrame.LocalY);
        Assert.Equal(new PlanarVector3(0, 0, 1), analogy.StripFrame.LocalZ);

        var corners = new[] { new PlanarPoint2D(2, 4), new PlanarPoint2D(8, 4), new PlanarPoint2D(8, 6), new PlanarPoint2D(2, 6) };
        Assert.Equal(4, analogy.Geometry.Polygon.Count);
        foreach (var corner in corners)
            Assert.Contains(analogy.Geometry.Polygon, p => Close(p, corner));

        Assert.Equal(2, analogy.Geometry.LeftBoundary.Count);
        Assert.All(analogy.Geometry.LeftBoundary, p => Assert.Equal(6.0, p.V, 9));
        Assert.Equal(2, analogy.Geometry.RightBoundary.Count);
        Assert.All(analogy.Geometry.RightBoundary, p => Assert.Equal(4.0, p.V, 9));

        Assert.Equal(PlateStripFingerprint.Compute(region, start, end, 2.0), analogy.Fingerprint);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Build_RotatedRegionFrame_ProjectsConsistently()
    {
        var regionFrame = new Frame3D(
            new PlanarVector3(10, 20, 30),
            new PlanarVector3(0, 1, 0),
            new PlanarVector3(0, 0, 1),
            new PlanarVector3(1, 0, 0));
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [-10, 10, 10, -10], Y = [-10, -10, 10, 10] }, frame: regionFrame);

        var start = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(10, 21, 32) } };
        var end = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(10, 25, 32) } };

        var result = PlateStripGeometryBuilder.Build("strip-rot", region, start, end, 2.0);

        Assert.True(result.IsCalculable, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
        var analogy = result.Analogy!;
        Assert.Equal(4.0, analogy.Geometry.LengthM, 9);
        Assert.Equal(new[] { new PlanarPoint2D(1, 2), new PlanarPoint2D(5, 2) }, analogy.Geometry.CenterLine);
        Assert.Equal(new PlanarVector3(10, 21, 32), analogy.StripFrame.Origin);
        Assert.Equal(new PlanarVector3(0, 1, 0), analogy.StripFrame.LocalX);
        Assert.Equal(new PlanarVector3(0, 0, 1), analogy.StripFrame.LocalY);
        Assert.Equal(new PlanarVector3(1, 0, 0), analogy.StripFrame.LocalZ);
    }

    [Fact]
    public void Build_SupportOffsetAlongNormal_ProjectsOntoMidplane()
    {
        var regionFrame = new Frame3D(
            new PlanarVector3(10, 20, 30),
            new PlanarVector3(0, 1, 0),
            new PlanarVector3(0, 0, 1),
            new PlanarVector3(1, 0, 0));
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [-10, 10, 10, -10], Y = [-10, -10, 10, 10] }, frame: regionFrame);

        // Опоры со смещением +5 и -3 вдоль нормали региона (LocalZ = глобальная X) —
        // ось колонны, продолжающаяся выше/ниже средней плоскости плиты.
        var start = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(15, 21, 32) } };
        var end = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(7, 25, 32) } };

        var result = PlateStripGeometryBuilder.Build("strip-offplane", region, start, end, 2.0);

        Assert.True(result.IsCalculable, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
        Assert.Equal(new[] { new PlanarPoint2D(1, 2), new PlanarPoint2D(5, 2) }, result.Analogy!.Geometry.CenterLine);
    }

    [Fact]
    public void Build_NonFiniteSupportOrigin_ReturnsInvalidInputDiagnostic()
    {
        var region = Region();
        var start = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(double.NaN, 5, 0) } };
        var end = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(8, 5, 0) } };

        var result = PlateStripGeometryBuilder.Build("strip-nan-origin", region, start, end, 2.0);

        Assert.False(result.IsCalculable);
        Assert.Null(result.Analogy);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_invalid_input");
    }

    [Fact]
    public void Build_NonFiniteWidth_ReturnsInvalidInputDiagnostic()
    {
        var region = Region();
        var start = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(2, 5, 0) } };
        var end = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(8, 5, 0) } };

        var result = PlateStripGeometryBuilder.Build("strip-nan-width", region, start, end, double.NaN);

        Assert.False(result.IsCalculable);
        Assert.Null(result.Analogy);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_invalid_input");
    }

    [Fact]
    public void Build_CoincidentSupports_ReturnsDegenerateAxisDiagnostic()
    {
        var region = Region();
        var locus = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(2, 5, 0) } };

        var result = PlateStripGeometryBuilder.Build("strip-degenerate", region, locus, locus, 2.0);

        Assert.False(result.IsCalculable);
        Assert.Null(result.Analogy);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_degenerate_axis");
    }

    [Fact]
    public void Build_NonPositiveWidth_ReturnsInvalidWidthDiagnostic()
    {
        var region = Region();
        var start = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(2, 5, 0) } };
        var end = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(8, 5, 0) } };

        var result = PlateStripGeometryBuilder.Build("strip-zerowidth", region, start, end, 0.0);

        Assert.False(result.IsCalculable);
        Assert.Null(result.Analogy);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_invalid_width");
    }

    [Fact]
    public void Build_AxisOutsideHull_ReturnsOutsideRegionDiagnostic()
    {
        var region = Region();
        var start = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(20, 20, 0) } };
        var end = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(26, 20, 0) } };

        var result = PlateStripGeometryBuilder.Build("strip-outside", region, start, end, 2.0);

        Assert.False(result.IsCalculable);
        Assert.Null(result.Analogy);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_outside_region");
    }

    [Fact]
    public void Build_CenterLineOutsideButWideCorridorTouchesHull_ReturnsOutsideRegionDiagnostic()
    {
        // Ось строго горизонтальна на y=10.5..12 — целиком выше Hull (0..10 x 0..10).
        // Ширина 2 расширяет коридор поперёк оси (по V) до y=9.5..12.5, то есть коридор
        // формально задевает Hull, хотя сама ось (CenterLine) его нигде не касается.
        // До фикса эта полоса ошибочно строилась — проверяем, что теперь блокируется.
        var region = Region();
        var start = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(5, 10.5, 0) } };
        var end = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(7, 10.5, 0) } };

        var result = PlateStripGeometryBuilder.Build("strip-corridor-touches", region, start, end, 2.0);

        Assert.False(result.IsCalculable);
        Assert.Null(result.Analogy);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_outside_region");
    }

    [Fact]
    public void Build_CenterLineInsideHullButWidthExceedsBoundary_SucceedsWithClippedPolygon()
    {
        // Ось (5,1)-(5,9) целиком внутри Hull (0..10 x 0..10), но ширина 6 (коридор
        // u∈[2,8]) не обрезается, а вот у самого края V коридор не выходит за Hull в этом
        // сценарии по построению — проверяем именно "успех + разумный полигон", когда
        // геометрическая ширина ограничена Hull лишь частично (край полосы u∈[8,8] касается
        // границы). Клиппинг по ширине — не ошибка (см. спеку).
        var region = Region();
        var start = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(5, 1, 0) } };
        var end = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(5, 9, 0) } };

        var result = PlateStripGeometryBuilder.Build("strip-width-clipped", region, start, end, 20.0);

        Assert.True(result.IsCalculable, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
        var polygon = result.Analogy!.Geometry.Polygon;
        Assert.Equal(4, polygon.Count);
        // Ширина 20 (коридор u∈[-5,15]) обрезана Hull'ом (0..10) ровно по u∈[0,10] —
        // подтверждает, что клиппинг по ширине прошёл, а не заблокировался как ошибка.
        Assert.Contains(polygon, p => Close(p, new PlanarPoint2D(0, 1)));
        Assert.Contains(polygon, p => Close(p, new PlanarPoint2D(10, 1)));
        Assert.Contains(polygon, p => Close(p, new PlanarPoint2D(10, 9)));
        Assert.Contains(polygon, p => Close(p, new PlanarPoint2D(0, 9)));
    }

    [Fact]
    public void Build_StripCrossesNotchInNonConvexHull_ReturnsNonContiguousDiagnostic()
    {
        // "Скобообразный" (U-образный сверху) Hull: сплошной прямоугольник 0..10 x 0..10 с
        // выемкой сверху между x=4..6, идущей вниз до y=3. У y=8 сечение Hull по x —
        // два непересекающихся интервала: [0,4] и [6,10].
        var region = PlanarRegion.CreateFromContour(new Contour
        {
            X = [0, 10, 10, 6, 6, 4, 4, 0],
            Y = [0, 0, 10, 10, 3, 3, 10, 10]
        });
        var start = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(1, 8, 0) } };
        var end = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(9, 8, 0) } };

        var result = PlateStripGeometryBuilder.Build("strip-notch", region, start, end, 1.0);

        Assert.False(result.IsCalculable);
        Assert.Null(result.Analogy);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_non_contiguous");
    }

    [Fact]
    public void Build_HoleInsideCorridor_ReturnsCrossesHoleDiagnostic()
    {
        var region = Region(holes: [new Contour { X = [4, 6, 6, 4], Y = [4.5, 4.5, 5.5, 5.5] }]);
        var start = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(2, 5, 0) } };
        var end = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(8, 5, 0) } };

        var result = PlateStripGeometryBuilder.Build("strip-hole", region, start, end, 2.0);

        Assert.False(result.IsCalculable);
        Assert.Null(result.Analogy);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_crosses_hole");
    }

    [Fact]
    public void Build_HoleOutsideCorridor_SucceedsNormally()
    {
        var region = Region(holes: [new Contour { X = [8.5, 9.5, 9.5, 8.5], Y = [8.5, 8.5, 9.5, 9.5] }]);
        var start = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(2, 5, 0) } };
        var end = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(8, 5, 0) } };

        var result = PlateStripGeometryBuilder.Build("strip-hole-clear", region, start, end, 2.0);

        Assert.True(result.IsCalculable, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
        Assert.Equal(4, result.Analogy!.Geometry.Polygon.Count);
    }

    [Fact]
    public void Build_ConcaveHoleInsideCorridor_ReturnsCrossesHoleDiagnostic()
    {
        // U-образное (вогнутое) отверстие, целиком внутри коридора u∈[2,8], v∈[4,6].
        var region = Region(holes:
        [
            new Contour
            {
                X = [3, 7, 7, 5.5, 5.5, 4.5, 4.5, 3],
                Y = [4.2, 4.2, 5.8, 5.8, 4.8, 4.8, 5.8, 5.8]
            }
        ]);
        var start = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(2, 5, 0) } };
        var end = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(8, 5, 0) } };

        var result = PlateStripGeometryBuilder.Build("strip-hole-concave", region, start, end, 2.0);

        Assert.False(result.IsCalculable);
        Assert.Null(result.Analogy);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_crosses_hole");
    }

    [Fact]
    public void Build_HoleTouchesCorridorBoundaryOnly_SucceedsNormally()
    {
        // Отверстие целиком за пределами коридора (u >= 8), левое ребро лежит ровно на
        // границе коридора u=8 — касание без положительной площади пересечения не считается
        // пересечением (отличие от Build_HoleInsideCorridor... с реальным перекрытием).
        var region = Region(holes: [new Contour { X = [8, 9, 9, 8], Y = [4, 4, 6, 6] }]);
        var start = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(2, 5, 0) } };
        var end = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(8, 5, 0) } };

        var result = PlateStripGeometryBuilder.Build("strip-hole-touching", region, start, end, 2.0);

        Assert.True(result.IsCalculable, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
    }

    static bool Close(PlanarPoint2D a, PlanarPoint2D b, double tol = 1e-9) =>
        Math.Abs(a.U - b.U) < tol && Math.Abs(a.V - b.V) < tol;

    static PlanarRegion Region(IEnumerable<Contour>? holes = null) =>
        PlanarRegion.CreateFromContour(new Contour { X = [0, 10, 10, 0], Y = [0, 0, 10, 10] }, holes);
}
