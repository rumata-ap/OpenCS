using CScore;
using CScore.Sp63.CrackWidth;
using CScore.Sp63.Normal;
using CScore.Tests.Sp63Fixtures;
using Xunit;

namespace CScore.Tests.Sp63CrackWidth;

/// <summary>
/// Аналитические тесты ориентированного полосового профиля SLS. Builders фикстур добавляют
/// ровно два point-fiber уровня арматуры, поэтому RED этих тестов должен возникать только на
/// отсутствующих типах профиля, а не на insufficient_rebar_layers.
/// </summary>
public sealed class Sp63SlsSectionGeometryTests
{
    [Fact]
    public void Rectangle_OneBand_HasAnalyticalAreaAndMoments()
    {
        var section = Sp63SlsTestSections.Rectangle(width: 0.5, height: 0.3);
        Assert.True(Sp63SlsSectionGeometryFactory.TryCreate(
            section, Sp63NormalShapeKind.Rectangular, Sp63NormalAxis.Mx, CalcType.N, 1,
            out var geometry, out var messages), string.Join(";", messages));

        var profile = Assert.IsType<Sp63SlsSectionGeometry>(geometry);
        Assert.Single(profile.Bands);
        Assert.Equal(0.5 * 0.3, profile.Area(0, 0.3), 12);
        Assert.Equal(0.5 * 0.3 * 0.3 / 2.0, profile.FirstMoment(0, 0.3), 12);
        Assert.Equal(0.5 * Math.Pow(0.3, 3) / 3.0, profile.SecondMoment(0, 0.3), 12);
    }

    [Fact]
    public void Tee_TwoBands_UsesTheActualFlangeWidth()
    {
        var section = Sp63SlsTestSections.TopTee(width: 0.6, height: 0.6, webWidth: 0.2, flangeHeight: 0.15);
        Assert.True(Sp63SlsSectionGeometryFactory.TryCreate(
            section, Sp63NormalShapeKind.Tee, Sp63NormalAxis.Mx, CalcType.N, -1,
            out var geometry, out var messages), string.Join(";", messages));

        var profile = Assert.IsType<Sp63SlsSectionGeometry>(geometry);
        Assert.Equal(2, profile.Bands.Count);
        Assert.Equal(0.2 * 0.45 + 0.6 * 0.15, profile.Area(0, 0.6), 12);
        Assert.True(profile.HasCompressionFlange);
        Assert.False(profile.HasTensionFlange);
    }

    [Fact]
    public void ISection_ThreeBands_ChangesBandOrderWhenMomentReverses()
    {
        var section = Sp63SlsTestSections.ISection(width: 0.6, height: 0.6,
            webWidth: 0.2, topFlangeHeight: 0.1, bottomFlangeHeight: 0.1);
        Assert.True(Sp63SlsSectionGeometryFactory.TryCreate(
            section, Sp63NormalShapeKind.Tee, Sp63NormalAxis.Mx, CalcType.N, 1,
            out var positive, out var positiveMessages), string.Join(";", positiveMessages));
        Assert.True(Sp63SlsSectionGeometryFactory.TryCreate(
            section, Sp63NormalShapeKind.Tee, Sp63NormalAxis.Mx, CalcType.N, -1,
            out var negative, out var negativeMessages), string.Join(";", negativeMessages));

        Assert.Equal(3, positive!.Bands.Count);
        Assert.True(positive.HasTensionFlange);
        Assert.True(positive.HasCompressionFlange);
        Assert.True(negative!.HasTensionFlange);
        Assert.True(negative.HasCompressionFlange);
        Assert.Equal(positive.Bands.Select(b => b.Width), negative!.Bands.Reverse().Select(b => b.Width));
    }
}
