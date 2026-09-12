using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Проверки минимального процента армирования по п. 10.3.6 СП 63.</summary>
public sealed class Sp63NormalConstructiveReinforcementTests
{
    [Fact]
    public void Bending_TensionAboveMinimum_IsSatisfied()
    {
        // b*h0 = 0.30*0.55 = 0.165 m²; As = 0.0020 m² -> mu = 1.212 % >= 0.1 %.
        var profile = Profile(tensionArea: 0.0020, compressionArea: 0.0);

        var (details, notes) = Sp63NormalConstructiveReinforcement.Check(
            "bending", profile, MemberContext());

        var detail = Assert.Single(details);
        Assert.Equal("Sp63Normal_MinReinforcementTension", detail.Description);
        Assert.Equal("10.3.6", detail.NormReference);
        Assert.Equal(0.1, detail.Applied, precision: 9);
        Assert.True(detail.Passed);
        Assert.Empty(notes);
    }

    [Fact]
    public void Bending_TensionBelowMinimum_IsNotSatisfied_ButStillReported()
    {
        // As = 0.0001 m² -> mu = 0.0001/0.165*100 = 0.0606 % < 0.1 %.
        var profile = Profile(tensionArea: 0.0001, compressionArea: 0.0);

        var (details, _) = Sp63NormalConstructiveReinforcement.Check(
            "bending", profile, MemberContext());

        var detail = Assert.Single(details);
        Assert.False(detail.Passed);
    }

    [Fact]
    public void Bending_ZeroCompressionArea_DoesNotAddCompressionCheck()
    {
        var profile = Profile(tensionArea: 0.0020, compressionArea: 0.0);

        var (details, _) = Sp63NormalConstructiveReinforcement.Check(
            "bending", profile, MemberContext());

        Assert.DoesNotContain(details,
            detail => detail.Description == "Sp63Normal_MinReinforcementCompression");
    }

    [Fact]
    public void Bending_PositiveCompressionArea_AddsSecondCheck()
    {
        var profile = Profile(tensionArea: 0.0020, compressionArea: 0.0020);

        var (details, _) = Sp63NormalConstructiveReinforcement.Check(
            "bending", profile, MemberContext());

        Assert.Equal(2, details.Count);
        Assert.Contains(details,
            detail => detail.Description == "Sp63Normal_MinReinforcementCompression");
    }

    [Fact]
    public void CentralTension_UsesDoubledRatio_OverFullConcreteArea()
    {
        // b*h = 0.30*0.60 = 0.18 m²; total As = 0.0020+0.0020 = 0.0040 m² -> mu = 2.222 %.
        var profile = Profile(tensionArea: 0.0020, compressionArea: 0.0020);

        var (details, _) = Sp63NormalConstructiveReinforcement.Check(
            "central_tension", profile, MemberContext());

        var detail = Assert.Single(details);
        Assert.Equal("Sp63Normal_MinReinforcementCentralTension", detail.Description);
        Assert.Equal(0.2, detail.Applied, precision: 9);
        Assert.Equal(0.0040 / 0.18 * 100.0, detail.Allowable, precision: 9);
        Assert.True(detail.Passed);
    }

    // h = 0.60 м, поэтому l0 подобрано так, чтобы l0/h давало нужную гибкость.
    [Theory]
    [InlineData(1.8, 0.1)]    // l0/h = 3 (ниже нижнего порога) -> нижняя граница 0,1 %
    [InlineData(3.0, 0.1)]    // l0/h = 5 (нижний порог) -> 0,1 %
    [InlineData(9.0, 0.175)]  // l0/h = 15 (середина интерполяции) -> 0,175 %
    [InlineData(15.0, 0.25)]  // l0/h = 25 (верхний порог) -> 0,25 %
    [InlineData(24.0, 0.25)]  // l0/h = 40 (выше верхнего порога) -> верхняя граница 0,25 %
    public void Compression_InterpolatesMuMin_BySlenderness(double l0, double expectedMuMin)
    {
        var profile = Profile(tensionArea: 0.0020, compressionArea: 0.0);
        var context = MemberContext() with { EffectiveLengthL0 = l0 };

        var (details, _) = Sp63NormalConstructiveReinforcement.Check(
            "compression", profile, context);

        var detail = Assert.Single(details);
        Assert.Equal(expectedMuMin, detail.Applied, precision: 9);
    }

    [Fact]
    public void Compression_MissingEffectiveLength_ReturnsNoDetails_ButAddsInformationalNote()
    {
        var profile = Profile(tensionArea: 0.0020, compressionArea: 0.0);
        var context = MemberContext() with { EffectiveLengthL0 = null };

        var (details, notes) = Sp63NormalConstructiveReinforcement.Check(
            "compression", profile, context);

        Assert.Empty(details);
        var note = Assert.Single(notes);
        Assert.Equal(Sp63NormalMessageKind.Information, note.Kind);
        Assert.Equal("10.3.6", note.NormReference);
    }

    [Fact]
    public void UnsupportedBranch_ReturnsNoDetailsAndNoNotes()
    {
        var profile = Profile(tensionArea: 0.0020, compressionArea: 0.0);

        var (details, notes) = Sp63NormalConstructiveReinforcement.Check(
            "not_applicable", profile, MemberContext());

        Assert.Empty(details);
        Assert.Empty(notes);
    }

    static Sp63MemberContext MemberContext() => new(
        6.0,
        Sp63StructuralScheme.StaticallyIndeterminate,
        4.2,
        Sp63NormalStabilityMode.Member,
        1.0);

    static Sp63NormalSectionProfile Profile(double tensionArea, double compressionArea)
    {
        const double b = 0.30;
        const double h = 0.60;
        const double h0 = 0.55;
        const double aPrime = 0.05;
        var tensionLayer = new Sp63NormalRebarLayer(-h / 2 + aPrime, tensionArea,
            435_000.0, 435_000.0, []);
        var compressionLayer = new Sp63NormalRebarLayer(h / 2 - aPrime, compressionArea,
            435_000.0, 435_000.0, []);
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
