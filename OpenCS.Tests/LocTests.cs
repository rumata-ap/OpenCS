using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Тесты, меняющие язык Loc, не должны идти параллельно с остальными: те ждут «нет языка → ключ».</summary>
[CollectionDefinition("Loc language", DisableParallelization = true)]
public class LocLanguageCollection;

/// <summary>Loc без WPF: таблицы из вложенных XAML-словарей совпадают с тем, что грузит WPF.</summary>
[Collection("Loc language")]
public class LocTests
{
    // ── Эквивалентность с WPF ─────────────────────────────────────────

    [Theory]
    [InlineData(Loc.Russian)]
    [InlineData(Loc.English)]
    public void Table_MatchesWpfBamlDictionary(string language)
    {
        var wpf = LoadWpfDictionary(language);
        var table = Loc.Table(language);

        Assert.Equal(wpf.Count, table.Count);
        var mismatches = new List<string>();
        foreach (var (key, value) in wpf)
        {
            if (!table.TryGetValue(key, out var ours))
                mismatches.Add($"{key}: нет в таблице");
            else if (!string.Equals(value, ours, StringComparison.Ordinal))
                mismatches.Add($"{key}: WPF [{value}] ≠ Loc [{ours}]");
        }
        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches.Take(20)));
    }

    /// <summary>BAML самого приложения — так словарь читает App.xaml.</summary>
    static Dictionary<string, string> LoadWpfDictionary(string language)
    {
        Dictionary<string, string>? result = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var uri = new Uri($"/OpenCS;component/Resources/Strings.{language}.xaml", UriKind.Relative);
                var dict = (ResourceDictionary)Application.LoadComponent(uri);
                result = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var key in dict.Keys)
                    result[(string)key] = Assert.IsType<string>(dict[key]);
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
        return result!;
    }

    // ── Нормализация пробелов как у XAML ──────────────────────────────

    const string Header = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
              xmlns:system="clr-namespace:System;assembly=mscorlib">
        """;

    static Dictionary<string, string> Parse(string body, string? rootAttributes = null)
    {
        string xaml = rootAttributes == null ? Header : Header.Replace("mscorlib\">", $"mscorlib\" {rootAttributes}>");
        xaml += body + "</ResourceDictionary>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xaml));
        return XamlStringDictionaryReader.Read(stream, "test.xaml");
    }

    [Fact]
    public void Reader_NormalizesWhitespaceLikeXaml()
    {
        var d = Parse("""
            <system:String x:Key="Entity">а:&#x0a;б&#10;в</system:String>
            <system:String x:Key="Literal">Узел
            U</system:String>
            <system:String x:Key="Trailing">Asw: </system:String>
            <system:String x:Key="Double">a  b	c</system:String>
            <system:String x:Key="Preserve" xml:space="preserve">    {0} — {1}&#x0a;</system:String>
            """);
        Assert.Equal("а: б в", d["Entity"]);
        Assert.Equal("Узел U", d["Literal"]);
        Assert.Equal("Asw:", d["Trailing"]);
        Assert.Equal("a b c", d["Double"]);
        Assert.Equal("    {0} — {1}\n", d["Preserve"]);
    }

    [Fact]
    public void Reader_PreserveOnRootAppliesToChildren()
    {
        var d = Parse("""<system:String x:Key="A"> x </system:String>""", "xml:space=\"preserve\"");
        Assert.Equal(" x ", d["A"]);
    }

    [Fact]
    public void Reader_KeysAreCaseSensitive()
    {
        var d = Parse("""
            <system:String x:Key="OK">OK</system:String>
            <system:String x:Key="Ok">Ок</system:String>
            """);
        Assert.Equal("OK", d["OK"]);
        Assert.Equal("Ок", d["Ok"]);
    }

    [Theory]
    [InlineData("""<system:Double x:Key="A">1</system:Double>""")]
    [InlineData("""<system:String>без ключа</system:String>""")]
    [InlineData("""<system:String x:Key="A">1</system:String><system:String x:Key="A">2</system:String>""")]
    [InlineData("""<system:String x:Key="A">a<Run/>b</system:String>""")]
    public void Reader_RejectsNonFlatDictionaries(string body) =>
        Assert.Throws<InvalidDataException>(() => Parse(body));

    // ── Словари проекта ───────────────────────────────────────────────

    [Fact]
    public void Dictionaries_HaveSameKeys()
    {
        var ru = Loc.Table(Loc.Russian).Keys.ToHashSet(StringComparer.Ordinal);
        var en = Loc.Table(Loc.English).Keys.ToHashSet(StringComparer.Ordinal);
        Assert.Empty(ru.Except(en));
        Assert.Empty(en.Except(ru));
    }

    /// <summary>Переносы строк и пробелы у подписей, которые XAML без xml:space="preserve" съедает.</summary>
    [Theory]
    [InlineData(Loc.Russian)]
    [InlineData(Loc.English)]
    public void Dictionaries_KeepIntendedLineBreaksAndSpaces(string language)
    {
        var t = Loc.Table(language);
        foreach (var key in new[] { "LiraTextConfirm", "ScadPluginAccessDenied", "EtaMuTableHint", "FemMeshGroupOfferMembers",
                     "Fem3DNodeResultFormat", "SectionCutTooltipStress", "SectionCutTooltipStrain" })
            Assert.Contains("\n", t[key]);
        foreach (var key in new[] { "ParametricRcAsw", "ParametricRcQsw", "Area", "Sp63NormalVariableSeparator",
                     "FemCsfeaResultErrorPrefix", "TotalCurvature_LabelNColon", "TotalCurvature_LabelKFullColon" })
            Assert.EndsWith(" ", t[key]);
        Assert.Contains("  (", t["ShellStrainForcesDesc"]);
    }

    [Fact]
    public void LiteralKeysInSources_ExistInBothDictionaries()
    {
        string root = FindRepoRoot();
        var literal = new Regex(@"Loc\.S\(\s*""([^""]+)""\s*[,)]", RegexOptions.Compiled);
        var missing = new List<string>();
        int count = 0;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "OpenCS"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;
            foreach (Match m in literal.Matches(File.ReadAllText(file)))
            {
                count++;
                string key = m.Groups[1].Value;
                foreach (var language in Loc.Languages)
                    if (!Loc.Table(language).ContainsKey(key))
                        missing.Add($"{Path.GetFileName(file)}: {key} ({language})");
            }
        }
        Assert.True(count > 1000, $"найдено лишь {count} вызовов — сломан поиск исходников");
        Assert.True(missing.Count == 0, string.Join("\n", missing.Distinct().Take(30)));
    }

    static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "OpenCS.sln")))
                return dir.FullName;
        throw new DirectoryNotFoundException("Не найден каталог с OpenCS.sln");
    }

    // ── API ───────────────────────────────────────────────────────────

    [Fact]
    public void S_WithoutLanguage_ReturnsKeyOrFallback()
    {
        Assert.Null(Loc.Language);
        Assert.Equal("Delete", Loc.S("Delete"));
        Assert.Equal("запас", Loc.S("Delete", "запас"));
        Assert.False(Loc.TryGet("Delete", out _));
    }

    [Fact]
    public void SetLanguage_SwitchesTextsAndBackToKeys()
    {
        try
        {
            Loc.SetLanguage(Loc.Russian);
            Assert.Equal("Удалить", Loc.S("Delete"));
            Loc.SetLanguage(Loc.English);
            Assert.Equal(Loc.English, Loc.Language);
            Assert.Equal("Delete", Loc.S("Delete"));
            Assert.Equal("NoSuchKey_42", Loc.S("NoSuchKey_42"));
            Assert.Equal("запас", Loc.S("NoSuchKey_42", "запас"));
            Assert.Throws<ArgumentException>(() => Loc.SetLanguage("de-DE"));
            Assert.Equal(Loc.English, Loc.Language);
        }
        finally
        {
            Loc.SetLanguage(null);
        }
        Assert.Equal("Delete", Loc.S("Delete"));
    }

    [Fact]
    public void S_IsSafeWhileLanguageSwitches()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "Delete", Loc.Table(Loc.Russian)["Delete"], Loc.Table(Loc.English)["Delete"],
        };
        try
        {
            using var cts = new CancellationTokenSource();
            var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                while (!cts.IsCancellationRequested)
                    Assert.Contains(Loc.S("Delete"), allowed);
            })).ToArray();
            for (int i = 0; i < 2000; i++)
                Loc.SetLanguage((i % 3) switch { 0 => Loc.Russian, 1 => Loc.English, _ => null });
            cts.Cancel();
            Task.WaitAll(readers);
        }
        finally
        {
            Loc.SetLanguage(null);
        }
    }
}
