using CScore;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Проверки извлечения эффективных слоёв арматуры.</summary>
public sealed class Sp63RebarLayoutAnalyzerTests
{
    [Fact]
    public void Analyzer_GroupsBarsOnSameLevel_AndComputesH0AndAprime()
    {
        var section = Sp63NormalFixtures.Rectangle(0.30, 0.60);
        var steel = Sp63NormalFixtures.Rebar(2);
        Sp63NormalFixtures.AddBar(section, -0.05, -0.25, 0.0010, steel);
        Sp63NormalFixtures.AddBar(section, 0.05, -0.25, 0.0010, steel);
        Sp63NormalFixtures.AddBar(section, 0.00, 0.25, 0.0005, steel);

        var result = Sp63RebarLayoutAnalyzer.Analyze(section, Sp63NormalAxis.Mx,
            CalcType.C, tensionDirection: -1);

        Assert.NotNull(result.Profile);
        Assert.Equal(0.55, result.Profile!.H0, precision: 12);
        Assert.Equal(0.05, result.Profile.APrime, precision: 12);
        Assert.Equal(0.0025, result.Profile.TotalRebarArea, precision: 12);
        Assert.Equal(0.002, result.Profile.TensionLayer.Area, precision: 12);
        Assert.Equal(0.0005, result.Profile.CompressionLayer.Area, precision: 12);
    }

    [Fact]
    public void Analyzer_ReportsSymmetryByLayerResistances()
    {
        var section = Sp63NormalFixtures.TwoLayerRectangle(0.30, 0.60,
            tensionArea: 0.0010, compressionArea: 0.0010);

        var result = Sp63RebarLayoutAnalyzer.Analyze(section, Sp63NormalAxis.Mx,
            CalcType.C, tensionDirection: -1);

        Assert.True(result.Profile!.IsSymmetric);
        Assert.Equal(0.0, result.Profile.SymmetryRelativeDifference, precision: 12);
    }

    [Fact]
    public void Analyzer_SigSpNonZero_IsNotApplicable()
    {
        var section = Sp63NormalFixtures.TwoLayerRectangle(0.30, 0.60,
            tensionArea: 0.0010, compressionArea: 0.0010, sigSp: 100.0);

        var result = Sp63RebarLayoutAnalyzer.Analyze(section, Sp63NormalAxis.Mx,
            CalcType.C, tensionDirection: -1);

        Assert.Null(result.Profile);
        Assert.Contains(result.Messages, message => message.Code == "prestressed_rebar");
    }

    [Fact]
    public void Analyzer_SeveralMaterialResistances_AreNotApplicable()
    {
        var section = Sp63NormalFixtures.TwoLayerRectangle(0.30, 0.60,
            tensionArea: 0.0010, compressionArea: 0.0010,
            useDifferentRebar: true);

        var result = Sp63RebarLayoutAnalyzer.Analyze(section, Sp63NormalAxis.Mx,
            CalcType.C, tensionDirection: -1);

        Assert.Null(result.Profile);
        Assert.Contains(result.Messages, message => message.Code == "mixed_rebar_resistance");
    }

    [Fact]
    public void Analyzer_MyUsesXCoordinate_AsHeightCoordinate()
    {
        var section = Sp63NormalFixtures.Rectangle(0.30, 0.60);
        var steel = Sp63NormalFixtures.Rebar(2);
        Sp63NormalFixtures.AddBar(section, -0.10, 0.00, 0.0010, steel);
        Sp63NormalFixtures.AddBar(section, 0.10, 0.00, 0.0010, steel);

        var result = Sp63RebarLayoutAnalyzer.Analyze(section, Sp63NormalAxis.My,
            CalcType.C, tensionDirection: -1);

        Assert.Equal(0.25, result.Profile!.H0, precision: 12);
        Assert.Equal(0.05, result.Profile.APrime, precision: 12);
    }
}
