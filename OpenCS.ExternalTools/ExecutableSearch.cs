namespace OpenCS.ExternalTools;

/// <summary>Поиск исполняемых файлов внешних программ одинаково на Windows и Linux.</summary>
public static class ExecutableSearch
{
    /// <summary>Имя файла программы для текущей ОС: «gmsh» → «gmsh.exe» на Windows, «gmsh» на Linux.</summary>
    public static string PlatformFileName(string baseName) =>
        OperatingSystem.IsWindows() && !baseName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? baseName + ".exe"
            : baseName;

    /// <summary>Файл существует и его можно запустить: на Unix нужен хотя бы один бит x.</summary>
    public static bool IsExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        if (OperatingSystem.IsWindows()) return true;
        var mode = File.GetUnixFileMode(path);
        return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
    }

    /// <summary>
    /// Сохранённый путь годится для этой ОС — полный (<see cref="Path.IsPathFullyQualified(string)"/>).
    /// Пути хранятся в файле проекта: «C:\Tools\…» с Windows на Linux — относительный путь, «/opt/…» на
    /// Windows — путь от корня текущего диска; оба считаются незаданными.
    /// </summary>
    public static bool IsUsablePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path);

    /// <summary>
    /// Первый подходящий исполняемый файл из каталогов PATH (пустые и неполные элементы пропускаются);
    /// <paramref name="accept"/> отсеивает кандидатов (например, заглушку Python из Microsoft Store).
    /// </summary>
    public static string? FindInPath(IEnumerable<string> fileNames, Func<string, bool>? accept = null) =>
        FindInPath(Environment.GetEnvironmentVariable("PATH"), fileNames, accept);

    /// <summary>То же для явно заданной строки PATH (для тестов).</summary>
    public static string? FindInPath(string? pathVariable, IEnumerable<string> fileNames, Func<string, bool>? accept = null)
    {
        if (string.IsNullOrEmpty(pathVariable)) return null;
        var names = fileNames.ToList();
        foreach (var raw in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var dir = raw.Trim().Trim('"');
            if (!IsUsablePath(dir)) continue;
            foreach (var name in names)
            {
                string candidate;
                try { candidate = Path.Combine(dir, name); }
                catch (ArgumentException) { continue; }
                if (IsExecutable(candidate) && (accept == null || accept(candidate)))
                    return Path.GetFullPath(candidate);
            }
        }
        return null;
    }
}
