using CScore.Sp63Shear;
using Xunit;

namespace CScore.Tests.Sp63Shear;

/// <summary>Коэффициент φn по п. 8.1.34 СП 63.13330.</summary>
public sealed class PhiNCalculatorTests
{
    [Fact]
    public void Compute_BendingUnstressedWithAxialForce_KeepsUnity()
    {
        var result = PhiNCalculator.Compute(ElementKind.BendingUnstressed, -800.0, Geometry());

        Assert.Equal(1.0, result.Value, 12);
        Assert.False(result.AppliesToStrip);
        Assert.Contains("8.1.34", result.Explanation);
    }

    [Fact]
    public void Compute_CompressionForOtherElement_IncreasesCapacity()
    {
        // Сила подобрана так, чтобы отсечка 2,25 не срабатывала и проверялась сама формула:
        // при N = 800 кН φn = 2,559 и результат определялся бы отсечкой.
        var geometry = Geometry();
        var result = PhiNCalculator.Compute(ElementKind.Other, -300.0, geometry);

        double nuB = geometry.Rb / (geometry.Eb0 * geometry.Eb);
        double aRed = geometry.Ab * nuB + geometry.AsTotal;
        double expected = 1.0 + 300.0 / aRed / geometry.Rb;

        Assert.True(expected < PhiNCalculator.MaxCompression);
        Assert.Equal(expected, result.Value, 9);
        Assert.True(result.AppliesToStrip);
    }

    [Fact]
    public void Compute_LargeCompression_IsCappedAt225()
    {
        var result = PhiNCalculator.Compute(ElementKind.Other, -100_000.0, Geometry());

        Assert.Equal(2.25, result.Value, 12);
    }

    [Fact]
    public void Compute_Tension_ReducesCapacityAndSkipsStrip()
    {
        var result = PhiNCalculator.Compute(ElementKind.Other, 300.0, Geometry());

        Assert.True(result.Value < 1.0);
        Assert.False(result.AppliesToStrip);
    }

    [Fact]
    public void Compute_HugeTension_MayBecomeNonPositive()
    {
        var result = PhiNCalculator.Compute(ElementKind.Other, 100_000.0, Geometry());

        Assert.True(result.Value <= 0.0);
    }

    [Fact]
    public void Compute_ZeroAxialForce_IsUnity()
    {
        var result = PhiNCalculator.Compute(ElementKind.Other, 0.0, Geometry());

        Assert.Equal(1.0, result.Value, 12);
    }

    static InclinedSectionGeometry Geometry() => new(
        B: 0.30, H0: 0.55, Ns: 435.0, As: 0.001,
        Rb: 11_500.0, Rbt: 900.0, Ab: 0.18, AsTotal: 0.0015,
        Eb: 24_000_000.0, Eb0: 0.002, Ebt0: 0.0001,
        Plane: ShearPlane.Vy, TensionOnPositiveSide: false, Warnings: []);
}
