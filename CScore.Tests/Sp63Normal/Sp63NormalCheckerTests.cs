using CScore;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Проверки выбора ветви и вердикта нормального сечения.</summary>
public sealed class Sp63NormalCheckerTests
{
    [Fact]
    public void PositiveMx_UsesYPositiveSideAsTensionSide()
    {
        var result = Check(new LoadItem { N = 0.0, Mx = 20.0, My = 0.0 });

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Equal(1.0, result.Variables["tensionDirection"]);
    }

    [Fact]
    public void NegativeMx_UsesYNegativeSideAsTensionSide()
    {
        var result = Check(new LoadItem { N = 0.0, Mx = -20.0, My = 0.0 });

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Equal(-1.0, result.Variables["tensionDirection"]);
    }

    [Fact]
    public void MyBranch_UsesXCoordinate_AsHeightAndRejectsNonZeroMx()
    {
        var options = Sp63NormalFixtures.MemberOptions() with
        {
            Axis = Sp63NormalAxis.My
        };
        var result = Sp63NormalChecker.Check(
            Sp63NormalFixtures.TwoLayerRectangle(0.30, 0.60, 0.0010, 0.0010),
            new LoadItem { N = 0.0, Mx = 1.0, My = 20.0 }, CalcType.C, options);

        Assert.Equal(Sp63NormalStatus.NotApplicable, result.Status);
        Assert.Contains(result.ApplicabilityMessages,
            message => message.Code == "biaxial_load");
        Assert.Contains(result.InformationalMessages,
            message => message.Code == "suggest_ndm");
    }

    [Fact]
    public void MissingConcreteResistance_DoesNotSuggestNdm()
    {
        // Отсутствие сопротивления бетона - проблема исходных данных, а не сложности
        // геометрии или арматуры, которую решает переход к НДМ.
        var concreteNoChars = new Material
            { Id = 1, Tag = "B-none", Type = MatType.Concrete, E = 30_000_000.0 };
        var section = new CrossSection { Tag = "test" };
        section.Areas.Add(Sp63NormalFixtures.ConcreteRegion(concreteNoChars,
        [
            (-0.15, -0.30), (0.15, -0.30), (0.15, 0.30), (-0.15, 0.30)
        ]));
        var steel = Sp63NormalFixtures.Rebar(2);
        Sp63NormalFixtures.AddBar(section, -0.075, -0.25, 0.0010, steel);
        Sp63NormalFixtures.AddBar(section, 0.075, -0.25, 0.0010, steel);
        Sp63NormalFixtures.AddBar(section, -0.075, 0.25, 0.0010, steel);
        Sp63NormalFixtures.AddBar(section, 0.075, 0.25, 0.0010, steel);

        var result = Sp63NormalChecker.Check(section, new LoadItem { N = 0.0, Mx = -20.0 },
            CalcType.C, Sp63NormalFixtures.MemberOptions());

        Assert.Equal(Sp63NormalStatus.NotApplicable, result.Status);
        Assert.Contains(result.ApplicabilityMessages,
            message => message.Code == "missing_concrete_resistance");
        Assert.DoesNotContain(result.InformationalMessages,
            message => message.Code == "suggest_ndm");
    }

    [Fact]
    public void PureBending_ReturnsOneStrengthDetail_AndNotInfoFailure()
    {
        var result = Check(new LoadItem { N = 0.0, Mx = -20.0 });

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Single(result.StrengthDetails);
        Assert.DoesNotContain(result.StrengthDetails,
            detail => detail.Formula == "ξ ≤ ξR");
    }

    [Fact]
    public void CentralTension_ReturnsEquation819Detail()
    {
        var result = Check(new LoadItem { N = 500.0 });

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Contains(result.StrengthDetails,
            detail => detail.Formula == "(8.19)");
    }

    [Fact]
    public void Compression_ReturnsEquation810Detail()
    {
        var result = Check(new LoadItem { N = -500.0, Mx = -12.0 });

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Contains(result.StrengthDetails,
            detail => detail.Formula == "(8.10)");
    }

    [Fact]
    public void MissingLengthForCompression_ReturnsNotApplicable_NotFailed()
    {
        var options = Sp63NormalFixtures.MemberOptions() with
        {
            MemberContext = Sp63NormalFixtures.MemberOptions().MemberContext with
            {
                ElementLengthOrRestraintDistance = null
            }
        };
        var result = Sp63NormalChecker.Check(
            Sp63NormalFixtures.TwoLayerRectangle(0.30, 0.60, 0.0010, 0.0010),
            new LoadItem { N = -500.0 }, CalcType.C, options);

        Assert.Equal(Sp63NormalStatus.NotApplicable, result.Status);
        Assert.Null(result.StrengthPassed);
    }

