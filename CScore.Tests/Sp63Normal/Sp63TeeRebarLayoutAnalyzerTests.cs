using CScore;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Проверки извлечения профиля таврового сечения.</summary>
public sealed class Sp63TeeRebarLayoutAnalyzerTests
{
    [Fact]
    public void Analyze_BottomFlangeWithPositiveMoment_SelectsBottomFlangeAsCompressed()
    {
        var section = Sp63NormalFixtures.Tee(0.4, 0.8, 2.5, 0.2, flangeOnTop: false,
            tensionY: 0.25, compressionY: -0.35,
            tensionArea: 0.004, compressionArea: 0.001);

        var result = Sp63TeeRebarLayoutAnalyzer.Analyze(section, Sp63NormalAxis.Mx,
            CalcType.C, tensionDirection: 1);

        Assert.NotNull(result.Profile);
        var profile = result.Profile!;
        Assert.Equal(0.4, profile.Bw, precision: 12);
        Assert.Equal(0.8, profile.H, precision: 12);
        Assert.Equal(0.65, profile.H0, precision: 12);
        Assert.Equal(0.05, profile.APrime, precision: 12);
        Assert.Equal(2.5, profile.CompressionFlangeActualWidth, precision: 12);
        Assert.Equal(0.2, profile.CompressionFlangeThickness, precision: 12);
        Assert.Equal(0.004, profile.TensionLayer.Area, precision: 12);
        Assert.Equal(0.001, profile.CompressionLayer.Area, precision: 12);
    }

    [Fact]
    public void Analyze_TopFlangeWithNegativeMoment_SelectsTopFlangeAsCompressed()
    {
        var section = Sp63NormalFixtures.Tee(0.4, 0.8, 2.5, 0.2, flangeOnTop: true,
            tensionY: -0.25, compressionY: 0.35,
            tensionArea: 0.004, compressionArea: 0.001);

        var result = Sp63TeeRebarLayoutAnalyzer.Analyze(section, Sp63NormalAxis.Mx,
            CalcType.C, tensionDirection: -1);

        Assert.NotNull(result.Profile);
        Assert.Equal(2.5, result.Profile!.CompressionFlangeActualWidth, precision: 12);
        Assert.Equal(0.2, result.Profile.CompressionFlangeThickness, precision: 12);
    }

    [Fact]
    public void Analyze_TensionOnFlangeSide_ReportsNoCompressionFlange()
    {
        var section = Sp63NormalFixtures.Tee(0.4, 0.8, 2.5, 0.2, flangeOnTop: true,
            tensionY: 0.25, compressionY: -0.35,
            tensionArea: 0.004, compressionArea: 0.001);

        var result = Sp63TeeRebarLayoutAnalyzer.Analyze(section, Sp63NormalAxis.Mx,
            CalcType.C, tensionDirection: 1);

        Assert.NotNull(result.Profile);
        Assert.Equal(0.0, result.Profile!.CompressionFlangeActualWidth);
        Assert.Equal(0.0, result.Profile.CompressionFlangeThickness);
    }

    [Fact]
    public void Analyze_Rectangle_ReturnsNotATeeShape()
    {
        var result = Sp63TeeRebarLayoutAnalyzer.Analyze(
            Sp63NormalFixtures.TwoLayerRectangle(0.3, 0.6, 0.001, 0.001),
            Sp63NormalAxis.Mx, CalcType.C, tensionDirection: -1);

        Assert.Null(result.Profile);
        Assert.Contains(result.Messages, message => message.Code == "not_a_tee_shape");
    }

    [Fact]
    public void Analyze_MyAxis_UsesXCoordinate()
    {
        var section = Sp63NormalFixtures.Tee(0.4, 0.8, 2.5, 0.2, flangeOnTop: false,
            tensionY: 0.25, compressionY: -0.35,
            tensionArea: 0.004, compressionArea: 0.001, rotateForMy: true);

        var result = Sp63TeeRebarLayoutAnalyzer.Analyze(section, Sp63NormalAxis.My,
            CalcType.C, tensionDirection: 1);

        Assert.NotNull(result.Profile);
        Assert.Equal(0.65, result.Profile!.H0, precision: 12);
        Assert.Equal(2.5, result.Profile.CompressionFlangeActualWidth, precision: 12);
    }

    [Fact]
    public void Analyze_PrestressedRebar_IsNotApplicable()
    {
        var section = Sp63NormalFixtures.Tee(0.4, 0.8, 2.5, 0.2, flangeOnTop: false,
            tensionY: 0.25, compressionY: -0.35,
            tensionArea: 0.004, compressionArea: 0.001, sigSp: 100.0);

        var result = Sp63TeeRebarLayoutAnalyzer.Analyze(section, Sp63NormalAxis.Mx,
            CalcType.C, tensionDirection: 1);

        Assert.Null(result.Profile);
        Assert.Contains(result.Messages, message => message.Code == "prestressed_rebar");
    }
}
