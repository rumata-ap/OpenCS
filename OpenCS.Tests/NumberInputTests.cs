using System.Globalization;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Число из поля ввода: запятая и точка — десятичный разделитель, разделителей тысяч нет.</summary>
public sealed class NumberInputTests
{
    [Theory]
    [InlineData("14,5", 14.5)]
    [InlineData("14.5", 14.5)]
    [InlineData(" -0,002 ", -0.002)]
    [InlineData("2e-3", 0.002)]
    [InlineData("30", 30)]
    public void Parses(string text, double expected) => Assert.Equal(expected, NumberInput.Parse(text), 12);

    [Theory]
    [InlineData("1,000.5")]
    [InlineData("1 000")]
    [InlineData("")]
    [InlineData("abc")]
    public void Rejects(string text) => Assert.Throws<FormatException>(() => NumberInput.Parse(text));

    [Fact]
    public void InverseConverters_ZeroIsPositiveZero()
    {
        var inv = (double)new ConvertInv().Convert(0.0, typeof(double), null!, CultureInfo.InvariantCulture);
        var mpa = (double)new MPaConvertInv().Convert(0.0, typeof(double), null!, CultureInfo.InvariantCulture);
        Assert.False(double.IsNegative(inv));
        Assert.False(double.IsNegative(mpa));
        Assert.Equal(0.002, (double)new ConvertInv().Convert(-0.002, typeof(double), null!, CultureInfo.InvariantCulture), 12);
    }

    [Fact]
    public void MPaConverter_CommaInput_NotTimesTen()
    {
        var conv = new MPaConvert();
        Assert.Equal(14500.0, (double)conv.ConvertBack("14,5", typeof(double), null!, CultureInfo.InvariantCulture), 9);
    }
}
