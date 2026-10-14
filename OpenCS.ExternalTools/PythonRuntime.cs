using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenCS.ExternalTools;

/// <summary>Пакет Python, нужный OpenCS: есть ли он у интерпретатора и какой версии.</summary>
public sealed record PythonModuleStatus(string Name, bool Available, string? Version, string? Error);

/// <summary>Результат проверки интерпретатора: версия Python и состояние пакетов (<see cref="PythonRuntime.KnownModules"/>).</summary>
public sealed record PythonProbe(string PythonPath, string? PythonVersion, IReadOnlyDictionary<string, PythonModuleStatus> Modules, string? Error)
{
    public bool Has(string module) => Modules.TryGetValue(module, out var m) && m.Available;
}

/// <summary>
/// Интерпретатор Python с пакетами OpenCS (<c>openseespy</c> + <c>tkinter</c> для OpenSees, <c>gmsh</c>).
/// Кандидаты по порядку: <c>OPENCS_PYTHON</c>, активное окружение (<c>VIRTUAL_ENV</c>, <c>CONDA_PREFIX</c>), python
/// из PATH. Берётся первый, у которого есть нужный пакет; проверка (один запуск Python на интерпретатор)
/// кэшируется на время процесса.
/// </summary>
public static class PythonRuntime
{
    public const string PythonEnvironmentVariable = "OPENCS_PYTHON";

    /// <summary>Пакеты, которые проверяет <see cref="Probe"/>.</summary>
    public static readonly IReadOnlyList<string> KnownModules = ["openseespy", "tkinter", "gmsh"];

    static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(60);
    static readonly ConcurrentDictionary<string, Lazy<PythonProbe>> probes = new(StringComparer.Ordinal);

