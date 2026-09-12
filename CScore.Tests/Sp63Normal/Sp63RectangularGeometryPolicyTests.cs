using CScore;
using CScore.Sp63;
using CScore.Sp63Shear;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Проверки общей политики прямоугольной геометрии СП 63.</summary>
public sealed class Sp63RectangularGeometryPolicyTests
{
    [Fact]
    public void SharedPolicy_AndShearClassifier_AgreeOnValidRectangle()
    {
        var section = Sp63NormalFixtures.Rectangle(0.30, 0.60);

        var common = Sp63RectangularGeometryPolicy.Classify(section);
        var shear = RectangularShearSectionClassifier.Classify(section);

        Assert.True(common.IsApplicable);
        Assert.True(shear.IsSupported);
        Assert.Equal(common.Reasons, shear.Reasons);
        Assert.Equal(0.30, common.Geometry!.Width, precision: 12);
        Assert.Equal(0.60, common.Geometry.Height, precision: 12);
    }

    [Fact]
    public void TwoConcreteRegions_AreNotApplicable()
    {
        var section = Sp63NormalFixtures.Rectangle(0.30, 0.60);
        section.Areas.Add(Sp63NormalFixtures.ConcreteRegion(
            Sp63NormalFixtures.Concrete(2), [
                (-0.01, -0.01), (0.01, -0.01), (0.01, 0.01), (-0.01, 0.01)
            ]));

        var result = Sp63RectangularGeometryPolicy.Classify(section);

        Assert.False(result.IsApplicable);
        Assert.NotEmpty(result.Reasons);
        Assert.Null(result.Geometry);
    }

    [Fact]
    public void Hole_IsNotApplicable()
    {
        var section = Sp63NormalFixtures.Rectangle(0.30, 0.60);
        section.Areas[0].Contours.Add(new Contour(
            [-0.04, 0.04, 0.04, -0.04, -0.04],
            [-0.04, -0.04, 0.04, 0.04, -0.04], "hole")
        {
            Type = ContourType.Hole
        });

        var result = Sp63RectangularGeometryPolicy.Classify(section);

        Assert.False(result.IsApplicable);
        Assert.NotEmpty(result.Reasons);
        Assert.Null(result.Geometry);
    }

    [Fact]
    public void NonFourVertexContour_IsNotApplicable()
    {
        var section = new CrossSection { Tag = "triangle" };
        section.Areas.Add(Sp63NormalFixtures.ConcreteRegion(
            Sp63NormalFixtures.Concrete(), [
                (-0.15, -0.30), (0.15, -0.30), (0.0, 0.30)
            ]));

        var result = Sp63RectangularGeometryPolicy.Classify(section);

        Assert.False(result.IsApplicable);
        Assert.NotEmpty(result.Reasons);
        Assert.Null(result.Geometry);
    }

    [Fact]
    public void RotatedContour_IsNotApplicable()
    {
        var section = new CrossSection { Tag = "rotated" };
        section.Areas.Add(Sp63NormalFixtures.ConcreteRegion(
            Sp63NormalFixtures.Concrete(), [
                (-0.10, -0.20), (0.20, -0.10), (0.10, 0.20), (-0.20, 0.10)
            ]));

        var result = Sp63RectangularGeometryPolicy.Classify(section);

        Assert.False(result.IsApplicable);
        Assert.NotEmpty(result.Reasons);
        Assert.Null(result.Geometry);
    }
}
