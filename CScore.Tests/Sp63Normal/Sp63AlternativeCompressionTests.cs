using CScore;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Альтернативный метод п. 8.1.16 СП 63: φ по таблице 8.1 и формула (8.17).</summary>
public sealed class Sp63AlternativeCompressionTests
{
    [Theory]
    [InlineData(30, 4.0, 0.92)]   // l0/h < 6 — значение первого столбца
    [InlineData(30, 6.0, 0.92)]
    [InlineData(30, 12.0, 0.872)] // 0,90 + (0,83 − 0,90)·0,4
    [InlineData(55, 20.0, 0.70)]
    [InlineData(60, 15.0, 0.80)]
    [InlineData(80, 20.0, 0.64)]
    [InlineData(80, 17.5, 0.715)] // (0,79 + 0,64)/2
    public void PhiLongTerm_Table81(int concreteClass, double l0OverH, double expected) =>
        Assert.Equal(expected, Sp63AlternativeCompression.PhiLongTerm(concreteClass, l0OverH)!.Value, 6);

    [Theory]
    [InlineData(15, 10.0)]  // ниже B20 — нет в таблице
    [InlineData(70, 10.0)]  // B65–B75 — нет в таблице
    [InlineData(90, 10.0)]  // выше B80
    [InlineData(25, 20.5)]  // l0/h > 20 — метод неприменим
    public void PhiLongTerm_OutsideTable_ReturnsNull(int concreteClass, double l0OverH) =>
        Assert.Null(Sp63AlternativeCompression.PhiLongTerm(concreteClass, l0OverH));

    [Theory]
    [InlineData(5.0, 0.9)]
    [InlineData(10.0, 0.9)]
    [InlineData(15.0, 0.875)]
    [InlineData(20.0, 0.85)]
    public void PhiShortTerm_LinearBetween10And20(double l0OverH, double expected) =>
        Assert.Equal(expected, Sp63AlternativeCompression.PhiShortTerm(l0OverH)!.Value, 6);

    [Fact]
    public void PhiShortTerm_AboveLimit_ReturnsNull() =>
        Assert.Null(Sp63AlternativeCompression.PhiShortTerm(21.0));

    [Theory]
    [InlineData("B25", 25)]
    [InlineData("Бетон В30", 30)]   // кириллическая «В»
    [InlineData("B22.5", 22)]
    [InlineData("B-1", null)]
    [InlineData("A500", null)]
    [InlineData("", null)]
    public void ParseConcreteClass(string tag, int? expected) =>
        Assert.Equal(expected, Sp63AlternativeCompression.ParseConcreteClass(tag));

    [Fact]
    public void UltimateForce_Formula817() =>
        // 0,9·(20 000·0,18 + 435 000·0,002) = 0,9·(3600 + 870) = 4023 кН
        Assert.Equal(4023.0, Sp63AlternativeCompression.UltimateForce(
            0.9, 20_000.0, 0.18, 435_000.0, 0.002), 6);

    [Fact]
    public void Checker_CentralCompression_AddsReferenceCheckOutsideVerdict()
    {
        // 300×600, l = 6 м → ea = h/30 = 0,02 м; l0/h = 4,2/0,6 = 7 → φ = 0,9 (кратковременно).
        var result = Check(new LoadItem { N = -1000.0, Mx = 0.0 });

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        var detail = Assert.Single(result.AlternativeChecks);
        Assert.Equal("(8.17)", detail.Formula);
        Assert.Equal("8.1.16", detail.NormReference);
        Assert.Equal(0.9, detail.Variables["phi"], 6);
        Assert.Equal(1000.0, detail.Applied, 6);
        // 4 стержня по 0,001 м²: 0,9·(20 000·0,18 + 435 000·0,004) = 0,9·5340 = 4806 кН.
        Assert.Equal(0.004, detail.Variables["AsTot"], 9);
        Assert.Equal(4806.0, detail.Allowable, 6);
        Assert.DoesNotContain(result.StrengthDetails, d => d.Formula == "(8.17)");
    }

    [Fact]
    public void Checker_EccentricityAboveH30_OnlyInformationalMessage()
    {
        // e0 = 12/500 = 0,024 м > h/30 = 0,02 м.
        var result = Check(new LoadItem { N = -500.0, Mx = -12.0 });

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Empty(result.AlternativeChecks);
        Assert.Contains(result.InformationalMessages,
            m => m.Code == "alt_compression_eccentricity");
    }

    [Fact]
    public void Checker_SlendernessAbove20_OnlyInformationalMessage()
    {
        var options = Sp63NormalFixtures.MemberOptions() with
        {
            MemberContext = Sp63NormalFixtures.MemberOptions().MemberContext with
            {
                EffectiveLengthL0 = 12.6 // l0/h = 21
            }
        };
        var result = Sp63NormalChecker.Check(Section(), new LoadItem { N = -300.0 },
            CalcType.C, options);

        Assert.Empty(result.AlternativeChecks);
        Assert.Contains(result.InformationalMessages,
            m => m.Code == "alt_compression_slenderness");
    }

    [Fact]
    public void Checker_Bending_NoAlternativeCheck()
    {
        var result = Check(new LoadItem { N = 0.0, Mx = 20.0 });

        Assert.Empty(result.AlternativeChecks);
        Assert.DoesNotContain(result.InformationalMessages,
            m => m.Code.StartsWith("alt_compression", StringComparison.Ordinal));
    }

    static CrossSection Section() => Sp63NormalFixtures.TwoLayerRectangle(0.30, 0.60,
        tensionArea: 0.0010, compressionArea: 0.0010);

    static Sp63NormalResult Check(LoadItem load) => Sp63NormalChecker.Check(
        Section(), load, CalcType.C, Sp63NormalFixtures.MemberOptions());
}
