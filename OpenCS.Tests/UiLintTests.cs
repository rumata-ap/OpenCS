using System.IO;
using System.Text;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Статическая проверка XAML окон по описи: отчёт tmp/ui-lint.md, строгий режим для закрытых разделов.</summary>
[Trait("Category", "UiLint")]
public sealed class UiLintTests
{
    static readonly HashSet<string> NoAllow = [];

    static List<UiLintFinding> Check(string xaml) => UiLint.Check("x.xaml", xaml, NoAllow).ToList();

    [Fact]
    public void Literal_CyrillicAndLatinWords_Flagged_ResourcesAndSymbolsNot()
    {
        var f = Check("""
            <UserControl>
              <TextBlock Text="Норма:"/>
              <Button Content="OK"/>
              <TextBlock Text="{DynamicResource Norm}"/>
              <TextBlock Text="C:"/>
            </UserControl>
            """);
        Assert.Equal(["Норма:", "OK"], f.Where(x => x.Rule == "literal").Select(x => x.Text));
        Assert.Equal(2, f.First(x => x.Text == "Норма:").Line);
    }

    [Fact]
    public void Literal_AttachedPropertyResourceKey_NotFlagged()
    {
        Assert.Empty(Check("""<MenuItem helpers:CommandIcons.Header="FemSchemaNew" Command="{Binding X}"/>"""));
    }

    [Fact]
    public void Literal_AllowListed_NotFlagged()
    {
        var f = UiLint.Check("x.xaml", """<UserControl><TextBlock Text="CL"/></UserControl>""", new HashSet<string> { "CL" });
        Assert.Empty(f);
    }

    [Fact]
    public void Window_ButtonsWithoutDefaultCancel_AndNoMinSize_Flagged()
    {
        var f = Check("""
            <Window x:Class="A" Title="{DynamicResource T}">
              <Button Content="{DynamicResource OK}" Click="Ok"/>
            </Window>
            """);
        Assert.Equal(new[] { "no-cancel", "no-default", "no-min-size" }, f.Select(x => x.Rule).Order());
    }

    [Fact]
    public void Window_Complete_NoFindings()
    {
        var f = Check("""
            <?xml version="1.0"?>
            <Window x:Class="A" MinWidth="300" MinHeight="200">
              <Button Content="{DynamicResource OK}" IsDefault="True"/>
              <Button Content="{DynamicResource Cancel}" IsCancel="True"/>
            </Window>
            """);
        Assert.Empty(f);
    }

    [Fact]
    public void MainWindow_NoDefaultCancelRule_ButMinSizeRule()
    {
        var f = Check("""
            <Window x:Class="OpenCS.MainWindow" Title="{Binding T}">
              <Button Content="{DynamicResource Add}" Click="Add"/>
            </Window>
            """);
        Assert.Equal(["no-min-size"], f.Select(x => x.Rule));
    }

    [Fact]
    public void Window_NoResize_NeedsNoMinSize()
    {
        Assert.Empty(Check("""<Window x:Class="A" ResizeMode="NoResize"><TextBlock Text="{Binding X}"/></Window>"""));
    }

    [Fact]
    public void WritesReport_StrictForClosedSections()
    {
        var inv = UiInventory.Load();
        var allow = UiLint.LoadAllow(UiInventory.RepoRoot);
        var report = new StringBuilder("# UiLint\n\n");
        var strict = new List<string>();
        foreach (var section in inv.Sections)
        {
            var findings = inv.Operations.Where(o => o.Section == section)
                .SelectMany(o => o.Xaml)
                .SelectMany(x => UiLint.Check(x, File.ReadAllText(Path.Combine(UiInventory.RepoRoot, x)), allow))
                .ToList();
            report.Append($"## {section} — {findings.Count}\n\n");
            foreach (var f in findings)
                report.Append($"- `{f.File}:{f.Line}` **{f.Rule}** {f.Text}\n");
            report.Append('\n');
            if (inv.ClosedSections.Contains(section))
                strict.AddRange(findings.Select(f => $"{f.File}:{f.Line} {f.Rule} {f.Text}"));
        }
        string dir = Path.Combine(UiInventory.RepoRoot, "tmp");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "ui-lint.md"), report.ToString());
        Assert.True(strict.Count == 0, "Закрытые разделы нарушают UiLint:\n" + string.Join("\n", strict));
    }
}
