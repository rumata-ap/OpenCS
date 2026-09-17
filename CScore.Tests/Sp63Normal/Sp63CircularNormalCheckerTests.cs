using CScore;
using CScore.Sp63;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Сборка результата упрощённой проверки круглых и кольцевых сечений.</summary>
public sealed class Sp63CircularNormalCheckerTests
{
    static Sp63NormalOptions Options(Sp63NormalShapeKind kind,
        Sp63NormalStabilityMode mode = Sp63NormalStabilityMode.SectionOnlyExplicit,
        double? length = 6.0, double? l0 = 4.2, double psi = 0.0) =>
        new(kind, Sp63NormalAxis.Mx, new Sp63MemberContext(length,
            Sp63StructuralScheme.StaticallyIndeterminate, l0, mode, psi));

    static CrossSection Circle(double rs = 340_000.0, int bars = 8,
        Material? concrete = null, Material? rebar = null)
    {
        var section = Sp63NormalFixtures.CircleSection(0.25, concrete: concrete);
        Sp63NormalFixtures.AddPolarBars(section, bars, 0.20, 3.0e-4,
            rebar ?? Sp63NormalFixtures.Rebar(2, rs, rs));
        return section;
    }

    static CrossSection Ring(double rs = 340_000.0)
    {
        var section = Sp63NormalFixtures.RingSection(0.30, 0.20);
        Sp63NormalFixtures.AddPolarBars(section, 8, 0.25, 2.0e-4,
            Sp63NormalFixtures.Rebar(2, rs, rs));
        return section;
    }

    static Sp63NormalResult CheckCircle(LoadItem load, Sp63NormalOptions? options = null,
        CrossSection? section = null, CalcType calc = CalcType.C) =>
        Sp63NormalChecker.Check(section ?? Circle(), load, calc,
            options ?? Options(Sp63NormalShapeKind.Circular));

    static Sp63NormalResult CheckRing(LoadItem load, CrossSection? section = null) =>
        Sp63NormalChecker.Check(section ?? Ring(), load, CalcType.C,
            Options(Sp63NormalShapeKind.Annular));

    static bool HasMessage(Sp63NormalResult result, string code) =>
        result.InformationalMessages.Any(m => m.Code == code);

    [Fact]
    public void Circle_PureBending_UsesD6WithActualGeometry()
    {
        var result = CheckCircle(new LoadItem { N = 0.0, Mx = 100.0 });

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Equal("circular_bending", result.Branch);
        var detail = Assert.Single(result.StrengthDetails);
        Assert.Equal("(Д.6)", detail.Formula);
        Assert.Equal("Д.2", detail.NormReference);
        Assert.Equal("Sp63Normal_CircularCheck", detail.Description);
        Assert.Equal(100.0, detail.Applied, 12);
        var v = result.Variables;
        double expected = Sp63CircularFormulas.Circular(0.0, 14_500.0, 340_000.0,
            v["A"], v["AsTot"], v["r2"], v["rs"]).Mult;
        Assert.Equal(expected, detail.Allowable, 9);
        Assert.Empty(result.ConstructiveChecks);
        Assert.True(HasMessage(result, "appendix_d_recommended"));
        Assert.True(HasMessage(result, "appendix_d_pure_bending_extension"));
        Assert.True(HasMessage(result, "circular_rebar_class_by_rs"));
        Assert.False(HasMessage(result, "resultant_moment_used"));
    }

    [Theory]
    [InlineData(100.0, 0.0)]
    [InlineData(0.0, 100.0)]
    [InlineData(-100.0, 0.0)]
    [InlineData(70.71067811865476, 70.71067811865476)]
    public void Circle_MomentDirection_DoesNotChangeResult(double mx, double my)
    {
        var reference = CheckCircle(new LoadItem { N = 0.0, Mx = 100.0 });
        var result = CheckCircle(new LoadItem { N = 0.0, Mx = mx, My = my });

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Equal(reference.StrengthDetails[0].Allowable,
            result.StrengthDetails[0].Allowable, 9);
        Assert.Equal(100.0, result.StrengthDetails[0].Applied, 9);
        Assert.Equal(mx != 0.0 && my != 0.0, HasMessage(result, "resultant_moment_used"));
    }

