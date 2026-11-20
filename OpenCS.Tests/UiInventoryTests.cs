using System.IO;
using System.Text;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Опись окон GUI полна и согласована; из неё пишется отчёт покрытия tmp/ui-coverage.md.</summary>
public sealed class UiInventoryTests
{
    [Fact]
    public void EveryWindowClassIsListed()
    {
        var listed = UiInventory.Load().Operations.Select(o => o.Window).ToHashSet();
        var windows = typeof(OpenCS.Views.SettingsWindow).Assembly.GetTypes()
            .Where(t => typeof(System.Windows.Window).IsAssignableFrom(t) && !t.IsAbstract)
            .Select(t => t.Name).Distinct().OrderBy(n => n).ToList();
        var missing = windows.Where(w => !listed.Contains(w)).ToList();
        Assert.True(missing.Count == 0,
            "Нет в описи tools/ui-inventory/operations.json: " + string.Join(", ", missing));
    }

    [Fact]
    public void EntriesAreConsistent()
    {
        var inv = UiInventory.Load();
        var problems = new List<string>();
        foreach (var dup in inv.Operations.GroupBy(o => o.Window).Where(g => g.Count() > 1))
            problems.Add($"{dup.Key}: повтор в описи");
        foreach (var op in inv.Operations)
        {
            if (!inv.Sections.Contains(op.Section)) problems.Add($"{op.Window}: неизвестный раздел «{op.Section}»");
            if (!UiInventory.Kinds.Contains(op.Kind)) problems.Add($"{op.Window}: неизвестный вид «{op.Kind}»");
            if (!UiInventory.AuditStates.Contains(op.Audit)) problems.Add($"{op.Window}: неизвестное состояние «{op.Audit}»");
            foreach (var x in op.Xaml)
                if (!File.Exists(Path.Combine(UiInventory.RepoRoot, x))) problems.Add($"{op.Window}: нет файла {x}");
        }
        foreach (var closed in inv.ClosedSections)
            foreach (var op in inv.Operations.Where(o => o.Section == closed && o.Audit != "done"))
                problems.Add($"{op.Window}: раздел «{closed}» закрыт, а аудит «{op.Audit}»");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void WritesCoverageReport()
    {
        var inv = UiInventory.Load();
        var sb = new StringBuilder("# Покрытие: окна GUI и CLI\n\n| Раздел | Окно | Вид | Аудит | Служба | CLI |\n|---|---|---|---|---|---|\n");
        foreach (var op in inv.Operations.OrderBy(o => Array.IndexOf(inv.Sections, o.Section)).ThenBy(o => o.Window))
            sb.Append($"| {op.Section} | {op.Window} | {op.Kind} | {op.Audit} | {op.Service ?? "—"} | {op.Cli ?? "—"} |\n");
        string dir = Path.Combine(UiInventory.RepoRoot, "tmp");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "ui-coverage.md"), sb.ToString());
    }
}
