using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>
/// Проверки частичного покрытия п. 10.3.2 (защитный слой по диаметру стержня)
/// и п. 10.3.9 (минимум два растянутых стержня при широком сечении).
/// </summary>
public sealed class Sp63NormalCoverAndSpacingTests
{
    [Fact]
    public void Cover_TensionSide_Passes_WhenActualCoverExceedsDiameterAndMinimum()
    {
        // h - h0 = 0.60 - 0.55 = 0.05 м; диаметр 0.016 м -> факт. слой 0.042 м >= 0.016 м.
        var profile = Profile(h: 0.60, h0: 0.55, aPrime: 0.05,
            tensionDiameter: 0.016, compressionDiameter: 0.016);

        var (details, notes) = Sp63NormalConstructiveReinforcement.CheckCoverAndSpacing(profile);

        var detail = Assert.Single(details, d => d.Description == "Sp63Normal_MinCoverTension");
        Assert.Equal("10.3.2", detail.NormReference);
        Assert.Equal(0.016, detail.Applied, precision: 9);
        Assert.Equal(0.042, detail.Allowable, precision: 9);
        Assert.True(detail.Passed);
        Assert.Empty(notes);
    }

    [Fact]
    public void Cover_TensionSide_Fails_WhenEffectiveDepthLeavesNoRoomForDiameter()
    {
        // h - h0 = 0.006 м; диаметр 0.016 м -> факт. слой отрицательный.
        var profile = Profile(h: 0.60, h0: 0.594, aPrime: 0.006,
            tensionDiameter: 0.016, compressionDiameter: 0.016);

        var (details, _) = Sp63NormalConstructiveReinforcement.CheckCoverAndSpacing(profile);

        var detail = Assert.Single(details, d => d.Description == "Sp63Normal_MinCoverTension");
        Assert.False(detail.Passed);
    }

    [Fact]
    public void Cover_CompressionSide_Skipped_WhenCompressionAreaIsZero()
    {
        var profile = Profile(h: 0.60, h0: 0.55, aPrime: 0.05,
            tensionDiameter: 0.016, compressionDiameter: 0.016, compressionArea: 0.0);

        var (details, _) = Sp63NormalConstructiveReinforcement.CheckCoverAndSpacing(profile);

        Assert.DoesNotContain(details,
            detail => detail.Description == "Sp63Normal_MinCoverCompression");
    }

    [Fact]
    public void Cover_UnknownBarDiameter_AddsInformationalNote_InsteadOfSilentlySkipping()
    {
        var profile = Profile(h: 0.60, h0: 0.55, aPrime: 0.05,
            tensionDiameter: 0.0, compressionDiameter: 0.0);

        var (details, notes) = Sp63NormalConstructiveReinforcement.CheckCoverAndSpacing(profile);

        Assert.DoesNotContain(details,
            detail => detail.Description == "Sp63Normal_MinCoverTension");
        Assert.Contains(notes, note => note.Code == "cover_bar_diameter_unknown");
    }

    [Fact]
    public void TensionBarCount_Passes_WhenTwoBars_AndWidthAboveThreshold()
    {
        var profile = Profile(h: 0.60, h0: 0.55, aPrime: 0.05,
            tensionDiameter: 0.016, compressionDiameter: 0.016, b: 0.30, tensionBarCount: 2);

        var (details, _) = Sp63NormalConstructiveReinforcement.CheckCoverAndSpacing(profile);

        var detail = Assert.Single(details, d => d.Description == "Sp63Normal_MinTensionBarCount");
        Assert.Equal("10.3.9", detail.NormReference);
        Assert.True(detail.Passed);
    }

    [Fact]
    public void TensionBarCount_Fails_WhenOnlyOneBar_AndWidthAboveThreshold()
    {
        var profile = Profile(h: 0.60, h0: 0.55, aPrime: 0.05,
            tensionDiameter: 0.016, compressionDiameter: 0.016, b: 0.30, tensionBarCount: 1);

        var (details, _) = Sp63NormalConstructiveReinforcement.CheckCoverAndSpacing(profile);

        var detail = Assert.Single(details, d => d.Description == "Sp63Normal_MinTensionBarCount");
        Assert.False(detail.Passed);
    }

    [Fact]
    public void TensionBarCount_Skipped_WhenWidthAtOrBelowThreshold()
    {
        var profile = Profile(h: 0.60, h0: 0.55, aPrime: 0.05,
            tensionDiameter: 0.016, compressionDiameter: 0.016, b: 0.150, tensionBarCount: 1);

        var (details, _) = Sp63NormalConstructiveReinforcement.CheckCoverAndSpacing(profile);

        Assert.DoesNotContain(details,
            detail => detail.Description == "Sp63Normal_MinTensionBarCount");
    }

    static Sp63NormalSectionProfile Profile(double h, double h0, double aPrime,
        double tensionDiameter, double compressionDiameter,
        double b = 0.30, double tensionArea = 0.0020, double compressionArea = 0.0020,
        int tensionBarCount = 2)
    {
        var tensionBars = new List<(double X, double Y, double Area, double Diameter)>();
        for (int i = 0; i < tensionBarCount; i++)
            tensionBars.Add((0.0, 0.0, tensionArea / tensionBarCount, tensionDiameter));

        var compressionBars = compressionArea > 0
            ? new List<(double X, double Y, double Area, double Diameter)>
                { (0.0, 0.0, compressionArea, compressionDiameter) }
            : [];

        var tensionLayer = new Sp63NormalRebarLayer(-h / 2 + aPrime, tensionArea,
            435_000.0, 435_000.0, tensionBars);
        var compressionLayer = new Sp63NormalRebarLayer(h / 2 - aPrime, compressionArea,
            435_000.0, 435_000.0, compressionBars);
        return new Sp63NormalSectionProfile(
            B: b,
            Height: h,
            H0: h0,
            APrime: aPrime,
            TensionLayer: tensionLayer,
            CompressionLayer: compressionLayer,
            TotalRebarArea: tensionArea + compressionArea,
            IsSymmetric: true,
            SymmetryRelativeDifference: 0.0,
            PrecomputedXWithoutCompressionRebar: 0.0);
    }
}
