using OpenCS.ExternalTools;

namespace OpenCS.Gmsh.Runtime;

/// <summary>
/// Разрешает Gmsh: explicit → <c>GMSH_EXE</c> → bundled → PATH → Python-пакет <c>gmsh</c>.
/// В режиме <see cref="ExternalToolSource.PythonPackages"/> (консоль) — только Python-пакет: его запуск с теми же
/// аргументами командной строки, что у gmsh.exe (<see cref="GmshProcessRunner"/>).
/// </summary>
public sealed class GmshExecutableResolver
{
    /// <summary>Имя инструмента в <see cref="PythonToolCommand"/>.</summary>
    public const string PythonToolName = "gmsh";

    const string InstallHint = "Установите: python -m pip install gmsh (на Linux также системные библиотеки " +
                               "libglu1-mesa libxcursor1 libxft2 libxinerama1 libxrender1 libgl1).";

    readonly string? _bundledPath;
    readonly string _environmentVariableName;
    readonly bool _useFallbacks;

    /// <param name="useFallbacks">false — без поиска в PATH и Python-пакета (только явные источники).</param>
    public GmshExecutableResolver(string? bundledPath = null, string environmentVariableName = "GMSH_EXE", bool useFallbacks = true)
    {
        _bundledPath = bundledPath;
        _environmentVariableName = environmentVariableName;
        _useFallbacks = useFallbacks;
    }

    public GmshExecutableInfo Resolve(string? explicitPath)
    {
        if (ExternalToolPolicy.Current == ExternalToolSource.PythonPackages)
            return ResolvePython(out var probed) ?? throw new FileNotFoundException(
                "Gmsh (режим Python-пакетов): " + PythonRuntime.DescribeMissing("gmsh", probed, InstallHint));

        if (TryResolve(explicitPath, out var path)) return new(path, "explicit");
        if (TryResolve(Environment.GetEnvironmentVariable(_environmentVariableName), out path)) return new(path, "environment");
        if (TryResolve(_bundledPath, out path)) return new(path, "bundled");
        if (_useFallbacks)
        {
            // На Windows — только gmsh.exe: gmsh.bat из pip-пакета запускается через cmd с иными правилами кавычек.
            var inPath = ExecutableSearch.FindInPath([ExecutableSearch.PlatformFileName("gmsh")]);
            if (inPath != null) return new(inPath, "path");
            var python = ResolvePython(out _);
            if (python != null) return python;
        }
        throw new FileNotFoundException(
            $"Gmsh executable не найден. Укажите путь явным параметром, {_environmentVariableName}, добавьте gmsh в PATH " +
            "или установите Python-пакет: python -m pip install gmsh.");
    }

    static GmshExecutableInfo? ResolvePython(out IReadOnlyList<PythonProbe> probed)
    {
        var python = PythonRuntime.FindWithModules(["gmsh"], out probed);
        return python == null ? null : new(new PythonToolCommand(PythonToolName, python).ToString(), "python");
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
