using CScore;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>
/// Расстояния между стержнями по пп. 10.3.5 (минимальный зазор в свету) и 10.3.8
/// (наибольший шаг) в зависимости от типа элемента.
/// </summary>
public sealed class Sp63NormalBarSpacingTests
{
    [Fact]
    public void Beam_BottomAndTopLayers_UseDifferentAbsoluteMinimum()
    {
        // 300×600, низ 3⌀20 с шагом 100 мм, верх 2⌀16 с шагом 200 мм.
        var section = Section(0.30, 0.60,
            Layer(-0.25, 0.020, -0.10, 0.0, 0.10),
            Layer(0.25, 0.016, -0.10, 0.10));

        var details = Spacing(section, Sp63ElementKind.BeamOrSlab, tensionDirection: -1);

        var bottomMin = Assert.Single(details, d => d.Description == "Sp63Normal_MinClearSpacingTension");
        Assert.Equal(0.025, bottomMin.Variables["absoluteMinimum"], 9);
        Assert.Equal(0.025, bottomMin.Applied, 9);          // max(d = 20, 25) мм
        Assert.Equal(0.080, bottomMin.Allowable, 9);        // 100 − 20 мм
        Assert.True(bottomMin.Passed);
        var topMin = Assert.Single(details, d => d.Description == "Sp63Normal_MinClearSpacingCompression");
        Assert.Equal(0.030, topMin.Variables["absoluteMinimum"], 9);
        Assert.Equal(0.184, topMin.Allowable, 9);
        var bottomMax = Assert.Single(details, d => d.Description == "Sp63Normal_MaxBarSpacingTension");
        Assert.Equal(0.400, bottomMax.Allowable, 9);        // min(1,5h = 900; 400) мм
        Assert.Equal(0.100, bottomMax.Applied, 9);
        Assert.True(bottomMax.Passed);
        Assert.DoesNotContain(details, d => d.Description == "Sp63Normal_MaxLevelSpacingColumn");
    }

    [Fact]
    public void Beam_ClearSpacingBelowBarDiameter_Fails()
    {
        // ⌀32 с шагом 50 мм: зазор 18 мм < d = 32 мм.
        var section = Section(0.30, 0.60,
            Layer(-0.25, 0.032, -0.05, 0.0, 0.05),
            Layer(0.25, 0.016, -0.10, 0.10));

        var details = Spacing(section, Sp63ElementKind.BeamOrSlab, tensionDirection: -1);

        var detail = Assert.Single(details, d => d.Description == "Sp63Normal_MinClearSpacingTension");
        Assert.Equal(0.032, detail.Applied, 9);
        Assert.Equal(0.018, detail.Allowable, 9);
        Assert.False(detail.Passed);
    }

    [Fact]
    public void Slab_ThinSection_MaxSpacing200mm_Fails()
    {
        // Полоса плиты 1000×120: h ≤ 150 мм → шаг не более 200 мм; фактический 800 мм.
        var section = Section(1.00, 0.12,
            Layer(-0.035, 0.012, -0.40, 0.40),
            Layer(0.035, 0.012, -0.40, 0.40));

        var details = Spacing(section, Sp63ElementKind.BeamOrSlab, tensionDirection: -1);

        var detail = Assert.Single(details, d => d.Description == "Sp63Normal_MaxBarSpacingTension");
        Assert.Equal(0.200, detail.Allowable, 9);
        Assert.Equal(0.800, detail.Applied, 9);
        Assert.False(detail.Passed);
    }

    [Fact]
    public void Column_Uses50mmClearAnd400mmAcross()
    {
        // Колонна 400×400, ⌀20 с шагом 60 мм: зазор 40 мм < 50 мм.
        var section = Section(0.40, 0.40,
            Layer(-0.15, 0.020, -0.06, 0.0, 0.06),
            Layer(0.15, 0.020, -0.06, 0.0, 0.06));

        var details = Spacing(section, Sp63ElementKind.Column, tensionDirection: -1);

        var min = Assert.Single(details, d => d.Description == "Sp63Normal_MinClearSpacingTension");
        Assert.Equal(0.050, min.Applied, 9);
        Assert.False(min.Passed);
        var across = Assert.Single(details, d => d.Description == "Sp63Normal_MaxBarSpacingTension");
        Assert.Equal(0.400, across.Allowable, 9);
        var inPlane = Assert.Single(details, d => d.Description == "Sp63Normal_MaxLevelSpacingColumn");
        Assert.Equal(0.300, inPlane.Applied, 9);
        Assert.Equal(0.500, inPlane.Allowable, 9);
        Assert.True(inPlane.Passed);
    }

