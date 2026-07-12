using CScore.Import;
using Xunit;

namespace CScore.Tests;

public class ScadXlsForceImporterTests
{
    static ScadXlsSheetData BarLoadCaseSheet() => new()
    {
        Name = "20. Величины усилий",
        Cells =
        [
            ["Величины усилий"],
            ["Единицы измерения:"],
            ["   - Силы: Т"],
            ["   - Единицы длины для силовых факторов: м"],
            ["Параметры выборки:"],
            ["Список узлов/элементов: Все"],
            ["Список сечений: Все"],
            ["Список загружений/комбинаций: Все"],
            ["Список факторов: Все"],
            ["Элемент", "Сечение", "Загружение", "Номер формы ", "Значение"],
            ["", "", "", "", "N", "Mk", "My", "Qz", "Mz", "Qy"],
            ["1", "1", "2", "", "1.0", "0.1", "0.2", "0.3", "0.4", "0.5"],
            ["1", "1", "2", "M1", "9", "9", "9", "9", "9", "9"],
            ["1", "2", "2", "LS+SD", "2.0", "0", "0", "0", "0", "0"],
            ["99", "1", "2", "", "7", "0", "0", "0", "0", "0"],
            ["1", "1", "3", "", "3.0", "0", "0", "0", "0", "0"],
        ],
    };

    static ScadXlsSheetData PlateSheet() => new()
    {
        Name = "25. Величины усилий",
        Cells =
        [
            ["Величины усилий"],
            ["Элемент", "Сечение", "Загружение", "Номер формы ", "Значение"],
            ["", "", "", "", "sX", "sY", "txy", "Mx", "My", "Mxy"],
            ["15", "Центр", "2", "", "1", "1", "1", "1", "1", "1"],
        ],
    };

    [Fact]
    public void ImportBarSheets_LoadCases_GroupsByLoadCase_FiltersFormsAndElements()
    {
        var opt = new ScadXlsImportOptions
        {
            TonToKnFactor = 1.0,
            InvertBarBendingMoments = false,
            ElementIds = new HashSet<int> { 1 },
        };
        var names = new Dictionary<int, string> { [2] = "DEAD", [3] = "LIVE" };

        var result = ScadXlsForceImporter.ImportBarSheets(
            ScadXlsImportMode.LoadCases, opt, names,
            [BarLoadCaseSheet(), PlateSheet()]);

        Assert.Null(result.Error);
        Assert.Equal(2, result.ForceSets.Count);
        var dead = Assert.Single(result.ForceSets, fs => fs.Tag == "DEAD");
        Assert.Equal("bar", dead.Kind);
        Assert.Equal("scad", dead.SourceType);
        Assert.Equal(2, dead.Items.Count);
        Assert.Contains(dead.Items, i => i.Label == "1_С1" && i.N == 1.0);
        Assert.Contains(dead.Items, i => i.Label == "1_С2 LS+SD" && i.N == 2.0);
        Assert.DoesNotContain(dead.Items, i => i.N == 9);

        var live = Assert.Single(result.ForceSets, fs => fs.Tag == "LIVE");
        Assert.Single(live.Items);
        Assert.Equal(3.0, live.Items[0].N);
    }

    [Fact]
    public void ImportBarSheets_LoadCases_WithoutFormColumn_TreatsAsStatic()
    {
        // Как в «Краеведческий музей»: нет колонки «форм», иначе ColForm=load+1 брал N и отбрасывал всё.
        var sheet = new ScadXlsSheetData
        {
            Name = "20. Величины усилий",
            Cells =
            [
                ["Величины усилий"],
                ["Элемент", "Сечение", "Загружение", "Значение"],
                ["", "", "", "N", "Mk", "My", "Qz", "Mz", "Qy"],
                ["1", "1", "2", "1.0", "0.1", "0.2", "0.3", "0.4", "0.5"],
                ["50", "1", "2", "4.0", "0", "0", "0", "0", "0"],
            ],
        };
        var opt = new ScadXlsImportOptions
        {
            TonToKnFactor = 1.0,
            InvertBarBendingMoments = false,
            ElementIds = new HashSet<int> { 1, 50 },
        };
        var result = ScadXlsForceImporter.ImportBarSheets(
            ScadXlsImportMode.LoadCases, opt,
            new Dictionary<int, string> { [2] = "DEAD" },
            [sheet]);

        Assert.Null(result.Error);
        Assert.Null(result.Warning);
        var fs = Assert.Single(result.ForceSets);
        Assert.Equal(2, fs.Items.Count);
        Assert.Contains(fs.Items, i => i.Label == "1_С1" && i.N == 1.0);
        Assert.Contains(fs.Items, i => i.Label == "50_С1" && i.N == 4.0);
    }

