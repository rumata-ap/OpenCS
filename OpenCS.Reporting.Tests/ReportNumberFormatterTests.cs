using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверяет единые правила округления чисел в отчётах.</summary>
public sealed class ReportNumberFormatterTests
{
    [Theory]
    [InlineData(12.0, "12")]
    [InlineData(-0.00001, "0")]
    [InlineData(1.23456, "1.235")]
    public void Format_removes_unhelpful_zeros_and_negative_zero(double value, string expected)
    {
        Assert.Equal(expected, ReportNumberFormatter.Format(value, ReportUnit.Unitless,
            ReportNumberProfile.Decimal(3)));
    }

    [Theory]
    [InlineData(0.9994, "0.9994")]
    [InlineData(1.0000, "1")]
    [InlineData(1.0004, "1.0004")]
    [InlineData(1.2341, "1.235")]
    public void FormatUtilization_is_conservative_near_one(double value, string expected)
        => Assert.Equal(expected, ReportNumberFormatter.FormatUtilization(value));

    [Fact]
    public void Format_non_finite_value_returns_explicit_placeholder()
    {
        Assert.Equal("—", ReportNumberFormatter.Format(
            double.PositiveInfinity, ReportUnit.Unitless));
    }
}
