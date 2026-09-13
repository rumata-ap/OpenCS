using CScore;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Проверки классификации осевой тавровой/двутавровой геометрии.</summary>
public sealed class Sp63TeeGeometryPolicyTests
{
    [Fact]
    public void TeeWithBottomFlange_Classifies()
    {
        var section = TeeSection(webWidth: 0.4, height: 0.8, flangeWidth: 2.5,
            flangeThickness: 0.2, flangeOnTop: false);

        var result = Sp63TeeGeometryPolicy.Classify(section, Sp63NormalAxis.Mx);

        Assert.True(result.IsApplicable);
        var geometry = result.Geometry!;
        Assert.Equal(0.4, geometry.Bw, precision: 12);
        Assert.Equal(0.8, geometry.H, precision: 12);
        Assert.Equal(-0.4, geometry.HeightCoordMin, precision: 12);
        Assert.Equal(0.4, geometry.HeightCoordMax, precision: 12);
        Assert.Equal(2.5, geometry.BottomFlangeWidth, precision: 12);
        Assert.Equal(0.2, geometry.BottomFlangeThickness, precision: 12);
        Assert.Equal(0.0, geometry.TopFlangeWidth);
        Assert.Equal(0.0, geometry.TopFlangeThickness);
    }

    [Fact]
    public void TeeWithTopFlange_Classifies()
    {
        var section = TeeSection(0.4, 0.8, 2.5, 0.2, flangeOnTop: true);

        var result = Sp63TeeGeometryPolicy.Classify(section, Sp63NormalAxis.Mx);

        Assert.True(result.IsApplicable);
        Assert.Equal(2.5, result.Geometry!.TopFlangeWidth, precision: 12);
        Assert.Equal(0.2, result.Geometry.TopFlangeThickness, precision: 12);
        Assert.Equal(0.0, result.Geometry.BottomFlangeWidth);
    }

    [Fact]
    public void IBeamWithDifferentFlanges_Classifies()
    {
        var section = IBeamSection(webWidth: 0.4, height: 1.0, topWidth: 2.5,
            topThickness: 0.15, bottomWidth: 1.6, bottomThickness: 0.25);

        var result = Sp63TeeGeometryPolicy.Classify(section, Sp63NormalAxis.Mx);

        Assert.True(result.IsApplicable);
        var geometry = result.Geometry!;
        Assert.Equal(0.4, geometry.Bw, precision: 12);
        Assert.Equal(2.5, geometry.TopFlangeWidth, precision: 12);
        Assert.Equal(0.15, geometry.TopFlangeThickness, precision: 12);
        Assert.Equal(1.6, geometry.BottomFlangeWidth, precision: 12);
        Assert.Equal(0.25, geometry.BottomFlangeThickness, precision: 12);
    }

    [Fact]
    public void MyAxis_UsesXAsHeight()
    {
        var section = TeeSection(0.4, 0.8, 2.5, 0.2, flangeOnTop: false,
            rotateForMy: true);

        var result = Sp63TeeGeometryPolicy.Classify(section, Sp63NormalAxis.My);

        Assert.True(result.IsApplicable);
        Assert.Equal(0.4, result.Geometry!.Bw, precision: 12);
        Assert.Equal(2.5, result.Geometry.BottomFlangeWidth, precision: 12);
    }

    [Fact]
    public void SingleRectangle_ReturnsNotATeeShape()
    {
        var result = Sp63TeeGeometryPolicy.Classify(
            Sp63NormalFixtures.Rectangle(0.4, 0.8), Sp63NormalAxis.Mx);

        Assert.False(result.IsApplicable);
        Assert.Equal(Sp63TeeGeometryFailure.NotATeeShape, result.Failure);
        Assert.NotEmpty(result.Reasons);
    }

    [Fact]
    public void Hole_IsUnsupported()
    {
        var section = TeeSection(0.4, 0.8, 2.5, 0.2, flangeOnTop: false);
        section.Areas[0].Contours.Add(new Contour(
            [-0.04, 0.04, 0.04, -0.04, -0.04],
            [-0.04, -0.04, 0.04, 0.04, -0.04], "hole")
        { Type = ContourType.Hole });

        var result = Sp63TeeGeometryPolicy.Classify(section, Sp63NormalAxis.Mx);

        Assert.False(result.IsApplicable);
        Assert.Equal(Sp63TeeGeometryFailure.UnsupportedGeometry, result.Failure);
    }

    [Fact]
    public void FlangeNarrowerThanWeb_IsUnsupported()
    {
        var section = ThreeStripSection(height: 0.8, topWidth: 0.4,
            topThickness: 0.2, webWidth: 0.6, bottomWidth: 0.4,
            bottomThickness: 0.2);

        var result = Sp63TeeGeometryPolicy.Classify(section, Sp63NormalAxis.Mx);

        Assert.False(result.IsApplicable);
        Assert.Equal(Sp63TeeGeometryFailure.UnsupportedGeometry, result.Failure);
    }

    [Fact]
    public void OffCenterFlange_IsUnsupported()
    {
        var section = new CrossSection { Tag = "off-center" };
        section.Areas.Add(Sp63NormalFixtures.ConcreteRegion(Sp63NormalFixtures.Concrete(),
        [
            (-0.3, -0.4), (2.0, -0.4), (2.0, -0.2), (0.2, -0.2),
            (0.2, 0.4), (-0.2, 0.4), (-0.2, -0.2), (-0.3, -0.2)
        ]));

        var result = Sp63TeeGeometryPolicy.Classify(section, Sp63NormalAxis.Mx);

        Assert.False(result.IsApplicable);
        Assert.Equal(Sp63TeeGeometryFailure.UnsupportedGeometry, result.Failure);
    }

