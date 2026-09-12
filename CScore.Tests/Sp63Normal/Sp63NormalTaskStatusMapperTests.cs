using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Проверки разделения статуса применимости и вердикта прочности.</summary>
public sealed class Sp63NormalTaskStatusMapperTests
{
    [Theory]
    [InlineData(Sp63NormalStatus.NotApplicable, null, "not_applicable")]
    [InlineData(Sp63NormalStatus.Calculated, true, "ok")]
    [InlineData(Sp63NormalStatus.Calculated, false, "not_passed")]
    [InlineData(Sp63NormalStatus.InvalidInput, null, "error")]
    public void StatusMapper_SeparatesApplicabilityFromStrength(
        Sp63NormalStatus status, bool? passed, string expected)
    {
        var result = new Sp63NormalResult { Status = status, StrengthPassed = passed };

        Assert.Equal(expected, Sp63NormalTaskStatusMapper.ToCalcResultStatus(result));
    }
}
