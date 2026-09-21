using System.Xml.Linq;
using CScore;
using OpenCS.ViewModels;
using OpenCS.Views;

using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверяет локализацию, bindings и подключение CDP-окна к дереву.</summary>
public sealed class AbaqusCdpExportResourcesTests
{
    static readonly string[] RequiredKeys =
    [
        "ExportAbaqusCdp", "AbaqusCdpExportTitle", "AbaqusCdpCalculationType",
        "AbaqusCdpUnitProfile", "AbaqusCdpStressUnit", "AbaqusCdpLengthUnit",
        "AbaqusCdpForceUnit", "AbaqusCdpEnergyUnit", "AbaqusCdpMaterial",
        "AbaqusCdpMaterialName", "AbaqusCdpElasticModulus", "AbaqusCdpPoissonRatio",
        "AbaqusCdpDilationAngle", "AbaqusCdpEccentricity", "AbaqusCdpFb0Fc0",
        "AbaqusCdpK", "AbaqusCdpViscosity", "AbaqusCdpFractureEnergy",
        "AbaqusCdpElementLength", "AbaqusCdpInitialCompressionRatio",
        "AbaqusCdpCompressionEtaMin", "AbaqusCdpSourceEkb", "AbaqusCdpKeywordTab",
        "AbaqusCdpTsvTab", "AbaqusCdpCopyKeyword", "AbaqusCdpCopyTsv",
        "AbaqusCdpClose", "AbaqusCdpInvalidInput", "AbaqusCdpCopyFailed",
        "AbaqusCdpNotConcrete", "AbaqusCdpProfileMpaMmN", "AbaqusCdpProfileKpaMKN",
        "AbaqusCdpProfilePaMN", "AbaqusCdpProfileCustom", "AbaqusCdpCustomStressScale",
        "AbaqusCdpCustomStressUnit", "AbaqusCdpCustomLengthUnit",
        "AbaqusCdpCustomForceUnit", "AbaqusCdpCustomEnergyUnit",
        "AbaqusSteelExportTitle", "AbaqusSteelYieldPlateau", "AbaqusSteelSource"
    ];

    [Fact]
    public void ResourceDictionaries_ContainEveryCdpKeyInBothLanguages()
    {
        string root = FindWorkspaceRoot();
        string ru = File.ReadAllText(Path.Combine(root, "OpenCS", "Resources", "Strings.ru-RU.xaml"));
        string en = File.ReadAllText(Path.Combine(root, "OpenCS", "Resources", "Strings.en-US.xaml"));
        var ruKeys = Keys(ru);
        var enKeys = Keys(en);

        Assert.All(RequiredKeys, key =>
        {
            Assert.Contains(key, ruKeys);
            Assert.Contains(key, enKeys);
        });
    }

    [Fact]
    public void ExportWindow_UsesDynamicResourcesAndExpectedBindings()
    {
        string root = FindWorkspaceRoot();
        string window = File.ReadAllText(Path.Combine(root, "OpenCS", "Views", "AbaqusCdpExportWindow.xaml"));
        string main = File.ReadAllText(Path.Combine(root, "OpenCS", "MainWindow.xaml"));
        string codeBehind = File.ReadAllText(Path.Combine(root, "OpenCS", "MainWindow.xaml.cs"));

        Assert.Contains("{DynamicResource AbaqusCdpExportTitle}", window);
        Assert.Contains("{Binding KeywordText}", window);
        Assert.Contains("{Binding TsvText}", window);
        Assert.Contains("{Binding CopyKeywordCommand}", window);
        Assert.Contains("{Binding CopyTsvCommand}", window);
        Assert.Contains("{DynamicResource", window);
        Assert.Contains("ExportAbaqusCdp", main);
        Assert.Contains("Click=\"ExportAbaqusCdp_Click\"", main);
        Assert.Contains("CommandParameter=\"{Binding}\"", main);
        Assert.Contains("MatType.Concrete", codeBehind);
        Assert.Contains("AbaqusCdpExportWindow", codeBehind);
    }

    [Fact]
    public void ExportWindow_CreatesViewModelOnStaThread()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new AbaqusCdpExportWindow(CreateConcrete());
                Assert.IsType<AbaqusCdpExportVM>(window.DataContext);
                window.Close();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }

    static HashSet<string> Keys(string text)
    {
        var document = XDocument.Parse(text);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document.Descendants().Attributes(x + "Key")
            .Select(attribute => attribute.Value)
            .ToHashSet(StringComparer.Ordinal);
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

    static Material CreateConcrete()
    {
        var material = new Material { Tag = "B25", Type = MatType.Concrete };
        material.MaterialChars =
        [
            Chars(CalcType.C, -14500, 1050),
            Chars(CalcType.CL, -13050, 1050),
            Chars(CalcType.N, -18500, 1550),
            Chars(CalcType.NL, -18500, 1550)
        ];
        return material;
    }

    static MaterialChars Chars(CalcType calcType, double fc, double ft) => new()
    {
        Type = MatType.Concrete,
        TypeCalc = calcType,
        Fc = fc,
        Ft = ft,
        E = 30_000_000,
        Ec0 = -0.002,
        Ec1 = -0.00029,
        Ec2 = -0.0035,
        Et0 = 0.0001,
        Et1 = 0.000021,
        Et2 = 0.00015
    };
}
