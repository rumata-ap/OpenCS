using System.IO;
using System.Text.Json;

namespace OpenCS.Tests;

/// <summary>Операция GUI в описи: окно (или страница), раздел, XAML, таблицы базы, служба, состояние аудита, CLI.</summary>
public sealed record UiOperation(string Window, string Kind, string Section, string[] Xaml, string[] Tables,
    string? Service, string Audit, string? Cli, string? Note);

/// <summary>Файл описи <c>tools/ui-inventory/operations.json</c>.</summary>
public sealed record UiInventoryFile(string[] Sections, string[] ClosedSections, UiOperation[] Operations);

/// <summary>Опись операций GUI: общий источник для аудита окон и покрытия CLI.</summary>
public static class UiInventory
{
    public static readonly string[] AuditStates = ["pending", "in-progress", "done"];
    public static readonly string[] Kinds = ["window", "page", "menu"];

    /// <summary>Корень репозитория — ближайшая вверх папка с OpenCS.sln.</summary>
    public static string RepoRoot { get; } = FindRoot();

    public static string FilePath => Path.Combine(RepoRoot, "tools", "ui-inventory", "operations.json");

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static UiInventoryFile Load() =>
        JsonSerializer.Deserialize<UiInventoryFile>(File.ReadAllText(FilePath), Options)
        ?? throw new InvalidDataException(FilePath);

    static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "OpenCS.sln"))) return d.FullName;
        throw new InvalidOperationException("OpenCS.sln не найден выше " + AppContext.BaseDirectory);
    }
}
