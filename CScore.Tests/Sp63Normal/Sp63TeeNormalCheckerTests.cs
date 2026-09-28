using CScore;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Проверки выбора ветви и вердикта таврового нормального сечения.</summary>
public sealed class Sp63TeeNormalCheckerTests
{
    const double SpanLength = 6.3;

    [Fact]
    public void CaseA_NeutralAxisInFlange_UsesFlangeFormulas()
    {
        var result = Check(load: new LoadItem { N = 0.0, Mx = 1000.0 },
            tensionArea: 0.012, compressionArea: 1e-12);

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Equal("bending", result.Branch);
        Assert.Equal(3017.16, result.StrengthDetails.Single().Allowable, precision: 2);
        Assert.Equal(1.0, result.Variables["hasCompressionFlange"]);
        Assert.Equal(1.0, result.Variables["neutralAxisInFlange"]);
        Assert.Equal(2.5, result.Variables["compressionFlangeEffectiveWidth"], precision: 12);
    }

    [Fact]
    public void CaseB_NeutralAxisInWeb_UsesEquation88()
    {
        var result = Check(load: new LoadItem { N = 0.0, Mx = 1000.0 },
            tensionArea: 0.02, compressionArea: 0.002);

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Equal("(8.8)", result.StrengthDetails.Single().Formula);
        Assert.Equal(4741.5, result.StrengthDetails.Single().Allowable, precision: 2);
        Assert.Equal(0.0, result.Variables["neutralAxisInFlange"]);
    }

    [Fact]
    public void SymmetricBranch_UsesX0WithoutCompressionRebar()
    {
        var result = Check(load: new LoadItem { N = 0.0, Mx = 1000.0 },
            tensionArea: 0.005, compressionArea: 0.005);

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Equal("(8.9)", result.StrengthDetails.Single().Formula);
        Assert.Equal(1.0, result.Variables["symmetricBranchApplied"]);
        Assert.Equal(0.06, result.Variables["symmetricXWithoutCompressionRebar"],
            precision: 12);
        Assert.Equal(1348.5, result.StrengthDetails.Single().Allowable, precision: 2);
    }

    [Fact]
    public void NonSymmetricRebarWithSmallX_StillUsesSymmetricBranch()
    {
        var result = Check(load: new LoadItem { N = 0.0, Mx = 1000.0 },
            tensionArea: 0.0055, compressionArea: 0.005);

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Equal(1.0, result.Variables["symmetricBranchApplied"]);
    }

    [Fact]
    public void TensionOnFlangeSide_IgnoresFlangeAndUsesWebOnly()
    {
        var section = Sp63NormalFixtures.Tee(0.4, 0.8, 2.5, 0.2, flangeOnTop: true,
            tensionY: 0.25, compressionY: -0.35,
            tensionArea: 0.004, compressionArea: 0.002);

        var result = Sp63NormalCheckerFor(section, new LoadItem { N = 0.0, Mx = 1000.0 },
            spanLength: null);

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Equal(0.0, result.Variables["hasCompressionFlange"]);
        Assert.Equal(0.4, result.Variables["compressionFlangeEffectiveWidth"]);
        double x = Sp63NormalFormulas.BendingX(435_000, 0.004, 435_000, 0.002,
            14_500, 0.4);
        double expected = Sp63NormalFormulas.BendingMoment(14_500, 0.4, x, 0.65,
            435_000, 0.002, 0.05);
        Assert.Equal(expected, result.StrengthDetails.Single().Allowable, precision: 6);
    }

    [Fact]
    public void CompressionFlangeWithoutSpanLength_IsNotApplicable()
    {
        var result = Check(load: new LoadItem { N = 0.0, Mx = 1000.0 },
            tensionArea: 0.012, compressionArea: 1e-12, spanLength: null);

        Assert.Equal(Sp63NormalStatus.NotApplicable, result.Status);
        Assert.Contains(result.ApplicabilityMessages,
            message => message.Code == "missing_span_length");
    }

