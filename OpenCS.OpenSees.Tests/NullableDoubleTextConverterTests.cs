using System.Globalization;
using System.Windows.Data;
using OpenCS.Converters;

namespace OpenCS.OpenSees.Tests;

public sealed class NullableDoubleTextConverterTests
{
    static readonly NullableDoubleTextConverter Converter = new();

    [Theory]
    [InlineData("0,25", 0.25)]
    [InlineData("0.25", 0.25)]
    [InlineData("1", 1.0)]
    public void ConvertBack_ParsesFractionalLengthWithEitherDecimalSeparator(string text, double expected)
    {
        var value = Converter.ConvertBack(text, typeof(double?), null!, CultureInfo.GetCultureInfo("ru-RU"));

        Assert.Equal(expected, Assert.IsType<double>(value));
    }

    [Fact]
    public void ConvertBack_EmptyTextReturnsNull()
    {
        var value = Converter.ConvertBack("   ", typeof(double?), null!, CultureInfo.InvariantCulture);

        Assert.Null(value);
    }

    [Fact]
    public void ConvertBack_InvalidTextDoesNotOverwriteCurrentValue()
    {
        var value = Converter.ConvertBack("0,2.5", typeof(double?), null!, CultureInfo.InvariantCulture);

        Assert.Same(Binding.DoNothing, value);
    }
}