    [Fact]
    public void ImportBarSheets_EmptyFilter_ReturnsError()
    {
        var opt = new ScadXlsImportOptions { ElementIds = new HashSet<int>() };
        var result = ScadXlsForceImporter.ImportBarSheets(
            ScadXlsImportMode.LoadCases, opt, new Dictionary<int, string>(),
            [BarLoadCaseSheet()]);
        Assert.NotNull(result.Error);
        Assert.Empty(result.ForceSets);
    }

    [Fact]
    public void ImportBarSheets_ImportAllElements_IncludesAllBars()
    {
        var opt = new ScadXlsImportOptions
        {
            TonToKnFactor = 1.0,
            InvertBarBendingMoments = false,
            ImportAllElements = true,
            ElementIds = new HashSet<int>(),
        };
        var result = ScadXlsForceImporter.ImportBarSheets(
            ScadXlsImportMode.LoadCases, opt,
            new Dictionary<int, string> { [2] = "DEAD" },
            [BarLoadCaseSheet()]);

        Assert.Null(result.Error);
        var dead = Assert.Single(result.ForceSets, fs => fs.Tag == "DEAD");
        Assert.Contains(dead.Items, i => i.Label.StartsWith("99_"));
        Assert.Contains(dead.Items, i => i.Label.StartsWith("1_"));
    }

    [Fact]
    public void ImportBarSheets_Rsu_GroupsByType_CarryForwardElement()
    {
        var sheet = new ScadXlsSheetData
        {
            Name = "63. РСУ",
            Cells =
            [
                ["РСУ с автоматическим выбором"],
                ["Единицы измерения:"],
                ["   - Силы: Т"],
                ["УНГ", "Элем.", "Сеч.", "СТ", "Крит.", "Вид", "Значение", "", "", "", "", "", "Тип"],
                ["", "", "", "", "", "", "N", "Mk", "My", "Qz", "Mz", "Qy"],
                ["--", "10", "1", "1", "1", "1", "1.5", "0", "0.2", "-1", "-0.1", "-0.5", "C"],
                ["--", "", "1", "1", "2", "1", "2.5", "0", "0", "0", "0", "0", "C"],
                ["--", "10", "2", "1", "1", "1", "3.5", "0", "0", "0", "0", "0", "CL"],
                ["--", "99", "1", "1", "1", "1", "9", "0", "0", "0", "0", "0", "C"],
            ],
        };
        var opt = new ScadXlsImportOptions
        {
            TonToKnFactor = 1.0,
            InvertBarBendingMoments = false,
            ElementIds = new HashSet<int> { 10 },
        };
        var result = ScadXlsForceImporter.ImportBarSheets(
            ScadXlsImportMode.Rsu, opt, new Dictionary<int, string>(), [sheet]);

        Assert.Null(result.Error);
        Assert.Equal(2, result.ForceSets.Count);
        var c = Assert.Single(result.ForceSets, fs => fs.Tag == "РСУ_C");
        Assert.Equal(2, c.Items.Count);
        Assert.Contains(c.Items, i => i.Label == "10_С1_К1" && i.N == 1.5);
        Assert.Contains(c.Items, i => i.Label == "10_С1_К2" && i.N == 2.5);
        var cl = Assert.Single(result.ForceSets, fs => fs.Tag == "РСУ_CL");
        Assert.Single(cl.Items);
        Assert.Equal("10_С2_К1", cl.Items[0].Label);
    }

