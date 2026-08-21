using System.Xml.Linq;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Регрессионные проверки маркировки сверхпредельных точек диаграммы.</summary>
public sealed class MomentCurvatureBiaxialResultTests
{
    [Fact]
    public void CurvatureSeries_SplitsEveryNonPhysicalRun()
    {
        var result = new CScore.CalcResult
        {
            Status = "ok",
            DataJson = """
            {
              "has_mx": true,
              "has_my": false,
              "points": [
                {"mx": 1, "my": 0, "ky": 1, "kz": 0, "segment": 1, "converged": true, "non_physical": false},
                {"mx": 2, "my": 0, "ky": 2, "kz": 0, "segment": 1, "converged": true, "non_physical": false},
                {"mx": 3, "my": 0, "ky": 3, "kz": 0, "segment": 3, "converged": true, "non_physical": true},
                {"mx": 4, "my": 0, "ky": 4, "kz": 0, "segment": 3, "converged": true, "non_physical": false},
                {"mx": 5, "my": 0, "ky": 5, "kz": 0, "segment": 3, "converged": true, "non_physical": false},
                {"mx": 6, "my": 0, "ky": 6, "kz": 0, "segment": 4, "converged": true, "non_physical": true},
                {"mx": 7, "my": 0, "ky": 7, "kz": 0, "segment": 4, "converged": true, "non_physical": true}
              ]
            }
            """
        };

        var viewModel = new OpenCS.ViewModels.MomentCurvatureBiaxialResultVM(result);

        Assert.Equal(2, viewModel.CurvatureYSeriesParts.Count);
        Assert.Equal(new[] { 1.0, 2.0 }, viewModel.CurvatureYSeriesParts[0].X);
        Assert.Equal(new[] { 4.0, 5.0 }, viewModel.CurvatureYSeriesParts[1].X);
        Assert.Equal(2, viewModel.CurvatureYSeriesFadedParts.Count);
        Assert.Equal(new[] { 2.0, 3.0, 4.0 }, viewModel.CurvatureYSeriesFadedParts[0].X);
        Assert.Equal(new[] { 5.0, 6.0, 7.0 }, viewModel.CurvatureYSeriesFadedParts[1].X);
    }

    [Fact]
    public void CurvatureSeries_ColorsTransitionFromFirstNonPhysicalPoint()
    {
        var result = new CScore.CalcResult
        {
            Status = "ok",
            DataJson = """
            {
              "has_mx": true,
              "has_my": false,
              "points": [
                {"mx": 1, "my": 0, "ky": 1, "kz": 0, "converged": true, "non_physical": true},
                {"mx": 2, "my": 0, "ky": 2, "kz": 0, "converged": true, "non_physical": false}
              ]
            }
            """
        };

        var viewModel = new OpenCS.ViewModels.MomentCurvatureBiaxialResultVM(result);

        Assert.Single(viewModel.CurvatureYSeriesFadedParts);
        Assert.Equal(new[] { 1.0, 2.0 }, viewModel.CurvatureYSeriesFadedParts[0].X);
    }

    [Fact]
    public void CurvatureSeries_ColorsTransitionToLastNonPhysicalPointOnMyAxis()
    {
        var result = new CScore.CalcResult
        {
            Status = "ok",
            DataJson = """
            {
              "has_mx": false,
              "has_my": true,
              "points": [
                {"mx": 0, "my": 1, "ky": 0, "kz": 1, "converged": true, "non_physical": false},
                {"mx": 0, "my": 2, "ky": 0, "kz": 2, "converged": true, "non_physical": true}
              ]
            }
            """
        };

        var viewModel = new OpenCS.ViewModels.MomentCurvatureBiaxialResultVM(result);

        Assert.Single(viewModel.CurvatureZSeriesFadedParts);
        Assert.Equal(new[] { 1.0, 2.0 }, viewModel.CurvatureZSeriesFadedParts[0].X);
    }

    [Fact]
    public void PointsTable_BindsNonPhysicalStatusAndUsesLocalizedResources()
    {
        string root = FindWorkspaceRoot();
        string viewPath = Path.Combine(root, "OpenCS", "Views", "MomentCurvatureBiaxialResultView.xaml");
        string viewCodePath = Path.Combine(root, "OpenCS", "Views", "MomentCurvatureBiaxialResultView.xaml.cs");
        string ruPath = Path.Combine(root, "OpenCS", "Resources", "Strings.ru-RU.xaml");
        string enPath = Path.Combine(root, "OpenCS", "Resources", "Strings.en-US.xaml");

        string view = File.ReadAllText(viewPath);
        string viewCode = File.ReadAllText(viewCodePath);
        var ru = ResourceKeys(ruPath);
        var en = ResourceKeys(enPath);

        Assert.Contains("Binding=\"{Binding NonPhysical}\"", view);
        Assert.Contains("Binding=\"{Binding LimitStatusText}\"", view);
        Assert.DoesNotContain("Clipped", view);
        Assert.Contains("CurvatureYSeriesFaded", viewCode);
        Assert.Contains("CurvatureZSeriesFaded", viewCode);
        Assert.Contains("AddPlotSeries(_curvatureXPlot, _viewModel.CurvatureYSeriesParts", viewCode);
        Assert.Contains("AddPlotSeries(_curvatureYPlot, _viewModel.CurvatureZSeriesFadedParts", viewCode);
        Assert.Contains("NonPhysicalColor", viewCode);
        Assert.Contains("row.NonPhysical == faded", viewCode);
        Assert.Contains("MomentCurvature_ColLimitStatus", ru);
        Assert.Contains("MomentCurvature_NonPhysicalStatus", ru);
        Assert.Contains("Превышено предельное усилие", File.ReadAllText(ruPath));
        Assert.Contains("MomentCurvature_ColLimitStatus", en);
        Assert.Contains("MomentCurvature_NonPhysicalStatus", en);
    }

    static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenCS.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Не найден корень рабочего пространства OpenCS.");
    }

    static HashSet<string> ResourceKeys(string path)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(path).Root!.Elements()
            .Select(element => (string?)element.Attribute(x + "Key"))
            .Where(key => key is not null)
            .Select(key => key!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