    [Fact]
    public void Column_InPlaneSpacing_CountsIntermediateLevels()
    {
        // 400×1200: крайние уровни ±550 мм (1100 мм > 500), промежуточный уровень 0.
        var withoutMiddle = Section(0.40, 1.20,
            Layer(-0.55, 0.020, -0.15, 0.15),
            Layer(0.55, 0.020, -0.15, 0.15));
        var withMiddle = Section(0.40, 1.20,
            Layer(-0.55, 0.020, -0.15, 0.15),
            Layer(0.0, 0.020, -0.15, 0.15),
            Layer(0.55, 0.020, -0.15, 0.15));

        var failing = Assert.Single(Spacing(withoutMiddle, Sp63ElementKind.Column, -1),
            d => d.Description == "Sp63Normal_MaxLevelSpacingColumn");
        var passing = Assert.Single(Spacing(withMiddle, Sp63ElementKind.Column, -1),
            d => d.Description == "Sp63Normal_MaxLevelSpacingColumn");

        Assert.Equal(1.10, failing.Applied, 9);
        Assert.False(failing.Passed);
        Assert.Equal(0.55, passing.Applied, 9);
        Assert.False(passing.Passed);   // 550 > 500 мм — всё ещё не выполнено
        Assert.Equal(3.0, passing.Variables["levelCount"]);
    }

    [Fact]
    public void Unspecified_AddsNoteAndNoSpacingDetails()
    {
        var section = Section(0.30, 0.60,
            Layer(-0.25, 0.020, -0.10, 0.0, 0.10),
            Layer(0.25, 0.016, -0.10, 0.10));
        var profile = Analyze(section, -1);

        var (details, notes) = Sp63NormalConstructiveReinforcement.CheckCoverAndSpacing(
            profile, Sp63NormalAxis.Mx, Sp63ElementKind.Unspecified);

        Assert.DoesNotContain(details, d => d.NormReference is "10.3.5" or "10.3.8");
        Assert.Contains(notes, n => n.Code == "spacing_element_kind_unspecified");
    }

    [Fact]
    public void Checker_PassesElementKindFromMemberContext()
    {
        var section = Section(0.30, 0.60,
            Layer(-0.25, 0.020, -0.10, 0.0, 0.10),
            Layer(0.25, 0.016, -0.10, 0.10));
        var options = Sp63NormalFixtures.MemberOptions() with
        {
            MemberContext = Sp63NormalFixtures.MemberOptions().MemberContext with
            {
                ElementKind = Sp63ElementKind.BeamOrSlab
            }
        };

        var result = Sp63NormalChecker.Check(section, new LoadItem { Mx = -50.0 },
            CalcType.C, options);

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Contains(result.ConstructiveChecks, d => d.NormReference == "10.3.5");
        Assert.Contains(result.ConstructiveChecks, d => d.NormReference == "10.3.8");
    }

    [Theory]
    [InlineData("""{"elementKind":"column"}""", Sp63ElementKind.Column)]
    [InlineData("""{"elementKind":"beam_or_slab"}""", Sp63ElementKind.BeamOrSlab)]
    [InlineData("""{}""", Sp63ElementKind.Unspecified)]
    public void TaskParams_ElementKind_RoundTrips(string json, Sp63ElementKind expected)
    {
        var parameters = Sp63NormalTaskParams.Parse(json);
        Assert.True(parameters.TryToOptions(out var options, out var error), error);
        Assert.Equal(expected, options.MemberContext.ElementKind);

        var reparsed = Sp63NormalTaskParams.Parse(parameters.ToJson());
        Assert.True(reparsed.TryToOptions(out var again, out _));
        Assert.Equal(expected, again.MemberContext.ElementKind);
    }

    [Fact]
    public void TaskParams_UnknownElementKind_IsInvalid()
    {
        var parameters = Sp63NormalTaskParams.Parse("""{"elementKind":"wall"}""");
        Assert.False(parameters.TryToOptions(out _, out var error));
        Assert.Equal("invalid_element_kind", error);
    }

    static (double Y, double Diameter, double[] Xs) Layer(double y, double diameter,
        params double[] xs) => (y, diameter, xs);

    static CrossSection Section(double width, double height,
        params (double Y, double Diameter, double[] Xs)[] layers)
    {
        var section = Sp63NormalFixtures.Rectangle(width, height);
        var rebar = Sp63NormalFixtures.Rebar(2);
        foreach (var layer in layers)
        foreach (double x in layer.Xs)
            Sp63NormalFixtures.AddBar(section, x, layer.Y,
                Math.PI * layer.Diameter * layer.Diameter / 4.0, rebar,
                diameter: layer.Diameter);
        return section;
    }

    static Sp63NormalSectionProfile Analyze(CrossSection section, int tensionDirection)
    {
        var analysis = Sp63RebarLayoutAnalyzer.Analyze(section, Sp63NormalAxis.Mx,
            CalcType.C, tensionDirection);
        Assert.NotNull(analysis.Profile);
        return analysis.Profile!;
    }

    static List<CheckDetail> Spacing(CrossSection section, Sp63ElementKind kind,
        int tensionDirection)
    {
        var (details, _) = Sp63NormalConstructiveReinforcement.CheckCoverAndSpacing(
            Analyze(section, tensionDirection), Sp63NormalAxis.Mx, kind);
        return details;
    }
}