    [Fact]
    public void AxialForceAboveTolerance_IsUnsupportedForTee()
    {
        var result = Check(load: new LoadItem { N = 1e-6, Mx = 1000.0 },
            tensionArea: 0.012, compressionArea: 1e-12, spanLength: null);

        Assert.Equal(Sp63NormalStatus.NotApplicable, result.Status);
        Assert.Contains(result.ApplicabilityMessages,
            message => message.Code == "unsupported_load_case_for_tee");
        Assert.Contains(result.InformationalMessages,
            message => message.Code == "suggest_ndm");
    }

    [Fact]
    public void AxialForceExactlyAtTolerance_RemainsPureBending()
    {
        var result = Check(load: new LoadItem { N = 1e-9, Mx = 1000.0 },
            tensionArea: 0.012, compressionArea: 1e-12);

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
    }

    [Fact]
    public void ZeroAndSmallMoment_AreZeroLoad()
    {
        var zero = Check(load: new LoadItem { N = 0.0, Mx = 0.0 },
            tensionArea: 0.012, compressionArea: 1e-12, spanLength: null);
        var small = Check(load: new LoadItem { N = 0.0, Mx = 1e-10 },
            tensionArea: 0.012, compressionArea: 1e-12, spanLength: null);

        Assert.Equal(Sp63NormalStatus.NotApplicable, zero.Status);
        Assert.Contains(zero.ApplicabilityMessages,
            message => message.Code == "zero_load");
        Assert.Contains(small.ApplicabilityMessages,
            message => message.Code == "zero_load");
    }

    [Fact]
    public void NonZeroOtherMoment_IsBiaxial()
    {
        var result = Check(load: new LoadItem { N = 0.0, Mx = 1000.0, My = 1.0 },
            tensionArea: 0.012, compressionArea: 1e-12, spanLength: null);

        Assert.Equal(Sp63NormalStatus.NotApplicable, result.Status);
        Assert.Contains(result.ApplicabilityMessages,
            message => message.Code == "biaxial_load");
    }

    [Fact]
    public void MinReinforcement_ForTee_UsesWebWidthTimesH0()
    {
        var result = Check(load: new LoadItem { N = 0.0, Mx = 1000.0 },
            tensionArea: 0.012, compressionArea: 1e-12);

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        // Сжатая арматура 1e-12 м² — ниже допуска, отдельной проверки нет.
        var detail = Assert.Single(result.ConstructiveChecks,
            check => check.NormReference == "10.3.6");
        Assert.Equal("Sp63Normal_MinReinforcementTension", detail.Description);
        Assert.Equal("10.3.6", detail.NormReference);
        Assert.Equal(0.1, detail.Applied, precision: 12);
        double baseArea = result.Variables["bw"] * result.Variables["h0"];
        Assert.Equal(baseArea, detail.Variables["baseArea"], precision: 12);
        Assert.Equal(0.012 / baseArea * 100.0, detail.Allowable, precision: 9);
        Assert.True(detail.Passed);
    }

    [Fact]
    public void MinReinforcement_ForTee_ChecksCompressionRebarWhenPresent()
    {
        var result = Check(load: new LoadItem { N = 0.0, Mx = 100.0 },
            tensionArea: 0.012, compressionArea: 0.0002);

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Equal(2, result.ConstructiveChecks.Count(check => check.NormReference == "10.3.6"));
        var compression = Assert.Single(result.ConstructiveChecks,
            detail => detail.Description == "Sp63Normal_MinReinforcementCompression");
        // 0,0002 / (0,4·h0) < 0,1 % — справочная проверка не выполнена, прочность не затронута.
        Assert.False(compression.Passed);
        Assert.True(result.StrengthPassed);
    }

    [Fact]
    public void Cover_ForTee_MeasuredFromFacesAlongHeight()
    {
        // h = 0,8; растянутый слой y = 0,25 → до грани 0,15; сжатый y = −0,35 → 0,05.
        // Стержни ∅16: факт. слой 0,142 и 0,042 м; требуемый (без табл. 10.1) — 0,016 м.
        var result = Check(load: new LoadItem { N = 0.0, Mx = 1000.0 },
            tensionArea: 0.012, compressionArea: 0.002);

        var tension = Assert.Single(result.ConstructiveChecks,
            check => check.Description == "Sp63Normal_MinCoverTension");
        Assert.Equal(0.016, tension.Applied, precision: 12);
        Assert.Equal(0.142, tension.Allowable, precision: 9);
        var compression = Assert.Single(result.ConstructiveChecks,
            check => check.Description == "Sp63Normal_MinCoverCompression");
        Assert.Equal(0.042, compression.Allowable, precision: 9);
    }