    [Fact]
    public void PrestressedRebar_ReturnsNotApplicable_NotFailed()
    {
        var result = Sp63NormalChecker.Check(
            Sp63NormalFixtures.TwoLayerRectangle(0.30, 0.60, 0.0010, 0.0010,
                sigSp: 100.0),
            new LoadItem { N = 500.0 }, CalcType.C,
            Sp63NormalFixtures.MemberOptions());

        Assert.Equal(Sp63NormalStatus.NotApplicable, result.Status);
        Assert.Null(result.StrengthPassed);
    }

    [Fact]
    public void EccentricTensionBetweenResultants_CreatesTwoIndependentChecks()
    {
        var result = Check(new LoadItem { N = 100.0, Mx = 1.0 });

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Contains(result.StrengthDetails,
            detail => detail.Formula == "(8.20)");
        Assert.Contains(result.StrengthDetails,
            detail => detail.Formula == "(8.21)");
    }

    [Fact]
    public void EccentricTensionBetweenResultants_OnePassOneFails_FailsStrengthOnly()
    {
        var section = Sp63NormalFixtures.TwoLayerRectangle(0.30, 0.60,
            tensionArea: 0.0001, compressionArea: 0.0010);
        var result = Sp63NormalChecker.Check(section,
            new LoadItem { N = 500.0, Mx = 1.0 }, CalcType.C,
            Sp63NormalFixtures.MemberOptions());

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Equal(2, result.StrengthDetails.Count);
        Assert.Contains(result.StrengthDetails, detail => detail.Passed);
        Assert.Contains(result.StrengthDetails, detail => !detail.Passed);
        Assert.False(result.StrengthPassed ?? true);
    }

    [Fact]
    public void EccentricTensionOutsideResultants_CapsX_AndRemainsCalculated()
    {
        var section = Sp63NormalFixtures.TwoLayerRectangle(0.30, 0.60,
            tensionArea: 0.0025, compressionArea: 0.0001);
        var result = Sp63NormalChecker.Check(section,
            new LoadItem { N = 100.0, Mx = -100.0 }, CalcType.C,
            Sp63NormalFixtures.MemberOptions());

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Equal(1.0, result.Variables["tensionXWasLimited"]);
        Assert.Equal(result.Variables["xiR"] * result.Variables["h0"],
            result.Variables["tensionUsedX"], precision: 12);
    }

    [Fact]
    public void Bending_PopulatesConstructiveChecks_WithoutAffectingStrengthPassed()
    {
        var result = Check(new LoadItem { N = 0.0, Mx = -20.0 });

        Assert.NotEmpty(result.ConstructiveChecks);
        Assert.All(result.ConstructiveChecks, detail => Assert.Contains(
            detail.NormReference, new[] { "10.3.6", "10.3.2", "10.3.9" }));
        Assert.Contains(result.ConstructiveChecks,
            detail => detail.NormReference == "10.3.2");
    }

    [Fact]
    public void Compression_BelowMinReinforcement_FailsConstructiveCheck_ButNotStrength()
    {
        // Символическая арматура почти нулевой площади: несущую способность даёт
        // в основном бетон, поэтому прочность может пройти, а 10.3.6 - нет.
        var section = Sp63NormalFixtures.TwoLayerRectangle(0.30, 0.60,
            tensionArea: 1e-7, compressionArea: 1e-7);
        var result = Sp63NormalChecker.Check(section,
            new LoadItem { N = -50.0, Mx = -2.0 }, CalcType.C,
            Sp63NormalFixtures.MemberOptions());

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Contains(result.ConstructiveChecks, detail => !detail.Passed);
    }

    [Fact]
    public void SymmetricBranch_UsesXOverTwo_WhenCompressionSteelExcludedXIsSmall()
    {
        var section = Sp63NormalFixtures.TwoLayerRectangle(0.30, 0.60,
            tensionArea: 0.0005, compressionArea: 1e-12);
        var result = Sp63NormalChecker.Check(section,
            new LoadItem { Mx = -1.0 }, CalcType.C,
            Sp63NormalFixtures.MemberOptions());

        Assert.True(result.Variables["symmetricBranchApplied"] > 0.5);
        Assert.Equal(result.Variables["symmetricXWithoutCompressionRebar"] / 2.0,
            result.Variables["symmetricAprimeEffective"], precision: 12);
    }

    static Sp63NormalResult Check(LoadItem load) => Sp63NormalChecker.Check(
        Sp63NormalFixtures.TwoLayerRectangle(0.30, 0.60,
            tensionArea: 0.0010, compressionArea: 0.0010),
        load, CalcType.C, Sp63NormalFixtures.MemberOptions());
}
