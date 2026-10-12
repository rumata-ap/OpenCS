using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки единиц отображения результатов FEM.</summary>
public class FemResultDisplayTests
{
    [Theory]
    [InlineData(FemLengthUnit.Millimeters, 1000.0)]
    [InlineData(FemLengthUnit.Centimeters, 100.0)]
    [InlineData(FemLengthUnit.Meters, 1.0)]
    public void ConvertLength_UsesMetersAsSourceUnit(FemLengthUnit unit, double expected)
    {
        Assert.Equal(expected, FemResultDisplayConverter.ConvertLength(1.0, unit), 12);
    }

    [Theory]
    [InlineData(FemRotationScale.One, 0.25)]
    [InlineData(FemRotationScale.OneHundred, 25.0)]
    [InlineData(FemRotationScale.OneThousand, 250.0)]
    public void ConvertRotation_AppliesConfiguredDisplayScale(FemRotationScale scale, double expected)
    {
        Assert.Equal(expected, FemResultDisplayConverter.ConvertRotation(0.25, scale), 12);
    }
}