    [Fact]
    public void Cover_ForTee_UsesTable101_WhenExposureGiven()
    {
        var result = Check(load: new LoadItem { N = 0.0, Mx = 1000.0 },
            tensionArea: 0.012, compressionArea: 0.002,
            context: Sp63NormalFixtures.MemberOptions().MemberContext with
            {
                ExposureCondition = Sp63ExposureCondition.Outdoor
            });

        var compression = Assert.Single(result.ConstructiveChecks,
            check => check.Description == "Sp63Normal_MinCoverCompression");
        Assert.Equal(0.030, compression.Applied, precision: 12);
        Assert.True(compression.Passed);
    }

    [Fact]
    public void BarSpacing_ForTeeBeam_ChecksClearAndMaxSpacing()
    {
        // Стержни слоя на x = ±0,1 → шаг осей 0,2 м, зазор 0,2 − 0,016 = 0,184 м.
        var result = Check(load: new LoadItem { N = 0.0, Mx = 1000.0 },
            tensionArea: 0.012, compressionArea: 0.002,
            context: Sp63NormalFixtures.MemberOptions().MemberContext with
            {
                ElementKind = Sp63ElementKind.BeamOrSlab
            });

        var maxSpacing = Assert.Single(result.ConstructiveChecks,
            check => check.Description == "Sp63Normal_MaxBarSpacingTension");
        Assert.Equal(0.2, maxSpacing.Applied, precision: 9);
        Assert.Equal(0.4, maxSpacing.Allowable, precision: 12); // min(1,5h; 400 мм)
        var clear = Assert.Single(result.ConstructiveChecks,
            check => check.Description == "Sp63Normal_MinClearSpacingTension");
        Assert.Equal(0.184, clear.Allowable, precision: 9);
        Assert.True(clear.Passed);
    }

    [Fact]
    public void BarSpacing_ForTeeColumn_ChecksLevelSpacingInPlane()
    {
        var result = Check(load: new LoadItem { N = 0.0, Mx = 1000.0 },
            tensionArea: 0.012, compressionArea: 0.002,
            context: Sp63NormalFixtures.MemberOptions().MemberContext with
            {
                ElementKind = Sp63ElementKind.Column
            });

        var levels = Assert.Single(result.ConstructiveChecks,
            check => check.Description == "Sp63Normal_MaxLevelSpacingColumn");
        Assert.Equal(0.6, levels.Applied, precision: 9);
        Assert.False(levels.Passed); // 600 мм > 500 мм
    }

    [Theory]
    [InlineData(false, 0.25, 0.4)] // растянутые стержни в стенке → bw
    [InlineData(true, 0.35, 2.5)]  // растянутые стержни в растянутой полке → bf
    public void TensionBarCount_ForTee_UsesWidthAtTensionLayer(bool flangeOnTop,
        double tensionY, double expectedWidth)
    {
        var section = Sp63NormalFixtures.Tee(0.4, 0.8, 2.5, 0.2, flangeOnTop,
            tensionY: tensionY, compressionY: -0.35,
            tensionArea: 0.012, compressionArea: 0.002);
        var result = Sp63NormalCheckerFor(section, new LoadItem { N = 0.0, Mx = 100.0 },
            SpanLength);

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        var count = Assert.Single(result.ConstructiveChecks,
            check => check.Description == "Sp63Normal_MinTensionBarCount");
        Assert.Equal(expectedWidth, count.Variables["b"], precision: 12);
        Assert.True(count.Passed);
    }

    [Fact]
    public void SideCover_ForTee_MeasuredToWebFaces()
    {
        // bw = 0,4; стержни ∅16 на x = ±0,1 → до грани ребра 0,1 − 0,008 = 0,092 м.
        var result = Check(load: new LoadItem { N = 0.0, Mx = 1000.0 },
            tensionArea: 0.012, compressionArea: 0.002);

        var side = Assert.Single(result.ConstructiveChecks,
            check => check.Description == "Sp63Normal_MinCoverSide");
        Assert.Equal(0.092, side.Allowable, precision: 9);
        Assert.True(side.Passed);
    }

