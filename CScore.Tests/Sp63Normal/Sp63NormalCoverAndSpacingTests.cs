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

    [Fact]
    public void IdealizedLayerSkipsCoverAndBarCountButKeepsInformationNote()
    {
        var physical = Profile(h: 0.60, h0: 0.55, aPrime: 0.05,
            tensionDiameter: 0.016, compressionDiameter: 0.016,
            b: 0.30, tensionBarCount: 1);
        var idealized = physical with
        {
            TensionLayer = physical.TensionLayer with { IsIdealized = true }
        };

        var (details, notes) = Sp63NormalConstructiveReinforcement.CheckCoverAndSpacing(idealized);

        Assert.DoesNotContain(details, detail => detail.Description is
            "Sp63Normal_MinCoverTension" or "Sp63Normal_MinTensionBarCount");
        Assert.Contains(notes, note => note.Code == "idealized_rebar_layer");
        var (percentage, _) = Sp63NormalConstructiveReinforcement.Check(
            "bending", idealized, MemberContext());
        Assert.Contains(percentage, detail => detail.Description == "Sp63Normal_MinReinforcementTension");
    }

    [Theory]
    [InlineData(Sp63ExposureCondition.IndoorNormal, false, 0.020)]
    [InlineData(Sp63ExposureCondition.IndoorHumid, false, 0.025)]
    [InlineData(Sp63ExposureCondition.Outdoor, false, 0.030)]
    [InlineData(Sp63ExposureCondition.Ground, false, 0.040)]
    [InlineData(Sp63ExposureCondition.Outdoor, true, 0.025)]
    [InlineData(Sp63ExposureCondition.Ground, true, 0.035)]
    public void Cover_Table101_RaisesRequiredCover(Sp63ExposureCondition exposure,
        bool isPrecast, double expectedTable)
    {
        // Диаметр 12 мм < любого значения таблицы — требование задаёт таблица 10.1.
        var profile = Profile(h: 0.60, h0: 0.55, aPrime: 0.05,
            tensionDiameter: 0.012, compressionDiameter: 0.012);

        var (details, notes) = Sp63NormalConstructiveReinforcement.CheckCoverAndSpacing(
            profile, Sp63NormalAxis.Mx, exposure: exposure, isPrecast: isPrecast);

        foreach (string key in new[] { "Sp63Normal_MinCoverTension", "Sp63Normal_MinCoverCompression" })
        {
            var detail = Assert.Single(details, d => d.Description == key);
            Assert.Equal(expectedTable, detail.Variables["tableCover"], 9);
            Assert.Equal(expectedTable, detail.Applied, 9);
            Assert.Equal(0.044, detail.Allowable, 9);
            Assert.True(detail.Passed);
        }
        Assert.Empty(notes);
    }

    [Fact]
    public void Cover_Table101_BarDiameterStillGoverns_WhenLarger()
    {
        // Сборный элемент в помещении: 20 − 5 = 15 мм < d = 16 мм.
        var profile = Profile(h: 0.60, h0: 0.55, aPrime: 0.05,
            tensionDiameter: 0.016, compressionDiameter: 0.016);

        var (details, _) = Sp63NormalConstructiveReinforcement.CheckCoverAndSpacing(
            profile, exposure: Sp63ExposureCondition.IndoorNormal, isPrecast: true);

        var detail = Assert.Single(details, d => d.Description == "Sp63Normal_MinCoverTension");
        Assert.Equal(0.015, detail.Variables["tableCover"], 9);
        Assert.Equal(0.016, detail.Applied, 9);
    }

    [Theory]
    [InlineData(false, 0.070, 0.040)]
    [InlineData(true, 0.035, 0.035)]
    public void Cover_FoundationWithoutPreparation_AppliesOnlyToBottomLayer(bool isPrecast,
        double expectedBottom, double expectedTop)
    {
        // Растянутый слой профиля — нижний (меньшая Y), факт. слой 44 мм.
        var profile = Profile(h: 0.60, h0: 0.55, aPrime: 0.05,
            tensionDiameter: 0.012, compressionDiameter: 0.012);

        var (details, _) = Sp63NormalConstructiveReinforcement.CheckCoverAndSpacing(
            profile, Sp63NormalAxis.Mx,
            exposure: Sp63ExposureCondition.FoundationWithoutPreparation, isPrecast: isPrecast);

        var bottom = Assert.Single(details, d => d.Description == "Sp63Normal_MinCoverTension");
        var top = Assert.Single(details, d => d.Description == "Sp63Normal_MinCoverCompression");
        Assert.Equal(expectedBottom, bottom.Applied, 9);
        Assert.Equal(expectedTop, top.Applied, 9);
        Assert.Equal(isPrecast, bottom.Passed);   // факт. 44 мм: < 70, но ≥ 35
    }

    [Fact]
    public void Cover_FoundationWithoutPreparation_UnderMy_UsesGroundValueAndAddsNote()
    {
        var profile = Profile(h: 0.60, h0: 0.55, aPrime: 0.05,
            tensionDiameter: 0.012, compressionDiameter: 0.012);

        var (details, notes) = Sp63NormalConstructiveReinforcement.CheckCoverAndSpacing(
            profile, Sp63NormalAxis.My,
            exposure: Sp63ExposureCondition.FoundationWithoutPreparation);

        Assert.All(details.Where(d => d.NormReference == "10.3.2"),
            d => Assert.Equal(0.040, d.Applied, 9));
        Assert.Contains(notes, n => n.Code == "cover_foundation_bottom_undetermined");
    }

    [Fact]
    public void Cover_ExposureUnspecified_AddsNote_AndKeepsDiameterRequirement()
    {
        var profile = Profile(h: 0.60, h0: 0.55, aPrime: 0.05,
            tensionDiameter: 0.016, compressionDiameter: 0.016);

        var (details, notes) = Sp63NormalConstructiveReinforcement.CheckCoverAndSpacing(
            profile, exposure: Sp63ExposureCondition.Unspecified);

        var detail = Assert.Single(details, d => d.Description == "Sp63Normal_MinCoverTension");
        Assert.Equal(0.016, detail.Applied, 9);
        Assert.False(detail.Variables.ContainsKey("tableCover"));
        Assert.Contains(notes, n => n.Code == "cover_exposure_unspecified");
    }

    [Fact]
    public void Checker_PassesExposureFromMemberContext()
    {
        var section = Sp63NormalFixtures.Rectangle(0.30, 0.60);
        var rebar = Sp63NormalFixtures.Rebar(2);
        double area = Math.PI * 0.020 * 0.020 / 4.0;
        foreach (double x in new[] { -0.10, 0.10 })
        {
            Sp63NormalFixtures.AddBar(section, x, -0.25, area, rebar, diameter: 0.020);
            Sp63NormalFixtures.AddBar(section, x, 0.25, area, rebar, diameter: 0.020);
        }
        var baseOptions = Sp63NormalFixtures.MemberOptions();
        var options = baseOptions with
        {
            MemberContext = baseOptions.MemberContext with
            {
                ExposureCondition = Sp63ExposureCondition.Outdoor,
                IsPrecast = true
            }
        };

        var result = Sp63NormalChecker.Check(section, new LoadItem { Mx = -50.0 },
            CalcType.C, options);

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Contains(result.ConstructiveChecks, d => d.NormReference == "10.3.2" &&
            Math.Abs(d.Variables["tableCover"] - 0.025) < 1e-9);
    }

    [Theory]
    [InlineData("""{"exposureCondition":"outdoor","isPrecast":true}""",
        Sp63ExposureCondition.Outdoor, true)]
    [InlineData("""{"exposureCondition":"foundation_no_preparation"}""",
        Sp63ExposureCondition.FoundationWithoutPreparation, false)]
    [InlineData("{}", Sp63ExposureCondition.Unspecified, false)]
    public void TaskParams_Exposure_RoundTrips(string json, Sp63ExposureCondition expected,
        bool expectedPrecast)
    {
        var parameters = Sp63NormalTaskParams.Parse(json);
        Assert.True(parameters.TryToOptions(out var options, out var error), error);
        Assert.Equal(expected, options.MemberContext.ExposureCondition);
        Assert.Equal(expectedPrecast, options.MemberContext.IsPrecast);

        var reparsed = Sp63NormalTaskParams.Parse(parameters.ToJson());
        Assert.True(reparsed.TryToOptions(out var again, out _));
        Assert.Equal(expected, again.MemberContext.ExposureCondition);
        Assert.Equal(expectedPrecast, again.MemberContext.IsPrecast);
    }

    [Fact]
    public void TaskParams_UnknownExposure_IsInvalid()
    {
        var parameters = Sp63NormalTaskParams.Parse("""{"exposureCondition":"sea"}""");
        Assert.False(parameters.TryToOptions(out _, out var error));
        Assert.Equal("invalid_exposure_condition", error);
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

    static Sp63MemberContext MemberContext() => new(
        6.0,
        Sp63StructuralScheme.StaticallyIndeterminate,
        4.2,
        Sp63NormalStabilityMode.Member,
        1.0);
}
