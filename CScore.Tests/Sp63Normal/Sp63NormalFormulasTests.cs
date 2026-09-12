using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Численные проверки формульных примитивов нормальных сечений.</summary>
public sealed class Sp63NormalFormulasTests
{
    [Fact]
    public void Bending_UsesEquations845()
    {
        double x = Sp63NormalFormulas.BendingX(435_000, 0.002, 435_000, 0.001,
            17_000, 0.30);
        double expectedX = (435_000 * 0.002 - 435_000 * 0.001) /
                           (17_000 * 0.30);
        double moment = Sp63NormalFormulas.BendingMoment(17_000, 0.30, x, 0.55,
            435_000, 0.001, 0.05);
        double expectedMoment = 17_000 * 0.30 * x * (0.55 - 0.5 * x)
            + 435_000 * 0.001 * (0.55 - 0.05);

        Assert.Equal(expectedX, x, precision: 12);
        Assert.Equal(expectedMoment, moment, precision: 12);
    }

    [Fact]
    public void SymmetricBranch_WhenXWithoutCompressionRebarLessThanTwoAprime_UsesXOverTwo()
    {
        var moment = Sp63NormalFormulas.SymmetricMoment(435_000, 0.002, 0.55,
            0.05, xWithoutCompressionRebar: 0.04,
            compressionRebarWasExcluded: true);

        Assert.Equal(435_000 * 0.002 * (0.55 - 0.02), moment, precision: 12);
    }

    [Fact]
    public void SymmetricBranch_WhenCompressionRebarIsPresent_UsesProvidedAprime()
    {
        var moment = Sp63NormalFormulas.SymmetricMoment(435_000, 0.002, 0.55,
            0.05, xWithoutCompressionRebar: 0.04,
            compressionRebarWasExcluded: false);

        Assert.Equal(435_000 * 0.002 * (0.55 - 0.05), moment, precision: 12);
    }

    [Fact]
    public void Compression_UsesEquation812WhenXiDoesNotExceedXiR()
    {
        var x = Sp63NormalFormulas.CompressionXLowXi(800, 435_000, 0.002,
            435_000, 0.001, 17_000, 0.30);

        Assert.Equal((800 + 435_000 * 0.002 - 435_000 * 0.001) /
                     (17_000 * 0.30), x, precision: 12);
    }

    [Fact]
    public void Compression_UsesEquation813WhenXiExceedsXiR()
    {
        const double xiR = 0.45;
        var x = Sp63NormalFormulas.CompressionXHighXi(800, 435_000, 0.002,
            435_000, 0.001, 17_000, 0.30, 0.55, xiR);
        var expected = (800 + 435_000 * 0.002 * (1 + xiR) / (1 - xiR)
            - 435_000 * 0.001) /
            (17_000 * 0.30 + 2 * 435_000 * 0.002 /
             (0.55 * (1 - xiR)));

        Assert.Equal(expected, x, precision: 12);
    }

    [Fact]
    public void EccentricCompression_UsesEquation811()
    {
        Assert.Equal(0.20 * 0.04 + (0.55 - 0.05) / 2.0,
            Sp63NormalFormulas.CompressionE(0.20, 0.04, 0.55, 0.05),
            precision: 12);
    }

    [Fact]
    public void EccentricTensionOutside_CapsXAtXiRH0()
    {
        var limited = Sp63NormalFormulas.LimitTensionX(0.40, 0.45, 0.55);

        Assert.True(limited.WasLimited);
        Assert.Equal(0.45 * 0.55, limited.UsedX, precision: 12);
    }

    [Fact]
    public void CentralTension_UsesSumOfRebarCapacity()
    {
        Assert.Equal(870.0,
            Sp63NormalFormulas.CentralTensionCapacity(435_000, 0.002),
            precision: 12);
    }

    [Fact]
    public void XiR_UsesConcreteUltimateStrain()
    {
        var expected = 0.8 / (1 + (435_000.0 / 200_000_000.0) / 0.0035);

        Assert.Equal(expected,
            Sp63NormalFormulas.XiR(435_000, 200_000_000, 0.0035),
            precision: 12);
    }
}
