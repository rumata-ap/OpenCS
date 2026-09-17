using CScore;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Распознавание круглых и кольцевых сечений из многоугольных контуров.</summary>
public sealed class Sp63CircularGeometryPolicyTests
{
    static Sp63CircularGeometryClassification Classify(CrossSection section,
        Sp63NormalShapeKind kind) => Sp63CircularGeometryPolicy.Classify(section, kind);

    static Sp63NormalMessage SingleMessage(Sp63CircularGeometryClassification result)
    {
        Assert.Null(result.Geometry);
        return Assert.Single(result.Messages);
    }

    [Fact]
    public void Circle32_IsAccepted_WithAreaEquivalentRadius()
    {
        var result = Classify(Sp63NormalFixtures.CircleSection(0.25, 32),
            Sp63NormalShapeKind.Circular);

        var g = Assert.IsType<Sp63CircularGeometry>(result.Geometry);
        double polygonArea = 16.0 * Math.Sin(2.0 * Math.PI / 32.0) * 0.0625;
        Assert.Equal(polygonArea, g.Area, 12);
        Assert.Equal(Math.Sqrt(polygonArea / Math.PI), g.OuterRadius, 12);
        Assert.Equal(0.0, g.InnerRadius);
        Assert.Equal(0.25, g.OuterMeanVertexRadius, 12);
        Assert.Equal(0.006413148855794248, g.OuterAreaDeviation, 9);
        Assert.True(g.OuterRadialDeviation < 1e-12);
        Assert.Equal(0.0, g.CenterX, 12);
        Assert.Equal(0.0, g.CenterY, 12);
    }

    [Fact]
    public void Circle16_IsRejectedByAreaCriterion_NotByRadialCriterion()
    {
        var section = Sp63NormalFixtures.CircleSection(0.25, 16);
        var measure = Sp63CircularGeometryPolicy.Measure(section.Areas[0].Hull!);

        Assert.True(measure.RadialDeviation < 1e-12);
        Assert.Equal(0.025504641595567312, measure.AreaDeviation, 9);
        var message = SingleMessage(Classify(section, Sp63NormalShapeKind.Circular));
        Assert.Equal("unsupported_geometry", message.Code);
        Assert.Equal("Sp63Normal_NotCircularContour", message.Text);
    }

    [Fact]
    public void Ellipse_IsRejectedByRadialCriterion()
    {
        var section = Sp63NormalFixtures.CircleSection(0.25, 64, scaleY: 0.9);
        var measure = Sp63CircularGeometryPolicy.Measure(section.Areas[0].Hull!);

        Assert.True(measure.RadialDeviation > Sp63CircularGeometryPolicy.RadialTolerance);
        Assert.Equal("Sp63Normal_NotCircularContour",
            SingleMessage(Classify(section, Sp63NormalShapeKind.Circular)).Text);
    }

    [Fact]
    public void Rectangle_IsRejected()
    {
        var message = SingleMessage(Classify(Sp63NormalFixtures.Rectangle(0.3, 0.3),
            Sp63NormalShapeKind.Circular));
        Assert.Equal("Sp63Normal_NotCircularContour", message.Text);
    }

    [Fact]
    public void Ring_IsAccepted()
    {
        var result = Classify(Sp63NormalFixtures.RingSection(0.30, 0.20),
            Sp63NormalShapeKind.Annular);

        var g = Assert.IsType<Sp63CircularGeometry>(result.Geometry);
        double k = 16.0 * Math.Sin(2.0 * Math.PI / 32.0);
        Assert.Equal(k * (0.09 - 0.04), g.Area, 12);
        Assert.Equal(Math.Sqrt(k * 0.09 / Math.PI), g.OuterRadius, 12);
        Assert.Equal(Math.Sqrt(k * 0.04 / Math.PI), g.InnerRadius, 12);
        Assert.True(g.CenterOffset < 1e-12);
        Assert.Equal(0.2, g.InnerMeanVertexRadius, 12);
    }

    [Fact]
    public void Ring_EccentricHole_IsRejected()
    {
        var message = SingleMessage(Classify(
            Sp63NormalFixtures.RingSection(0.30, 0.20, holeOffsetX: 0.01),
            Sp63NormalShapeKind.Annular));
        Assert.Equal("unsupported_geometry", message.Code);
        Assert.Equal("Sp63Normal_AnnularNotConcentric", message.Text);
    }

    [Fact]
    public void Ring_CoarseHole_IsRejected()
    {
        var message = SingleMessage(Classify(
            Sp63NormalFixtures.RingSection(0.30, 0.20, holeSegments: 16),
            Sp63NormalShapeKind.Annular));
        Assert.Equal("Sp63Normal_NotCircularContour", message.Text);
    }

    [Fact]
    public void Ring_SmallRadiusRatio_IsNotApplicable()
    {
        var message = SingleMessage(Classify(Sp63NormalFixtures.RingSection(0.30, 0.12),
            Sp63NormalShapeKind.Annular));
        Assert.Equal("annular_radius_ratio", message.Code);
        Assert.Equal("Sp63Normal_AnnularRadiusRatio", message.Text);
    }

    [Fact]
    public void CircularShape_WithHole_IsRejected()
    {
        var message = SingleMessage(Classify(Sp63NormalFixtures.RingSection(0.30, 0.20),
            Sp63NormalShapeKind.Circular));
        Assert.Equal("Sp63Normal_CircularHasHole", message.Text);
    }

    [Fact]
    public void AnnularShape_WithoutHole_IsRejected()
    {
        var message = SingleMessage(Classify(Sp63NormalFixtures.CircleSection(0.30),
            Sp63NormalShapeKind.Annular));
        Assert.Equal("Sp63Normal_AnnularHoleCount", message.Text);
    }

    [Fact]
    public void TwoConcreteRegions_AreRejected()
    {
        var section = Sp63NormalFixtures.CircleSection(0.25);
        section.Areas.Add(Sp63NormalFixtures.ConcreteRegion(Sp63NormalFixtures.Concrete(),
            Sp63NormalFixtures.CircleVertices(0.1, 32, centerX: 1.0)));

        var message = SingleMessage(Classify(section, Sp63NormalShapeKind.Circular));
        Assert.Equal("unsupported_geometry", message.Code);
        Assert.Equal("Sp63Normal_CircularSingleConcreteRegion", message.Text);
    }
}
