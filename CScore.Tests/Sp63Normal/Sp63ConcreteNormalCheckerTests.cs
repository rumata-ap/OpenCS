using CScore;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Проверки бетонных (без рабочей арматуры) элементов по разделу 7 СП 63.</summary>
public sealed class Sp63ConcreteNormalCheckerTests
{
    static Sp63NormalOptions ConcreteOptions(double length, double? l0,
        Sp63NormalStabilityMode mode = Sp63NormalStabilityMode.Member, double psi = 0.0,
        Sp63NormalShapeKind shape = Sp63NormalShapeKind.Rectangular) => new(
        shape,
        Sp63NormalAxis.Mx,
        new Sp63MemberContext(length, Sp63StructuralScheme.StaticallyIndeterminate, l0,
            mode, psi),
        Sp63NormalElementType.Concrete);

    /// <summary>Панель примера 1 Пособия: b = 1 м, h = 0,15 м, бетон B15 (Eb = 24 000 МПа).</summary>
    static CrossSection HandbookPanel(double rbC, double rbCl)
    {
        var concrete = Sp63NormalFixtures.Concrete(rb: rbC);
        concrete.E = 24_000_000.0;
        concrete.C!.E = 24_000_000.0;
        concrete.CL = new MaterialChars(CalcType.CL)
        {
            Type = MatType.Concrete,
            Fc = -rbCl,
            Ft = 675.0,
            E = 24_000_000.0,
            Ec2 = -0.0035
        };
        var section = new CrossSection { Tag = "panel" };
        section.Areas.Add(Sp63NormalFixtures.ConcreteRegion(concrete,
            [(-0.5, -0.075), (0.5, -0.075), (0.5, 0.075), (-0.5, 0.075)]));
        return section;
    }

    [Fact]
    public void HandbookExample1_FullLoad_MatchesManual()
    {
        // Пособие к СП 63, пример 1: N = 700 кН, H = l0 = 2,7 м, e0 = ea = 10 мм,
        // φl = 1 + 0,93 = 1,93 (как в книге), η = 1,797, Rb·γb3 = 7,65 МПа, Rb·Ab = 872,6 кН > 700.
        var section = HandbookPanel(rbC: 8_500.0, rbCl: 7_650.0);
        var result = Sp63NormalChecker.Check(section, new LoadItem { N = -700.0 },
            CalcType.C, ConcreteOptions(2.7, 2.7, psi: 0.93));

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Equal("concrete_compression", result.Branch);
        Assert.True(result.StrengthPassed);
        Assert.Equal(0.010, result.Variables["e0"], 9);
        Assert.Equal(1578.3, result.Variables["etaNcr"], 0);
        Assert.Equal(1.797, result.Variables["eta"], 3);
        Assert.Equal(7_650.0, result.Variables["Rb"], 6);
        var detail = Assert.Single(result.StrengthDetails);
        Assert.Equal("(7.1)", detail.Formula);
        Assert.Equal(872.6, detail.Allowable, 0);
        Assert.Contains(result.InformationalMessages, m => m.Code == "concrete_gamma_b3");
    }

    [Fact]
    public void HandbookExample1_LongTermLoad_MatchesManual()
    {
        // Та же панель на Nl = 650 кН: φl = 2, η = 1,745, Rb = 6,89 МПа (γb1·γb3),
        // Rb·Ab = 793 кН > 650.
        var section = HandbookPanel(rbC: 8_500.0, rbCl: 7_650.0);
        var result = Sp63NormalChecker.Check(section, new LoadItem { N = -650.0 },
            CalcType.CL, ConcreteOptions(2.7, 2.7, psi: 1.0));

        Assert.True(result.StrengthPassed);
        // Пособие округляет Ncr до 1523,4 кН; точное значение даёт η = 1,7445.
        Assert.InRange(result.Variables["eta"], 1.743, 1.746);
        Assert.Equal(793.0, result.StrengthDetails[0].Allowable, 0);
    }

    [Fact]
    public void Bending_UsesRbtTimesElasticModulus()
    {
        // W = 0,3·0,5²/6 = 0,0125 м³; Mult = 1000·0,0125 = 12,5 кН·м.
        var section = Sp63NormalFixtures.Rectangle(0.3, 0.5);
        var passed = Sp63NormalChecker.Check(section, new LoadItem { Mx = -5.0 },
            CalcType.C, ConcreteOptions(3.0, 3.0));
        var failed = Sp63NormalChecker.Check(section, new LoadItem { Mx = 20.0 },
            CalcType.C, ConcreteOptions(3.0, 3.0));

        Assert.Equal("concrete_bending", passed.Branch);
        Assert.Equal("(7.8)", passed.StrengthDetails[0].Formula);
        Assert.Equal(12.5, passed.StrengthDetails[0].Allowable, 9);
        Assert.True(passed.StrengthPassed);
        Assert.False(failed.StrengthPassed);
    }

    [Fact]
    public void ForceOutsideSection_UsesTensileZoneFormula()
    {
        // e0 = 0,4 м > h/2: (7.5) Nult = 1000·0,3·0,5/(6·0,4/0,5 − 1) = 150/3,8.
        var section = Sp63NormalFixtures.Rectangle(0.3, 0.5);
        var result = Sp63NormalChecker.Check(section, new LoadItem { N = -100.0, Mx = 40.0 },
            CalcType.C, ConcreteOptions(3.0, null, Sp63NormalStabilityMode.SectionOnlyExplicit));

        Assert.Equal("concrete_compression_outside", result.Branch);
        var detail = Assert.Single(result.StrengthDetails);
        Assert.Equal("(7.5)", detail.Formula);
        Assert.Equal(150.0 / 3.8, detail.Allowable, 9);
        Assert.False(result.StrengthPassed);
    }

