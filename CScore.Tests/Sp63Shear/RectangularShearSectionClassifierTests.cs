using System.Reflection;
using CScore;
using CScore.Sp63Shear;
using Xunit;

namespace CScore.Tests.Sp63Shear;

/// <summary>Автоматическая квалификация прямоугольного сечения для наклонных сечений.</summary>
public sealed class RectangularShearSectionClassifierTests
{
    [Fact]
    public void Classify_AxisAlignedSolidRectangle_IsSupported()
    {
        var result = Classify(Sp63ShearFixtures.Beam(-0.25, 0.25));

        Assert.True(ReadBool(result, "IsSupported"));
    }

    [Fact]
    public void Classify_RectangleWithHole_IsNotSupported()
    {
        var section = Sp63ShearFixtures.Beam(-0.25, 0.25);
        section.Areas[0].Contours.Add(new Contour(
            [-0.04, 0.04, 0.04, -0.04, -0.04],
            [-0.04, -0.04, 0.04, 0.04, -0.04], "hole") { Type = ContourType.Hole });

        var result = Classify(section);

        Assert.False(ReadBool(result, "IsSupported"));
        Assert.NotEmpty(ReadStrings(result, "Reasons"));
    }

    static object Classify(CrossSection section)
    {
        var type = typeof(ShearPlane).Assembly.GetType(
            "CScore.Sp63Shear.RectangularShearSectionClassifier");
        Assert.NotNull(type);
        var method = type!.GetMethod("Classify", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        return method!.Invoke(null, [section])!;
    }

    static bool ReadBool(object result, string property) =>
        (bool)result.GetType().GetProperty(property)!.GetValue(result)!;

    static IReadOnlyList<string> ReadStrings(object result, string property) =>
        (IReadOnlyList<string>)result.GetType().GetProperty(property)!.GetValue(result)!;
}