    [Fact]
    public void SideCover_ForTee_BarInFlangeOverhang_MeasuredToOverhangSurface()
    {
        // Нижняя полка y ∈ [−0,4; −0,2]; стержень в свесе на (0,8; −0,23) —
        // до верхней поверхности свеса 0,03 м, слой 0,03 − 0,008 = 0,022 м.
        var section = Sp63NormalFixtures.Tee(0.4, 0.8, 2.5, 0.2, flangeOnTop: false,
            tensionY: 0.25, compressionY: -0.35, tensionArea: 0.012, compressionArea: 0.002);
        Sp63NormalFixtures.AddBar(section, 0.8, -0.23, 0.0002, Sp63NormalFixtures.Rebar(2));

        var result = Sp63NormalCheckerFor(section, new LoadItem { N = 0.0, Mx = 1000.0 },
            SpanLength);

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        var side = Assert.Single(result.ConstructiveChecks,
            check => check.Description == "Sp63Normal_MinCoverSide");
        Assert.Equal(0.022, side.Allowable, precision: 9);
    }

    [Fact]
    public void RectangleSection_IsNotATeeShape()
    {
        var section = Sp63NormalFixtures.TwoLayerRectangle(0.3, 0.6, 0.001, 0.001);
        var result = Sp63NormalCheckerFor(section,
            new LoadItem { N = 0.0, Mx = 1000.0 }, spanLength: null);

        Assert.Equal(Sp63NormalStatus.NotApplicable, result.Status);
        Assert.Contains(result.ApplicabilityMessages,
            message => message.Code == "not_a_tee_shape");
    }

    [Fact]
    public void SharedChecker_DelegatesTeeToTeeChecker()
    {
        var result = Sp63NormalChecker.Check(
            Sp63NormalFixtures.Tee(0.4, 0.8, 2.5, 0.2, flangeOnTop: false,
                tensionY: 0.25, compressionY: -0.35,
                tensionArea: 0.012, compressionArea: 1e-12),
            new LoadItem { N = 0.0, Mx = 1000.0 }, CalcType.C,
            Sp63NormalFixtures.MemberOptions() with
            {
                ShapeKind = Sp63NormalShapeKind.Tee
            }, spanLength: SpanLength);

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Equal("bending", result.Branch);
    }

    [Fact]
    public void SharedChecker_TeeWithoutSpanLength_ReportsMissingSpanLength()
    {
        var result = Sp63NormalChecker.Check(
            Sp63NormalFixtures.Tee(0.4, 0.8, 2.5, 0.2, flangeOnTop: false,
                tensionY: 0.25, compressionY: -0.35,
                tensionArea: 0.012, compressionArea: 1e-12),
            new LoadItem { N = 0.0, Mx = 1000.0 }, CalcType.C,
            Sp63NormalFixtures.MemberOptions() with
            {
                ShapeKind = Sp63NormalShapeKind.Tee
            });

        Assert.Equal(Sp63NormalStatus.NotApplicable, result.Status);
        Assert.Contains(result.ApplicabilityMessages,
            message => message.Code == "missing_span_length");
    }

    static Sp63NormalResult Check(LoadItem load, double tensionArea,
        double compressionArea, double? spanLength = SpanLength,
        Sp63MemberContext? context = null) =>
        Sp63NormalCheckerFor(Sp63NormalFixtures.Tee(0.4, 0.8, 2.5, 0.2,
            flangeOnTop: false, tensionY: 0.25, compressionY: -0.35,
            tensionArea: tensionArea, compressionArea: compressionArea), load,
            spanLength, context);

    static Sp63NormalResult Sp63NormalCheckerFor(CrossSection section, LoadItem load,
        double? spanLength, Sp63MemberContext? context = null)
    {
        var options = Sp63NormalFixtures.MemberOptions() with
        {
            ShapeKind = Sp63NormalShapeKind.Tee
        };
        if (context is not null)
            options = options with { MemberContext = context };
        return Sp63TeeNormalChecker.Check(section, load, CalcType.C, options, spanLength);
    }
}
