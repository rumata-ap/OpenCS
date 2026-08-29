using System.Xml.Linq;
using System.Globalization;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверяет настройки ввода дробных чисел в редакторе хомутов.</summary>
public sealed class StirrupGroupInputTests
{
    static readonly string[] DecimalProperties =
    [
        "SpacingM", "OffsetM", "DiameterMm", "CutPosition", "CopyDx", "CopyDy"
    ];

    [Fact]
    public void DecimalTextBoxes_AcceptBothSeparatorsAndCommitOnFocusLoss()
    {
        var doc = XDocument.Load(FindPagePath());
        var textBoxes = doc.Descendants()
            .Where(element => element.Name.LocalName == "TextBox")
            .Select(element => element.Attribute("Text")?.Value)
            .Where(binding => binding != null)
            .ToArray();

        foreach (var property in DecimalProperties)
        {
            var binding = Assert.Single(textBoxes,
                value => value!.Contains($"Binding {property}", StringComparison.Ordinal));

            Assert.Contains("Converter={StaticResource AnyDoubleConverter}", binding);
            Assert.Contains("UpdateSourceTrigger=LostFocus", binding);
        }
    }

    [Theory]
    [InlineData("0,15")]
    [InlineData("0.15")]
    public void AnyDoubleConverter_ParsesBothDecimalSeparators(string text)
    {
        var result = new AnyDoubleConverter().ConvertBack(
            text, typeof(double), null!, CultureInfo.InvariantCulture);

        Assert.Equal(0.15, Assert.IsType<double>(result), 12);
    }

    static string FindPagePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine(directory.FullName, "OpenCS", "Views", "StirrupGroupPage.xaml");
            if (File.Exists(path)) return path;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Не найден XAML редактора хомутов.");
    }
}
