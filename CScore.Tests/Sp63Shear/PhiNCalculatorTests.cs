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
    public void Compute_CompressionForOtherElement_UsesNormativeReducedArea()
    {
        // Сила подобрана так, чтобы σcp ≤ 0,25·Rb и проверялась первая ветвь 1 + σcp/Rb.
        var geometry = Geometry();
        var result = PhiNCalculator.Compute(ElementKind.Other, -300.0, geometry);

        double nuB = geometry.Rb / (geometry.Eb0 * geometry.Eb);
        double aRed = geometry.Ab + geometry.Es / geometry.Eb / nuB * geometry.AsTotal;
        double sigma = 300.0 / aRed;
        double expected = 1.0 + sigma / geometry.Rb;

        Assert.True(sigma <= 0.25 * geometry.Rb);
        Assert.Equal(expected, result.Value, 9);
        Assert.True(result.AppliesToStrip);
    }

    [Theory]
    [InlineData(0.10, 1.10)]   // 1 + σcp/Rb
    [InlineData(0.25, 1.25)]   // граница первой ветви и полки
    [InlineData(0.40, 1.25)]   // полка 0,25·Rb < σcp ≤ 0,5·Rb
    [InlineData(0.50, 1.25)]   // граница полки и ниспадающей ветви
    [InlineData(0.80, 0.50)]   // 2,5·(1 − 0,8)
    [InlineData(1.20, 0.00)]   // 2,5·(1 − σcp/Rb) ≥ 0
    public void CompressionValue_FollowsAmendment2Branches(double ratio, double expected)
    {
        // Изм. № 2 к СП 63.13330.2018, п. 8.1.34: три ветви по σcp/Rb, непрерывные на границах.
        Assert.Equal(expected, PhiNCalculator.CompressionValue(ratio * 11_500.0, 11_500.0), 12);
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(0.5, 0.75)]    // 1 − σtp/(2·Rbt)
    [InlineData(2.0, 0.0)]     // граница ≥ 0
    [InlineData(3.0, 0.0)]
    public void TensionValue_UsesTwiceRbt_AndIsNonNegative(double ratio, double expected)
    {
        Assert.Equal(expected, PhiNCalculator.TensionValue(ratio * 900.0, 900.0), 12);
    }

    [Fact]
    public void Compute_LargeCompression_DescendingBranchReachesZero()
    {
        // σcp ≫ Rb — ветвь 2,5·(1 − σcp/Rb) ограничена нулём, а не отсечкой 2,25 прежней редакции.
        var result = PhiNCalculator.Compute(ElementKind.Other, -100_000.0, Geometry());

        Assert.Equal(0.0, result.Value, 12);
        Assert.True(result.AppliesToStrip);
    }

    [Fact]
    public void Compute_Tension_ReducesCapacityAndSkipsStrip()
    {
        var geometry = Geometry();
        var result = PhiNCalculator.Compute(ElementKind.Other, 300.0, geometry);

        double nuBt = geometry.Rbt / (geometry.Ebt0 * geometry.Eb);
        double aRed = geometry.Ab + geometry.Es / geometry.Eb / nuBt * geometry.AsTotal;
        double expected = 1.0 - 300.0 / aRed / (2.0 * geometry.Rbt);

        Assert.Equal(expected, result.Value, 9);
        Assert.False(result.AppliesToStrip);
    }

    [Fact]
    public void Compute_HugeTension_IsZeroNotNegative()
    {
        var result = PhiNCalculator.Compute(ElementKind.Other, 100_000.0, Geometry());

        Assert.Equal(0.0, result.Value, 12);
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
