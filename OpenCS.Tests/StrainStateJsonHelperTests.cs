using System.Text.Json;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

public sealed class StrainStateJsonHelperTests
{
    [Fact]
    public void FiniteRounded_ReturnsNullForNonFiniteValues()
    {
        Assert.Equal(1.234568, StrainStateJsonHelper.FiniteRounded(1.2345678, 6));
        Assert.Null(StrainStateJsonHelper.FiniteRounded(double.PositiveInfinity, 6));
        Assert.Null(StrainStateJsonHelper.FiniteRounded(double.NegativeInfinity, 6));
        Assert.Null(StrainStateJsonHelper.FiniteRounded(double.NaN, 6));
    }

    [Fact]
    public void FiniteRoundedArray_RemovesNonFiniteHistoryEntries()
    {
        var result = StrainStateJsonHelper.FiniteRoundedArray(
            [1.0, double.PositiveInfinity, 1.25, double.NaN, 1.5], 3);

        Assert.Equal([1.0, 1.25, 1.5], result);
    }

    [Fact]
    public void NonFiniteEtaPayload_IsValidStandardJson()
    {
        var payload = new
        {
            etaX = StrainStateJsonHelper.FiniteRounded(double.PositiveInfinity, 6),
            etaHistoryX = StrainStateJsonHelper.FiniteRoundedArray(
                [double.PositiveInfinity, 1.25], 6),
        };

        string json = JsonSerializer.Serialize(payload);

        Assert.Contains("\"etaX\":null", json);
        Assert.Contains("\"etaHistoryX\":[1.25]", json);
    }
}
