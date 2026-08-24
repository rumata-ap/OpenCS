using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>Поведение коэффициента использования при вырожденной несущей способности.</summary>
public sealed class CheckDetailRatioTests
{
    [Fact]
    public void Ratio_ZeroAllowableWithAppliedForce_IsInfiniteAndNotPassed()
    {
        var detail = new CheckDetail { Applied = 120.0, Allowable = 0.0 };

        Assert.True(double.IsPositiveInfinity(detail.Ratio));
        Assert.False(detail.Passed);
    }

    [Fact]
    public void Ratio_ZeroAllowableWithoutAppliedForce_IsZeroAndPassed()
    {
        var detail = new CheckDetail { Applied = 0.0, Allowable = 0.0 };

        Assert.Equal(0.0, detail.Ratio);
        Assert.True(detail.Passed);
    }

    [Fact]
    public void Ratio_PositiveAllowable_IsAppliedOverAllowable()
    {
        var detail = new CheckDetail { Applied = 50.0, Allowable = 200.0 };

        Assert.Equal(0.25, detail.Ratio, 12);
        Assert.True(detail.Passed);
    }
}