    /// <summary>Кандидаты в интерпретаторы (существующие файлы, без повторов), в порядке предпочтения.</summary>
    public static IReadOnlyList<string> Candidates()
    {
        var list = new List<string>();
        void Add(string? path)
        {
            if (!ExecutableSearch.IsUsablePath(path) || !ExecutableSearch.IsExecutable(path)) return;
            var full = Path.GetFullPath(path!);
            if (!list.Contains(full, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
                list.Add(full);
        }

        Add(Environment.GetEnvironmentVariable(PythonEnvironmentVariable));
        foreach (var prefix in new[] { "VIRTUAL_ENV", "CONDA_PREFIX" })
        {
            var root = Environment.GetEnvironmentVariable(prefix);
            if (!ExecutableSearch.IsUsablePath(root)) continue;
            Add(OperatingSystem.IsWindows() ? Path.Combine(root!, "Scripts", "python.exe") : Path.Combine(root!, "bin", "python"));
            if (OperatingSystem.IsWindows()) Add(Path.Combine(root!, "python.exe"));
        }
        string[] names = OperatingSystem.IsWindows() ? ["python.exe", "python3.exe"] : ["python3", "python"];
        foreach (var name in names)
            Add(ExecutableSearch.FindInPath([name], IsNotStoreStub));
        return list;
    }

    /// <summary>
    /// Первый кандидат, у которого доступны все <paramref name="modules"/>; null — нет такого.
    /// <paramref name="checkedCandidates"/> — проверенные интерпретаторы (для сообщения об ошибке).
    /// </summary>
    public static string? FindWithModules(IReadOnlyList<string> modules, out IReadOnlyList<PythonProbe> checkedCandidates)
    {
        var probed = new List<PythonProbe>();
        checkedCandidates = probed;
        foreach (var python in Candidates())
        {
            var probe = Probe(python);
            probed.Add(probe);
            if (modules.All(probe.Has)) return python;
        }
        return null;
    }

    /// <summary>Проверяет интерпретатор: версия Python и пакеты <see cref="KnownModules"/> (кэш на процесс).</summary>
    public static PythonProbe Probe(string pythonPath) =>
        probes.GetOrAdd(pythonPath, p => new Lazy<PythonProbe>(() => RunProbe(p))).Value;

    /// <summary>Текст для сообщения «пакет не найден»: что проверено и как установить.</summary>
    public static string DescribeMissing(string module, IReadOnlyList<PythonProbe> checkedCandidates, string installHint)
    {
        var sb = new StringBuilder();
        sb.Append($"Python-пакет {module} не найден. ");
        if (checkedCandidates.Count == 0)
            sb.Append($"Интерпретатор Python не найден ни в {PythonEnvironmentVariable}, ни в активном окружении, ни в PATH. ");
        foreach (var probe in checkedCandidates)
        {
            sb.Append($"{probe.PythonPath}: ");
            if (probe.Error != null) sb.Append(probe.Error);
            else sb.Append(probe.Modules.TryGetValue(module, out var m) && m.Error != null ? m.Error : "нет пакета");
            sb.Append("; ");
        }
        sb.Append(installHint);
        sb.Append($" Другой интерпретатор (например, venv) — переменная окружения {PythonEnvironmentVariable}=<путь к python>.");
        return sb.ToString();
    }

    /// <summary>
    /// Запуск Python: режим UTF-8 (<c>-X utf8</c>), вывод без буферизации — для живого журнала. Пользовательские
    /// site-packages не отключаются (<c>pip install --user</c>), поэтому запускается только файл сценария из
    /// <see cref="MaterializeScript"/>: каталог сценария — наш, текущий каталог в sys.path не попадает.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(string pythonPath, string scriptPath, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(pythonPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        info.ArgumentList.Add("-X");
        info.ArgumentList.Add("utf8");
        info.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["PYTHONUNBUFFERED"] = "1";
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        info.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        return info;
    }

    /// <summary>
    /// Кладёт встроенный сценарий в <c>%TEMP%/opencs-python/&lt;хеш содержимого&gt;/</c> (один раз на версию) и
    /// возвращает путь. Запись через временный файл и перенос — безопасна при параллельных запусках.
    /// </summary>
    public static string MaterializeScript(string fileName, string content)
    {
        var bytes = new UTF8Encoding(false).GetBytes(content);
        var hash = Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
        var dir = Path.Combine(Path.GetTempPath(), "opencs-python", hash);
        var path = Path.Combine(dir, fileName);
        if (File.Exists(path) && new FileInfo(path).Length == bytes.Length) return path;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $"{fileName}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(temp, bytes);
        try { File.Move(temp, path, overwrite: true); }
        catch (IOException) when (File.Exists(path)) { TryDelete(temp); }
        catch (UnauthorizedAccessException) when (File.Exists(path)) { TryDelete(temp); }
        return path;
    }

    static bool IsNotStoreStub(string path) =>
        // Заглушка Microsoft Store (%LOCALAPPDATA%\Microsoft\WindowsApps\python.exe) открывает магазин вместо Python.
        !path.Contains($"{Path.DirectorySeparatorChar}WindowsApps{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    const string ProbeScript = """
        import json, os, sys
        result = {"python": sys.version.split()[0], "modules": {}}
        def dist_version(name):
            try:
                from importlib.metadata import version
                return version(name)
            except Exception:
                return None
        def check(name, probe):
            try:
                result["modules"][name] = {"ok": True, "version": probe()}
            except BaseException as ex:
                result["modules"][name] = {"ok": False, "error": "%s: %s" % (type(ex).__name__, ex)}
        def openseespy():
            import openseespy.opensees
            return dist_version("openseespy")
        def tk():
            import tkinter
            return tkinter.Tcl().eval("info patchlevel")
        def gmsh():
            import gmsh
            return getattr(gmsh, "__version__", None) or dist_version("gmsh")
        check("openseespy", openseespy)
        check("tkinter", tk)
        check("gmsh", gmsh)
        sys.stdout.write("OPENCS_PROBE " + json.dumps(result) + "\n")
        sys.stdout.flush()
        # os._exit: OpenSeesPy при обычном завершении печатает «Process 0 Terminating».
        os._exit(0)
        """;

    static PythonProbe RunProbe(string pythonPath)
    {
        var empty = new Dictionary<string, PythonModuleStatus>();
        try
        {
            var script = MaterializeScript("opencs_probe.py", ProbeScript);
            using var process = Process.Start(CreateStartInfo(pythonPath, script, []))
                ?? throw new IOException("процесс не запустился");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(ProbeTimeout))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new PythonProbe(pythonPath, null, empty, $"проверка не уложилась в {ProbeTimeout.TotalSeconds:0} с");
            }
            var line = stdout.Result.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("OPENCS_PROBE ", StringComparison.Ordinal));
            if (line == null)
                return new PythonProbe(pythonPath, null, empty, $"не ответил (код {process.ExitCode}): {stderr.Result.Trim()}");

            using var doc = JsonDocument.Parse(line["OPENCS_PROBE ".Length..]);
            var modules = new Dictionary<string, PythonModuleStatus>();
            foreach (var m in doc.RootElement.GetProperty("modules").EnumerateObject())
            {
                bool ok = m.Value.GetProperty("ok").GetBoolean();
                string? version = ok && m.Value.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                string? error = !ok && m.Value.TryGetProperty("error", out var e) ? e.GetString() : null;
                modules[m.Name] = new PythonModuleStatus(m.Name, ok, version, error);
            }
            return new PythonProbe(pythonPath, doc.RootElement.GetProperty("python").GetString(), modules, null);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or JsonException
                                       or InvalidOperationException or KeyNotFoundException)
        {
            return new PythonProbe(pythonPath, null, empty, ex.Message);
        }
    }
}
