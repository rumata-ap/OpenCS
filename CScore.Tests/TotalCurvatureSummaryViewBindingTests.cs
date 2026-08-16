using System.Text.RegularExpressions;
using Xunit;

namespace CScore.Tests;

public class TotalCurvatureSummaryViewBindingTests
{
    [Fact]
    public void SummaryView_ReadOnlyRunBindingsAreOneWay()
    {
        string xaml = File.ReadAllText(FindSummaryView());
        var bindings = Regex.Matches(xaml, @"<Run\s+Text=""\{Binding\s+[^""]+""\s*/>");

        Assert.NotEmpty(bindings);
        Assert.All(bindings, binding => Assert.Contains("Mode=OneWay", binding.Value));
    }

    private static string FindSummaryView()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName,
                "OpenCS",
                "Views",
                "TotalCurvatureSummaryView.xaml");
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Не найден XAML TotalCurvatureSummaryView.");
    }
}