    [Fact]
    public void Tension_IsNotApplicable_WithNdmSuggestion()
    {
        var result = CheckCircle(new LoadItem { N = 100.0, Mx = 10.0 });

        Assert.Equal(Sp63NormalStatus.NotApplicable, result.Status);
        Assert.Equal("circular_tension_not_supported",
            Assert.Single(result.ApplicabilityMessages).Code);
        Assert.True(HasMessage(result, "suggest_ndm"));
    }

    [Fact]
    public void Circle_A500_IsNotApplicable_RingWithSameRebarIsCalculated()
    {
        var load = new LoadItem { N = 0.0, Mx = 50.0 };

        var circle = CheckCircle(load, section: Circle(rs: 435_000.0));
        var ring = CheckRing(load, Ring(rs: 435_000.0));

        Assert.Equal(Sp63NormalStatus.NotApplicable, circle.Status);
        Assert.Equal("circular_rebar_class_above_a400",
            Assert.Single(circle.ApplicabilityMessages).Code);
        Assert.True(HasMessage(circle, "suggest_ndm"));
        Assert.Equal(Sp63NormalStatus.Calculated, ring.Status);
    }

    [Theory]
    [InlineData(340_000.0)]
    [InlineData(350_000.0)]
    public void Circle_A400BothEditions_IsCalculated(double rs) =>
        Assert.Equal(Sp63NormalStatus.Calculated,
            CheckCircle(new LoadItem { N = 0.0, Mx = 50.0 }, section: Circle(rs: rs)).Status);

    [Fact]
    public void Circle_NormativeCalc_ClassIsCheckedByDesignCharacteristics()
    {
        var concrete = Sp63NormalFixtures.Concrete(rb: 14_500.0);
        concrete.N = new MaterialChars(CalcType.N)
        {
            Type = MatType.Concrete, Fc = -18_500.0, Ft = 1_550.0, E = 30_000_000.0,
            Ec1Red = -0.0015, Ec2 = -0.0035, Et1Red = 0.00008, Et2 = 0.00015
        };
        var rebar = Sp63NormalFixtures.Rebar(2, 340_000.0, 340_000.0);
        rebar.N = new MaterialChars(CalcType.N)
        {
            Type = MatType.ReSteelU, Ft = 400_000.0, Fc = -400_000.0,
            E = 200_000_000.0, Et2 = 0.025, Ec2 = -0.0035
        };

        var result = CheckCircle(new LoadItem { N = 0.0, Mx = 50.0 },
            section: Circle(concrete: concrete, rebar: rebar), calc: CalcType.N);

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Equal(340_000.0, result.Variables["RsClassCheck"]);
        Assert.Equal(400_000.0, result.Variables["Rs"]);
        Assert.Equal(18_500.0, result.Variables["Rb"]);
    }

    [Fact]
    public void Circle_CentralCompression_UsesAccidentalEccentricity()
    {
        var result = CheckCircle(new LoadItem { N = -800.0 });

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Equal("circular_compression", result.Branch);
        var v = result.Variables;
        double ea = Math.Max(0.01, Math.Max(6.0 / 600.0, v["D"] / 30.0));
        Assert.Equal(ea, v["ea"], 12);
        Assert.Equal(800.0 * ea, v["M"], 9);
        Assert.Equal(800.0 * ea, result.StrengthDetails[0].Applied, 9);
        Assert.True(HasMessage(result, "accidental_eccentricity"));
        Assert.True(HasMessage(result, "stability_excluded_explicitly"));
        Assert.False(HasMessage(result, "appendix_d_pure_bending_extension"));
    }

    [Fact]
    public void Circle_SlenderCompression_AmplifiesMomentByEta()
    {
        var result = CheckCircle(new LoadItem { N = -800.0, Mx = 40.0 },
            Options(Sp63NormalShapeKind.Circular, Sp63NormalStabilityMode.Member,
                length: 6.0, l0: 12.0, psi: 0.0));

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.NotNull(result.Eta);
        var v = result.Variables;
        Assert.True(v["eta"] > 1.0);
        Assert.Equal(0.05, v["e0"], 12);
        Assert.Equal(v["eta"] * 800.0 * 0.05, v["M"], 9);
        Assert.Equal("(Д.6)", result.StrengthDetails[0].Formula);
    }

