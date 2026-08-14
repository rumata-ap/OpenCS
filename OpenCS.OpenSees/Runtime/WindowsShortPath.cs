using System.Runtime.InteropServices;
using System.Text;

namespace OpenCS.OpenSees.Runtime;

/// <summary>Обход отсутствия Unicode-поддержки путей у OpenSees.exe: Tcl-рантайм открывает файлы
/// через ANSI-кодовую страницу процесса, поэтому путь с кириллицей превращается в "?????" и
/// становится нечитаемым для OpenSees, хотя для .NET/Windows он полностью корректен. Короткие
/// 8.3-имена Windows всегда ASCII и указывают на тот же файл — используем их как обходной путь.</summary>
public static class WindowsShortPath
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetShortPathNameW(string longPath, StringBuilder shortPath, int bufferSize);

    /// <summary>true, если путь целиком представим в 7-битном ASCII (безопасен для OpenSees.exe).</summary>
    public static bool IsAsciiSafe(string path)
    {
        foreach (char c in path)
            if (c > 127) return false;
        return true;
    }

    /// <summary>Пытается получить ASCII-safe alias пути через короткие 8.3-имена Windows.
    /// Возвращает исходный путь без изменений, если он уже ASCII-safe, платформа не Windows,
    /// либо 8.3-имена недоступны (отключены на томе, путь ещё не существует и т.п.) — в этом
    /// случае вызывающий код должен сам решить, как реагировать на оставшийся non-ASCII путь.</summary>
    public static string TryMakeAsciiSafe(string path)
    {
        if (IsAsciiSafe(path)) return path;
        if (!OperatingSystem.IsWindows()) return path;

        try
        {
            StringBuilder buffer = new(1024);
            int length = GetShortPathNameW(path, buffer, buffer.Capacity);
            if (length <= 0 || length > buffer.Capacity) return path;

            string shortPath = buffer.ToString(0, length);
            return IsAsciiSafe(shortPath) ? shortPath : path;
        }
        catch (EntryPointNotFoundException)
        {
            return path;
        }
    }
}