    [Fact]
    public void SmallEccentricity_AddsReferenceCheck73()
    {
        // e0 = ea = h/30, l0/h = 6 → φ = 0,92 (кратковременно); N ≤ φ·Rb·A справочно.
        var section = Sp63NormalFixtures.Rectangle(0.3, 0.5);
        var result = Sp63NormalChecker.Check(section, new LoadItem { N = -1000.0 },
            CalcType.C, ConcreteOptions(3.0, 3.0));

        var alt = Assert.Single(result.AlternativeChecks);
        Assert.Equal("(7.3)", alt.Formula);
        Assert.Equal(0.92, alt.Variables["phi"], 9);
        Assert.Equal(0.92 * 18_000.0 * 0.15, alt.Allowable, 6);
    }

    [Theory]
    [InlineData(4.0, true, 0.92)]
    [InlineData(12.5, true, 0.85)]
    [InlineData(20.0, true, 0.6)]
    [InlineData(10.0, false, 0.9)]
    [InlineData(15.0, false, 0.875)]
    [InlineData(20.0, false, 0.85)]
    public void Phi_FollowsTable71AndShortTermLine(double l0OverH, bool longTerm, double expected) =>
        Assert.Equal(expected, Sp63ConcreteNormalChecker.Phi(l0OverH, longTerm), 9);

    [Fact]
    public void Tension_IsNotApplicable()
    {
        var result = Sp63NormalChecker.Check(Sp63NormalFixtures.Rectangle(0.3, 0.5),
            new LoadItem { N = 10.0 }, CalcType.C, ConcreteOptions(3.0, 3.0));

        Assert.Equal(Sp63NormalStatus.NotApplicable, result.Status);
        Assert.Equal("concrete_tension_not_supported", result.ApplicabilityMessages[0].Code);
    }

    [Fact]
    public void NonRectangularShape_IsNotApplicable()
    {
        var result = Sp63NormalChecker.Check(Sp63NormalFixtures.Rectangle(0.3, 0.5),
            new LoadItem { N = -10.0 }, CalcType.C,
            ConcreteOptions(3.0, 3.0, shape: Sp63NormalShapeKind.Tee));

        Assert.Equal("concrete_shape_not_supported", result.ApplicabilityMessages[0].Code);
    }

    [Fact]
    public void RebarInSection_IsIgnoredWithNote()
    {
        var plain = Sp63NormalFixtures.Rectangle(0.3, 0.5);
        var withRebar = Sp63NormalFixtures.Rectangle(0.3, 0.5);
        Sp63NormalFixtures.AddBar(withRebar, 0.0, -0.2, 0.000201, Sp63NormalFixtures.Rebar(2));
        var load = new LoadItem { N = -1000.0, Mx = 30.0 };
        var options = ConcreteOptions(3.0, 3.0, Sp63NormalStabilityMode.SectionOnlyExplicit);

        var a = Sp63NormalChecker.Check(plain, load, CalcType.C, options);
        var b = Sp63NormalChecker.Check(withRebar, load, CalcType.C, options);

        Assert.Equal(a.StrengthDetails[0].Allowable, b.StrengthDetails[0].Allowable, 9);
        Assert.Contains(b.InformationalMessages, m => m.Code == "concrete_rebar_ignored");
        Assert.DoesNotContain(a.InformationalMessages, m => m.Code == "concrete_rebar_ignored");
    }

    [Fact]
    public void ReinforcedMode_WithoutRebar_StaysNotApplicable()
    {
        var result = Sp63NormalChecker.Check(Sp63NormalFixtures.Rectangle(0.3, 0.5),
            new LoadItem { N = -1000.0 }, CalcType.C, Sp63NormalFixtures.MemberOptions());

        Assert.Equal(Sp63NormalStatus.NotApplicable, result.Status);
    }

    [Theory]
    [InlineData(null, Sp63NormalElementType.Reinforced)]
    [InlineData("", Sp63NormalElementType.Reinforced)]
    [InlineData("reinforced", Sp63NormalElementType.Reinforced)]
    [InlineData("concrete", Sp63NormalElementType.Concrete)]
    public void TaskParams_ParseElementType(string? value, Sp63NormalElementType expected)
    {
        var parameters = new Sp63NormalTaskParams { ElementType = value! };
        Assert.True(parameters.TryToOptions(out var options, out _));
        Assert.Equal(expected, options.ElementType);
    }

    [Fact]
    public void TaskParams_UnknownElementType_IsInvalid()
    {
        var parameters = new Sp63NormalTaskParams { ElementType = "plain" };
        Assert.False(parameters.TryToOptions(out _, out var code));
        Assert.Equal("invalid_element_type", code);
    }

    [Fact]
    public void TaskParams_LegacyJsonWithoutField_IsReinforced()
    {
        var parameters = Sp63NormalTaskParams.Parse("{\"shapeKind\":\"rectangular\",\"axis\":\"Mx\"}");
        Assert.True(parameters.TryToOptions(out var options, out _));
        Assert.Equal(Sp63NormalElementType.Reinforced, options.ElementType);
    }
}
