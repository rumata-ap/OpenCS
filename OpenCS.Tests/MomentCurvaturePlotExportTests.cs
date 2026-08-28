using Xunit;

namespace OpenCS.Tests;

/// <summary>Регрессионные проверки экспорта графиков результата «кривизна–момент».</summary>
public sealed class MomentCurvaturePlotExportTests
{
    [Fact]
    public void ResultView_ConfiguresExportMenuForEveryPlot()
    {
        string root = FindWorkspaceRoot();
        string plotCanvas = File.ReadAllText(Path.Combine(root, "OpenCS", "Views", "PlotCanvas.cs"));
        string resultView = File.ReadAllText(Path.Combine(root, "OpenCS", "Views", "MomentCurvatureBiaxialResultView.xaml.cs"));

        Assert.Contains("ConfigureExportMenu", plotCanvas);
        Assert.Contains("SectionCutEmfClipboard.TryCopy", plotCanvas);
        Assert.Contains("SectionCutExporter.ExportPng", plotCanvas);
        Assert.Contains("MomentCurvature_CsvColKy", resultView);
        Assert.Contains("MomentCurvature_CsvColKz", resultView);
        Assert.Contains("MomentCurvature_CsvColPsiActive", resultView);
        // 11 = 2 кривизна-момент + 3 жёсткости + 6 арматуры (деформации и напряжения
        // × полный момент / Mx / My).
        Assert.Equal(11, Count(resultView, ".ConfigureExportMenu("));
        Assert.Equal(2, Count(resultView, "exportCsv: ExportPointsCsv"));
    }

    [Fact]
    public void CsvStiffnessHeaders_AreCompatibleWithWindows1251()
    {
        string root = FindWorkspaceRoot();
        string ru = File.ReadAllText(Path.Combine(root, "OpenCS", "Resources", "Strings.ru-RU.xaml"));
        string en = File.ReadAllText(Path.Combine(root, "OpenCS", "Resources", "Strings.en-US.xaml"));
        string resultView = File.ReadAllText(Path.Combine(root, "OpenCS", "Views", "MomentCurvatureBiaxialResultView.xaml.cs"));

        foreach (string key in new[]
        {
            "MomentCurvature_CsvColNStiffnessRatio",
            "MomentCurvature_CsvColMxStiffnessRatio",
            "MomentCurvature_CsvColMyStiffnessRatio"
        })
        {
            Assert.Contains(key, ru);
            Assert.Contains(key, en);
            Assert.Contains($"Loc.S(\"{key}\")", resultView);
        }

        Assert.Contains("EA/EA0", ru);
        Assert.Contains("B/B0x", ru);
        Assert.Contains("B/B0y", ru);
    }

    static int Count(string source, string value)
    {
        int count = 0;
        for (int index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
            count++;
        return count;
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
}