    [Fact]
    public void ImportBarSheets_LoadCases_ImportsShellAlongsideBar()
    {
        var opt = new ScadXlsImportOptions
        {
            TonToKnFactor = 1.0,
            InvertBarBendingMoments = false,
            ElementIds = new HashSet<int> { 1, 15 },
            DefaultThicknessM = 0.2,
        };
        var names = new Dictionary<int, string> { [2] = "DEAD" };

        var result = ScadXlsForceImporter.ImportBarSheets(
            ScadXlsImportMode.LoadCases, opt, names,
            [BarLoadCaseSheet(), PlateSheet()]);

        Assert.Null(result.Error);
        var bar = Assert.Single(result.ForceSets, fs => fs.Tag == "DEAD" && fs.Kind == "bar");
        Assert.True(bar.Items.Count >= 1);
        var shell = Assert.Single(result.ForceSets, fs => fs.Tag == "DEAD" && fs.Kind == "shell");
        Assert.Single(shell.ShellItems);
        Assert.Equal("15_Центр", shell.ShellItems[0].Label);
        Assert.Equal(0.2, shell.ShellItems[0].Nx); // sX=1 × h=0.2
        Assert.Equal(0.0, shell.ShellItems[0].Qx);
    }

    [Fact]
    public void ImportBarSheets_Rsu_ImportsShellWithQxQy()
    {
        var sheet = new ScadXlsSheetData
        {
            Name = "65. РСУ",
            Cells =
            [
                ["РСУ с автоматическим выбором"],
                ["УНГ", "Элем.", "Сеч.", "СТ", "Крит.", "Вид", "Значение", "", "", "", "", "", "", "", "Тип"],
                ["", "", "", "", "", "", "sX", "sY", "txy", "Mx", "My", "Mxy", "Qx", "Qy"],
                ["--", "20", "Центр", "1", "1", "1", "1.1", "2.2", "0.3", "0.4", "0.5", "0.6", "0.7", "0.8", "C"],
                ["--", "", "Центр", "1", "2", "1", "9", "0", "0", "0", "0", "0", "0", "0", "C"],
                ["--", "99", "Центр", "1", "1", "1", "8", "0", "0", "0", "0", "0", "0", "0", "C"],
            ],
        };
        var opt = new ScadXlsImportOptions
        {
            TonToKnFactor = 1.0,
            ElementIds = new HashSet<int> { 20 },
            DefaultThicknessM = 0.5,
            ElementThicknessM = new Dictionary<int, double> { [20] = 0.2 }, // per-КЭ перекрывает default
        };
        var result = ScadXlsForceImporter.ImportBarSheets(
            ScadXlsImportMode.Rsu, opt, new Dictionary<int, string>(), [sheet]);

        Assert.Null(result.Error);
        var fs = Assert.Single(result.ForceSets);
        Assert.Equal("shell", fs.Kind);
        Assert.Equal("РСУ_C", fs.Tag);
        Assert.Equal(2, fs.ShellItems.Count);
        Assert.Contains(fs.ShellItems, i => i.Label == "20_Центр_К1" && i.Nx == 1.1 * 0.2 && i.Qx == 0.7 && i.Qy == 0.8);
        Assert.Contains(fs.ShellItems, i => i.Label == "20_Центр_К2");
    }

    [Fact]
    public void ResolveThicknessM_PrefersElementMap_ThenDefault()
    {
        var opt = new ScadXlsImportOptions
        {
            DefaultThicknessM = 0.3,
            ElementThicknessM = new Dictionary<int, double> { [5] = 0.15 },
        };
        Assert.Equal(0.15, opt.ResolveThicknessM(5));
        Assert.Equal(0.3, opt.ResolveThicknessM(9));
    }

    static ScadXlsSheetData BarCombinationSheet() => new()
    {
        Name = "24. Величины усилий от комбина",
        Cells =
        [
            ["Величины усилий от комбинаций загружений"],
            ["Элемент", "Сечение", "Комбинация", "Значение"],
            ["", "", "", "N", "Mk", "My", "Qz", "Mz", "Qy"],
            ["1", "1", "1", "1.0", "0.1", "0.2", "0.3", "0.4", "0.5"],
            ["1", "2", "2", "2.0", "0", "0", "0", "0", "0"],
            ["99", "1", "1", "9", "0", "0", "0", "0", "0"],
        ],
    };

