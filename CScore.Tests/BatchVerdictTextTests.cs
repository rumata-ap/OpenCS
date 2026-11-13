using System.Globalization;
using CScore.Batch;
using Xunit;

namespace CScore.Tests;

/// <summary>Числа причин и отчётов: без экспоненты; причины — с запятой, отчёты одиночных задач — с точкой.</summary>
public sealed class BatchVerdictTextTests
{
    [Theory]
    [InlineData(1e-7, "0.0000001")]
    [InlineData(5.3291e-8, "0.000000053291")]
    [InlineData(52.5272, "52.5272")]
    [InlineData(-0.00076187934, "-0.00076187934")]
    [InlineData(55619.513, "55619.513")]
    public void Invariant_EightSignificant_NoExponent(double v, string expected)
        => Assert.Equal(expected, BatchVerdictText.N(v, 8, CultureInfo.InvariantCulture));

    [Theory]
    [InlineData(8.81e-5, "0,0000881")]
    [InlineData(12345.0, "12350")]
    [InlineData(0.00349, "0,00349")]
    public void Ru_FourSignificant_Comma(double v, string expected)
        => Assert.Equal(expected, BatchVerdictText.N(v, 4));
}
