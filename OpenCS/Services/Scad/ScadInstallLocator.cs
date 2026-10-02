using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace OpenCS.Services.Scad;

/// <summary>
/// Поиск установки SCAD: каталог с 64-битной SCADAPIX.dll и рабочий каталог SCAD.
/// Кандидаты — InstallLocation из записей «Программы и компоненты» (в реестре остаются и записи
/// удалённых версий, поэтому проверяется наличие файла), затем стандартные папки «SCAD Soft».
/// Из нескольких установок выбирается DLL с наибольшей версией файла.
/// </summary>
internal static class ScadInstallLocator
{
    const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>Каталог с SCADAPIX.dll (…\64); null — установка не найдена.</summary>
    public static string? FindDllDirectory() =>
        Candidates()
            .Select(dir => Path.Combine(dir, "64"))
            .Where(dir => File.Exists(Path.Combine(dir, ScadApiNative.DllName)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(dir => FileVersion(Path.Combine(dir, ScadApiNative.DllName)))
            .FirstOrDefault();

    /// <summary>Есть ли SCADAPIX.dll в каталоге.</summary>
    public static bool ContainsDll(string? dllDirectory) =>
        !string.IsNullOrWhiteSpace(dllDirectory) && File.Exists(Path.Combine(dllDirectory, ScadApiNative.DllName));

    /// <summary>
    /// Рабочий каталог SCAD — строка «WorkDir =» файла %ProgramData%\SCAD Soft\SCADX.ini (cp1251);
    /// null — файла или строки нет.
    /// </summary>
    public static string? FindWorkDirectory()
    {
        string ini = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SCAD Soft", "SCADX.ini");
        try
        {
            if (!File.Exists(ini)) return null;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            foreach (string line in File.ReadLines(ini, Encoding.GetEncoding(1251)))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0 || !line[..eq].Trim().Equals("WorkDir", StringComparison.OrdinalIgnoreCase)) continue;
                string dir = line[(eq + 1)..].Trim();
                return dir.Length > 0 ? dir : null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    static IEnumerable<string> Candidates()
    {
        foreach (string dir in RegistryLocations())
            yield return dir;
        foreach (string root in StandardRoots())
        {
            string[] dirs;
            try { dirs = Directory.Exists(root) ? Directory.GetDirectories(root) : []; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (string dir in dirs) yield return dir;
        }
    }

    static IEnumerable<string> StandardRoots()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SCAD Soft");
        string? systemDrive = Path.GetPathRoot(Environment.SystemDirectory);
        if (!string.IsNullOrEmpty(systemDrive))
            yield return Path.Combine(systemDrive, "SCAD Soft");
    }

    static List<string> RegistryLocations()
    {
        var result = new List<string>();
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var uninstall = root.OpenSubKey(UninstallKey);
                if (uninstall == null) continue;
                foreach (string name in uninstall.GetSubKeyNames())
                {
                    using var app = uninstall.OpenSubKey(name);
                    if (app?.GetValue("DisplayName") is not string display ||
                        !display.Contains("SCAD", StringComparison.OrdinalIgnoreCase)) continue;
                    if (app.GetValue("InstallLocation") is string location && location.Trim().Length > 0)
                        result.Add(location.Trim().TrimEnd('\\'));
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) { }
        }
        return result;
    }

    static Version FileVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException) { return new Version(0, 0); }
    }
}
