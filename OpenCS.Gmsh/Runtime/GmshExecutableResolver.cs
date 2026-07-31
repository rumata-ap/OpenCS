namespace OpenCS.Gmsh.Runtime;

/// <summary>Разрешает путь к Gmsh с explicit, environment и bundled приоритетами.</summary>
public sealed class GmshExecutableResolver
{
    readonly string? _bundledPath;
    readonly string _environmentVariableName;

    public GmshExecutableResolver(string? bundledPath = null, string environmentVariableName = "GMSH_EXE")
    {
        _bundledPath = bundledPath;
        _environmentVariableName = environmentVariableName;
    }

    public GmshExecutableInfo Resolve(string? explicitPath)
    {
        if (TryResolve(explicitPath, out var path)) return new(path, "explicit");
        if (TryResolve(Environment.GetEnvironmentVariable(_environmentVariableName), out path)) return new(path, "environment");
        if (TryResolve(_bundledPath, out path)) return new(path, "bundled");
        throw new FileNotFoundException($"Gmsh executable не найден. Укажите путь явным параметром, {_environmentVariableName} или bundled путь.");
    }

    static bool TryResolve(string? candidate, out string path)
    {
        if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
        {
            path = System.IO.Path.GetFullPath(candidate);
            return true;
        }
        path = "";
        return false;
    }
}