    [Fact]
    public void Circle_VerySlenderCompression_ReportsInstability()
    {
        var result = CheckCircle(new LoadItem { N = -800.0, Mx = 40.0 },
            Options(Sp63NormalShapeKind.Circular, Sp63NormalStabilityMode.Member,
                length: 6.0, l0: 60.0, psi: 1.0));

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.False(result.StrengthPassed);
        Assert.Equal("circular_compression", result.Branch);
        Assert.Equal("(8.15)", Assert.Single(result.StrengthDetails).Formula);
        Assert.Contains(result.InformationalMessages, m => m.Code == "unstable_element");
    }

    [Fact]
    public void Compression_WithoutLength_IsNotApplicable()
    {
        var result = CheckCircle(new LoadItem { N = -800.0, Mx = 40.0 },
            Options(Sp63NormalShapeKind.Circular, length: null));

        Assert.Equal("missing_accidental_eccentricity_length",
            Assert.Single(result.ApplicabilityMessages).Code);
    }

    [Fact]
    public void Circle_Overload_IsCalculatedAndFailed()
    {
        var result = CheckCircle(new LoadItem { N = -4500.0, Mx = 10.0 });

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.False(result.StrengthPassed);
        Assert.Equal(0.0, result.StrengthDetails[0].Allowable);
        Assert.Contains(result.InformationalMessages,
            m => m.Code == "compression_exceeds_section_capacity" &&
                 m.Kind == Sp63NormalMessageKind.Warning);
    }

    [Fact]
    public void Ring_ModerateCompression_UsesBranchA()
    {
        var result = CheckRing(new LoadItem { N = -500.0, Mx = 50.0 });

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Equal("annular_compression", result.Branch);
        var detail = result.StrengthDetails[0];
        Assert.Equal("(Д.2)", detail.Formula);
        Assert.Equal("Д.1", detail.NormReference);
        Assert.Equal("Sp63Normal_AnnularCheck", detail.Description);
        Assert.Equal(1.0, result.Variables["annularBranch"]);
        Assert.False(HasMessage(result, "circular_rebar_class_by_rs"));
    }

    [Fact]
    public void Ring_OverloadWithXi2AboveTwo_IsFailedNotFalselyPassed()
    {
        var result = CheckRing(new LoadItem { N = -6000.0, Mx = 10.0 });

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.True(result.Variables["xiCir2"] > 2.0);
        Assert.False(result.StrengthPassed);
        Assert.Equal(0.0, result.StrengthDetails[0].Allowable);
    }

    [Fact]
    public void Ring_PureBending_UsesBranchB()
    {
        var result = CheckRing(new LoadItem { N = 0.0, Mx = 50.0 });

        Assert.Equal("annular_bending", result.Branch);
        Assert.Equal("(Д.3)", result.StrengthDetails[0].Formula);
        Assert.Equal(2.0, result.Variables["annularBranch"]);
    }

    [Fact]
    public void SixBars_AreNotApplicable() =>
        Assert.Equal("circular_insufficient_bars", Assert.Single(
            CheckCircle(new LoadItem { Mx = 50.0 }, section: Circle(bars: 6))
                .ApplicabilityMessages).Code);

    [Fact]
    public void RectangleWithCircularShape_IsNotApplicable()
    {
        var section = Sp63NormalFixtures.TwoLayerRectangle(0.3, 0.6, 0.001, 0.001);
        var result = CheckCircle(new LoadItem { Mx = 50.0 }, section: section);

        Assert.Equal(Sp63NormalStatus.NotApplicable, result.Status);
        Assert.Equal("unsupported_geometry", Assert.Single(result.ApplicabilityMessages).Code);
    }

    [Fact]
    public void ZeroLoad_IsNotApplicable() =>
        Assert.Equal("zero_load", Assert.Single(
            CheckCircle(new LoadItem()).ApplicabilityMessages).Code);
}