    [Fact]
    public void ExtraCollinearVertex_IsUnsupported()
    {
        var section = new CrossSection { Tag = "collinear" };
        section.Areas.Add(Sp63NormalFixtures.ConcreteRegion(Sp63NormalFixtures.Concrete(),
        [
            (-1.25, -0.4), (1.25, -0.4), (1.25, -0.2), (0.2, -0.2),
            (0.2, 0.4), (0.0, 0.4), (-0.2, 0.4), (-0.2, -0.2), (-1.25, -0.2)
        ]));

        var result = Sp63TeeGeometryPolicy.Classify(section, Sp63NormalAxis.Mx);

        Assert.False(result.IsApplicable);
        Assert.Equal(Sp63TeeGeometryFailure.UnsupportedGeometry, result.Failure);
    }

    [Fact]
    public void FourStrips_AreUnsupported()
    {
        var section = new CrossSection { Tag = "four-strips" };
        section.Areas.Add(Sp63NormalFixtures.ConcreteRegion(Sp63NormalFixtures.Concrete(),
        [
            (-1.0, -0.5), (1.0, -0.5), (1.0, -0.4), (0.3, -0.4),
            (0.3, -0.1), (0.2, -0.1), (0.2, 0.3), (0.3, 0.3),
            (0.3, 0.5), (-0.3, 0.5), (-0.3, 0.3), (-0.2, 0.3),
            (-0.2, -0.1), (-0.3, -0.1), (-0.3, -0.4), (-1.0, -0.4)
        ]));

        var result = Sp63TeeGeometryPolicy.Classify(section, Sp63NormalAxis.Mx);

        Assert.False(result.IsApplicable);
        Assert.Equal(Sp63TeeGeometryFailure.UnsupportedGeometry, result.Failure);
    }

    [Fact]
    public void RoundedShape_IsUnsupported()
    {
        var section = new CrossSection { Tag = "inclined" };
        section.Areas.Add(Sp63NormalFixtures.ConcreteRegion(Sp63NormalFixtures.Concrete(),
        [
            (-1.0, -0.4), (1.0, -0.4), (1.0, -0.2), (0.5, 0.4),
            (-0.5, 0.4), (-1.0, -0.2)
        ]));

        var result = Sp63TeeGeometryPolicy.Classify(section, Sp63NormalAxis.Mx);

        Assert.False(result.IsApplicable);
        Assert.Equal(Sp63TeeGeometryFailure.UnsupportedGeometry, result.Failure);
    }

    static CrossSection TeeSection(double webWidth, double height, double flangeWidth,
        double flangeThickness, bool flangeOnTop, bool rotateForMy = false)
    {
        double xw = webWidth / 2, xf = flangeWidth / 2;
        double y0 = -height / 2, y1 = height / 2;
        (double X, double Y)[] vertices = flangeOnTop
            ? [(-xf, y1), (xf, y1), (xf, y1 - flangeThickness),
               (xw, y1 - flangeThickness), (xw, y0), (-xw, y0),
               (-xw, y1 - flangeThickness), (-xf, y1 - flangeThickness)]
            : [(-xf, y0), (xf, y0), (xf, y0 + flangeThickness),
               (xw, y0 + flangeThickness), (xw, y1), (-xw, y1),
               (-xw, y0 + flangeThickness), (-xf, y0 + flangeThickness)];
        if (rotateForMy)
            vertices = vertices.Select(vertex => (vertex.Y, vertex.X)).ToArray();
        return ContourSection(vertices);
    }

    static CrossSection IBeamSection(double webWidth, double height, double topWidth,
        double topThickness, double bottomWidth, double bottomThickness)
    {
        double xw = webWidth / 2, xt = topWidth / 2, xb = bottomWidth / 2;
        double y0 = -height / 2, y1 = height / 2;
        double yt = y1 - topThickness, yb = y0 + bottomThickness;
        return ContourSection(
        [
            (-xb, y0), (xb, y0), (xb, yb), (xw, yb), (xw, yt), (xt, yt),
            (xt, y1), (-xt, y1), (-xt, yt), (-xw, yt), (-xw, yb), (-xb, yb)
        ]);
    }

    static CrossSection ThreeStripSection(double height, double topWidth,
        double topThickness, double webWidth, double bottomWidth,
        double bottomThickness)
    {
        double xt = topWidth / 2, xb = bottomWidth / 2, xw = webWidth / 2;
        double y0 = -height / 2, y1 = height / 2;
        double yt = y1 - topThickness, yb = y0 + bottomThickness;
        return ContourSection(
        [
            (-xb, y0), (xb, y0), (xb, yb), (xw, yb), (xw, yt), (xt, yt),
            (xt, y1), (-xt, y1), (-xt, yt), (-xw, yt), (-xw, yb), (-xb, yb)
        ]);
    }

    static CrossSection ContourSection((double X, double Y)[] vertices)
    {
        var section = new CrossSection { Tag = "tee-test" };
        section.Areas.Add(Sp63NormalFixtures.ConcreteRegion(
            Sp63NormalFixtures.Concrete(), vertices));
        return section;
    }
}