    static ScadXlsSheetData ShellCombinationSheet() => new()
    {
        Name = "26. Величины усилий от комбина",
        Cells =
        [
            ["Величины усилий от комбинаций загружений"],
            ["Элемент", "Сечение", "Комбинация", "Значение"],
            ["", "", "", "sX", "sY", "txy", "Mx", "My", "Mxy", "Qx", "Qy"],
            ["15", "Центр", "1", "1", "1", "1", "1", "1", "1", "0.1", "0.2"],
        ],
    };

    [Fact]
    public void ImportBarSheets_Combinations_GroupsByNumber_FallbackTag()
    {
        var opt = new ScadXlsImportOptions
        {
            TonToKnFactor = 1.0,
            InvertBarBendingMoments = false,
            ElementIds = new HashSet<int> { 1, 15 },
            DefaultThicknessM = 0.2,
        };
        var result = ScadXlsForceImporter.ImportBarSheets(
            ScadXlsImportMode.Combinations, opt,
            new Dictionary<int, string>(),
            [BarCombinationSheet(), ShellCombinationSheet()]);

        Assert.Null(result.Error);
        Assert.Null(result.Warning);
        var rsn1Bar = Assert.Single(result.ForceSets, fs => fs.Tag == "РСН 1" && fs.Kind == "bar");
        Assert.Single(rsn1Bar.Items);
        Assert.Equal(1.0, rsn1Bar.Items[0].N);
        Assert.Equal("1_С1", rsn1Bar.Items[0].Label);

        var rsn2 = Assert.Single(result.ForceSets, fs => fs.Tag == "РСН 2");
        Assert.Equal("bar", rsn2.Kind);
        Assert.Equal(2.0, Assert.Single(rsn2.Items).N);

        var shell = Assert.Single(result.ForceSets, fs => fs.Kind == "shell");
        Assert.Equal("РСН 1", shell.Tag);
        Assert.Equal(0.2, Assert.Single(shell.ShellItems).Nx, 12);
    }

    [Fact]
    public void ImportBarSheets_Combinations_UsesNamesFromDictionary()
    {
        var opt = new ScadXlsImportOptions
        {
            TonToKnFactor = 1.0,
            InvertBarBendingMoments = false,
            ElementIds = new HashSet<int> { 1 },
        };
        var names = new Dictionary<int, string> { [1] = "1.2*L1+L2" };
        var result = ScadXlsForceImporter.ImportBarSheets(
            ScadXlsImportMode.Combinations, opt, names, [BarCombinationSheet()]);

        Assert.Contains(result.ForceSets, fs => fs.Tag == "1.2*L1+L2");
        Assert.DoesNotContain(result.ForceSets, fs => fs.Tag == "РСН 1");
    }

    [Fact]
    public void ImportBarSheets_LoadCases_IgnoresCombinationSheets()
    {
        var opt = new ScadXlsImportOptions
        {
            TonToKnFactor = 1.0,
            ElementIds = new HashSet<int> { 1 },
        };
        var result = ScadXlsForceImporter.ImportBarSheets(
            ScadXlsImportMode.LoadCases, opt, new Dictionary<int, string>(),
            [BarCombinationSheet()]);

        Assert.Equal("В файле не найдено листов с усилиями стержней/пластин.", result.Error);
        Assert.Empty(result.ForceSets);
    }

    [Fact]
    public void ImportBarSheets_Combinations_IgnoresLoadCaseSheets()
    {
        var opt = new ScadXlsImportOptions
        {
            TonToKnFactor = 1.0,
            InvertBarBendingMoments = false,
            ElementIds = new HashSet<int> { 1 },
        };
        var result = ScadXlsForceImporter.ImportBarSheets(
            ScadXlsImportMode.Combinations, opt, new Dictionary<int, string>(),
            [BarLoadCaseSheet()]);

        Assert.Equal("В файле не найдено листов с усилиями стержней/пластин.", result.Error);
    }
}
