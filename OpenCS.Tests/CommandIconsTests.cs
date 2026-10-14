using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using OpenCS.Views.Helpers;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Иконки команд МКЭ-схемы: словарь соответствий, ссылки в разметке и коде, загрузка ресурсов.</summary>
public class CommandIconsTests
{
    static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>Файлы, где у каждого пункта меню МКЭ должна быть иконка.</summary>
    static readonly string[] FemMenuFiles =
    [
        "OpenCS/MainWindow.xaml",
        "OpenCS/Views/FemSchemaView3D.xaml",
        "OpenCS/Views/FemBarsView.xaml",
        "OpenCS/Views/FemShellsView.xaml",
        "OpenCS/Views/FemMeshNodesView.xaml",
        "OpenCS/Views/FemAnalysisResultView.xaml",
    ];

    /// <summary>Меню, собираемые в коде.</summary>
    static readonly string[] FemMenuCodeFiles =
    [
        "OpenCS/Views/FemMemberTreeMenus.cs",
        "OpenCS/Views/FemSchemaView3D.xaml.cs",
        "OpenCS/Views/FemGroupMenus.cs",
    ];

    [Fact]
    public void EveryMappedIcon_IsDefined()
    {
        var defined = IconKeys();
        var missing = CommandIcons.ByHeaderKey.Values.Distinct().Where(k => !defined.Contains(k)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void EveryMappedHeaderKey_IsLocalized()
    {
        var root = FindWorkspaceRoot();
        foreach (var file in new[] { "Strings.ru-RU.xaml", "Strings.en-US.xaml" })
        {
            var strings = XDocument.Load(Path.Combine(root, "OpenCS.App", "Resources", file)).Descendants()
                .Attributes(X + "Key").Select(a => a.Value).ToHashSet(StringComparer.Ordinal);
            var missing = CommandIcons.ByHeaderKey.Keys.Where(k => !strings.Contains(k)).ToList();
            Assert.True(missing.Count == 0, $"{file}: нет строк {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void EveryCmdIconReference_InSources_IsDefined()
    {
        var root = FindWorkspaceRoot();
        var defined = IconKeys();
        var sources = Directory.EnumerateFiles(Path.Combine(root, "OpenCS"), "*.*", SearchOption.AllDirectories)
            .Where(p => (p.EndsWith(".cs") || p.EndsWith(".xaml"))
                        && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !p.EndsWith("FemCommandIcons.xaml"));
        var missing = new List<string>();
        foreach (var path in sources)
            foreach (Match m in Regex.Matches(File.ReadAllText(path), @"CmdIcon_\w+"))
                if (!defined.Contains(m.Value)) missing.Add($"{Path.GetFileName(path)}: {m.Value}");
        Assert.Empty(missing);
    }

    [Fact]
    public void FemMenuItems_InXaml_UseCommandIconsHeader()
    {
        var root = FindWorkspaceRoot();
        var bare = new List<string>();
        foreach (var file in FemMenuFiles)
        {
            var text = File.ReadAllText(Path.Combine(root, file));
            foreach (Match m in Regex.Matches(text, @"<MenuItem\b[^>]*Header=""\{DynamicResource ((?:Fem|RunFem)\w*)\}"""))
                bare.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");
            foreach (Match m in Regex.Matches(text, @"CommandIcons\.Header=""(\w+)"""))
                if (!CommandIcons.ByHeaderKey.ContainsKey(m.Groups[1].Value))
                    bare.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value} (нет в словаре)");
        }
        Assert.Empty(bare);
    }

    [Fact]
    public void KeysPassedToCommandIcons_InCode_AreMapped()
    {
        var root = FindWorkspaceRoot();
        var unmapped = new List<string>();
        foreach (var file in FemMenuCodeFiles)
        {
            var text = File.ReadAllText(Path.Combine(root, file));
            var keys = Regex.Matches(text, @"CommandIcons\.Item\(""(\w+)""\)").Select(m => m.Groups[1].Value)
                .Concat(Regex.Matches(text, @"\},\s*""(\w+)""\)").Select(m => m.Groups[1].Value));
            unmapped.AddRange(keys.Where(k => !CommandIcons.ByHeaderKey.ContainsKey(k))
                .Select(k => $"{Path.GetFileName(file)}: {k}"));
        }
        Assert.Empty(unmapped);
    }

    [Fact]
    public void CommandIconsDictionary_LoadsAllIconsAsImages()
    {
        var root = FindWorkspaceRoot();
        var path = Path.Combine(root, "OpenCS", "Resources", "FemCommandIcons.xaml");
        Exception? error = null;
        List<string> notImages = [];
        int count = 0;
        var thread = new Thread(() =>
        {
            try
            {
                using var stream = File.OpenRead(path);
                var dict = (ResourceDictionary)XamlReader.Load(stream, new ParserContext { BaseUri = new Uri(path) });
                foreach (var key in dict.Keys.OfType<string>().Where(k => k.StartsWith("CmdIcon_")))
                {
                    count++;
                    if (dict[key] is not DrawingImage) notImages.Add(key);
                }
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
        Assert.Empty(notImages);
        Assert.Equal(IconKeys().Count, count);
    }

    static HashSet<string> IconKeys()
    {
        var path = Path.Combine(FindWorkspaceRoot(), "OpenCS", "Resources", "FemCommandIcons.xaml");
        return XDocument.Load(path).Descendants().Attributes(X + "Key").Select(a => a.Value)
            .Where(k => k.StartsWith("CmdIcon_")).ToHashSet(StringComparer.Ordinal);
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
