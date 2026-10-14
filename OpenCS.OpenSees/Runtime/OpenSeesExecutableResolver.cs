using OpenCS.ExternalTools;

namespace OpenCS.OpenSees.Runtime;

/// <summary>
/// Разрешает OpenSees: explicit → <c>OPENSEES_EXE</c> → <c>OPENSEES_HOME/bin</c> → штатный путь → PATH →
/// Python-пакет <c>openseespy</c> (через <see cref="OpenSeesPyBridge"/>). В режиме
/// <see cref="ExternalToolSource.PythonPackages"/> (консоль) — только OpenSeesPy.
/// </summary>
public sealed class OpenSeesExecutableResolver
{
    /// <summary>Штатный путь OpenSees на компьютерах разработчика.</summary>
    public const string DefaultExecutablePath = @"C:\Tools\OpenSees\bin\OpenSees.exe";

    const string InstallHint = "Установите: python -m pip install openseespy (на Linux также пакет python3-tk).";

    /// <summary>Пакеты Python, без которых мост не работает.</summary>
    static readonly string[] PythonModules = ["openseespy", "tkinter"];

    private readonly string? _bundledPath;
    private readonly string _environmentVariableName;
    private readonly bool _useFallbacks;

    /// <summary>Создаёт resolver с необязательным bundled executable.</summary>
    /// <param name="useFallbacks">false — без поиска в PATH и OpenSeesPy (только явные источники).</param>
    public OpenSeesExecutableResolver(
        string? bundledPath = null,
        string environmentVariableName = "OPENSEES_EXE",
        bool useFallbacks = true)
    {
        _bundledPath = bundledPath ?? DefaultExecutablePath;
        _environmentVariableName = environmentVariableName;
        _useFallbacks = useFallbacks;
    }

    /// <summary>Возвращает существующий explicit, environment, bundled, PATH executable или OpenSeesPy.</summary>
    public OpenSeesExecutableInfo Resolve(string? explicitPath)
    {
        if (ExternalToolPolicy.Current == ExternalToolSource.PythonPackages)
            return ResolvePython(out var probed) ?? throw new FileNotFoundException(
                "OpenSees (режим Python-пакетов): " + PythonRuntime.DescribeMissing("openseespy", probed, InstallHint));

        if (TryGetExisting(explicitPath, out string explicitResolved))
            return new OpenSeesExecutableInfo { Path = explicitResolved, Source = "explicit" };

        string? environmentPath = Environment.GetEnvironmentVariable(_environmentVariableName);
        if (TryGetExisting(environmentPath, out string environmentResolved))
            return new OpenSeesExecutableInfo { Path = environmentResolved, Source = "environment" };

        string? home = Environment.GetEnvironmentVariable("OPENSEES_HOME");
        if (ExecutableSearch.IsUsablePath(home)
            && TryGetExisting(System.IO.Path.Combine(home!, "bin", ExecutableSearch.PlatformFileName("OpenSees")), out string homeResolved))
            return new OpenSeesExecutableInfo { Path = homeResolved, Source = "environment" };

        if (TryGetExisting(_bundledPath, out string bundledResolved))
            return new OpenSeesExecutableInfo { Path = bundledResolved, Source = "bundled" };

        if (_useFallbacks)
        {
            string[] names = OperatingSystem.IsWindows() ? ["OpenSees.exe"] : ["OpenSees", "opensees"];
            string? inPath = ExecutableSearch.FindInPath(names);
            if (inPath != null)
                return new OpenSeesExecutableInfo { Path = inPath, Source = "path" };
            OpenSeesExecutableInfo? python = ResolvePython(out _);
            if (python != null) return python;
        }

        throw new FileNotFoundException(
            $"OpenSees executable не найден. Укажите путь явным параметром, {_environmentVariableName}, " +
            $"OPENSEES_HOME, проверьте штатный путь {DefaultExecutablePath}, добавьте OpenSees в PATH " +
            "или установите Python-пакет: python -m pip install openseespy.");
    }

    /// <summary>Запускает команду версии и сохраняет raw stdout/stderr без разбора формата.</summary>
    public async Task<OpenSeesExecutableInfo> ResolveAndProbeAsync(
        string? explicitPath,
        IOpenSeesProcessRunner processRunner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        OpenSeesExecutableInfo resolved = Resolve(explicitPath);
        string directory = PythonToolCommand.TryParse(resolved.Path, out _)
            ? Environment.CurrentDirectory
            : System.IO.Path.GetDirectoryName(resolved.Path) ?? Environment.CurrentDirectory;
        OpenSeesRunResult result = await processRunner.RunAsync(new OpenSeesRunRequest
        {
            ExecutablePath = resolved.Path,
            Arguments = ["-version"],
            WorkingDirectory = directory,
            Timeout = TimeSpan.FromSeconds(10)
        }, cancellationToken);

        return new OpenSeesExecutableInfo
        {
            Path = resolved.Path,
            Source = resolved.Source,
            RawVersionOutput = result.Stdout + result.Stderr
        };
    }

    static OpenSeesExecutableInfo? ResolvePython(out IReadOnlyList<PythonProbe> probed)
    {
        string? python = PythonRuntime.FindWithModules(PythonModules, out probed);
        return python == null
            ? null
            : new OpenSeesExecutableInfo { Path = new PythonToolCommand(OpenSeesPyBridge.ToolName, python).ToString(), Source = "openseespy" };
    }

    private static bool TryGetExisting(string? path, out string resolved)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            resolved = System.IO.Path.GetFullPath(path);
            return true;
        }

        resolved = "";
        return false;
    }
}
