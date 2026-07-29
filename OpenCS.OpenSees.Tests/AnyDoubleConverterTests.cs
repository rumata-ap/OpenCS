using System.Globalization;
using System.Windows;
using System.Windows.Data;
using OpenCS.Utilites;

namespace OpenCS.OpenSees.Tests;

public sealed class AnyDoubleConverterTests
{
    [Theory]
    [InlineData("0,05")]
    [InlineData("0.05")]
    public void ConvertBack_AcceptsCommaAndDotDecimalSeparators(string text)
    {
        var result = new AnyDoubleConverter().ConvertBack(
            text, typeof(double), null!, CultureInfo.InvariantCulture);

        Assert.Equal(0.05, Assert.IsType<double>(result), 12);
    }

    [Fact]
    public void ConvertBack_InvalidText_ReturnsDoNothing()
    {
        var result = new AnyDoubleConverter().ConvertBack(
            "not-a-number", typeof(double), null!, CultureInfo.InvariantCulture);

        Assert.Same(Binding.DoNothing, result);
    }

    [Fact]
    public void FemAnalysisDialog_BindsStageNumbersThroughAnyDoubleConverter()
    {
        string path = FindRepositoryFile(Path.Combine("OpenCS", "Views", "FemAnalysisDialog.xaml"));
        string xaml = File.ReadAllText(path);

        Assert.Contains("x:Key=\"AnyDoubleConverter\"", xaml, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(xaml, "Converter=\"{StaticResource AnyDoubleConverter}\""));
    }

    static int CountOccurrences(string text, string value)
    {
        int count = 0;
        for (int offset = 0; (offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0; offset += value.Length)
            count++;
        return count;
    }

    static string FindRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new DirectoryInfo(typeof(AnyDoubleConverterTests).Assembly.Location).Parent;
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Не найден файл репозитория: {relativePath}");
    }
}
