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
    public void NoConstructiveChecks_ForTee()
    {
        var result = Check(load: new LoadItem { N = 0.0, Mx = 1000.0 },
            tensionArea: 0.012, compressionArea: 1e-12);

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Empty(result.ConstructiveChecks);
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
        double compressionArea, double? spanLength = SpanLength) =>
        Sp63NormalCheckerFor(Sp63NormalFixtures.Tee(0.4, 0.8, 2.5, 0.2,
            flangeOnTop: false, tensionY: 0.25, compressionY: -0.35,
            tensionArea: tensionArea, compressionArea: compressionArea), load,
            spanLength);

    static Sp63NormalResult Sp63NormalCheckerFor(CrossSection section, LoadItem load,
        double? spanLength) => Sp63TeeNormalChecker.Check(section, load, CalcType.C,
        Sp63NormalFixtures.MemberOptions() with
        {
            ShapeKind = Sp63NormalShapeKind.Tee
        }, spanLength);
}
