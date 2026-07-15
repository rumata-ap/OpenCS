namespace OpenCS.OpenSees.Runtime;

/// <summary>Разрешает путь к OpenSees с явным и bundled приоритетами.</summary>
public sealed class OpenSeesExecutableResolver
{
    private readonly string? _bundledPath;
    private readonly string _environmentVariableName;

    /// <summary>Создаёт resolver с необязательным bundled executable.</summary>
    public OpenSeesExecutableResolver(
        string? bundledPath = null,
        string environmentVariableName = "OPENSEES_EXE")
    {
        _bundledPath = bundledPath;
        _environmentVariableName = environmentVariableName;
    }

    /// <summary>Возвращает существующий explicit, environment или bundled executable.</summary>
    public OpenSeesExecutableInfo Resolve(string? explicitPath)
    {
        if (TryGetExisting(explicitPath, out string explicitResolved))
            return new OpenSeesExecutableInfo { Path = explicitResolved, Source = "explicit" };

        string? environmentPath = Environment.GetEnvironmentVariable(_environmentVariableName);
        if (TryGetExisting(environmentPath, out string environmentResolved))
            return new OpenSeesExecutableInfo { Path = environmentResolved, Source = "environment" };

        if (TryGetExisting(_bundledPath, out string bundledResolved))
            return new OpenSeesExecutableInfo { Path = bundledResolved, Source = "bundled" };

        throw new FileNotFoundException(
            $"OpenSees executable не найден. Укажите путь явным параметром, {_environmentVariableName} или bundled путь.");
    }

    /// <summary>Запускает команду версии и сохраняет raw stdout/stderr без разбора формата.</summary>
    public async Task<OpenSeesExecutableInfo> ResolveAndProbeAsync(
        string? explicitPath,
        IOpenSeesProcessRunner processRunner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        OpenSeesExecutableInfo resolved = Resolve(explicitPath);
        string directory = System.IO.Path.GetDirectoryName(resolved.Path) ?? Environment.CurrentDirectory;
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
