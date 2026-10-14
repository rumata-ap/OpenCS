namespace OpenCS.ExternalTools;

/// <summary>Откуда брать gmsh и OpenSees.</summary>
public enum ExternalToolSource
{
    /// <summary>Как в OpenCS: явный путь, переменные окружения, штатный путь, PATH; Python-пакеты — последними.</summary>
    Auto,

    /// <summary>
    /// Только Python-пакеты (<c>openseespy</c>, <c>gmsh</c>) — режим консоли: одинаково на Windows и Linux,
    /// без OpenSees.exe и gmsh.exe.
    /// </summary>
    PythonPackages
}

/// <summary>
/// Текущий источник внешних программ. Консоль включает <see cref="ExternalToolSource.PythonPackages"/>
/// через <see cref="Use"/> на время своей работы: значение <see cref="AsyncLocal{T}"/> переходит в задачи и
/// потоки, запущенные из этого потока выполнения, и не задевает другие (тесты в одном процессе).
/// Переменная окружения <c>OPENCS_EXTERNAL_TOOLS=python</c> включает режим для всего процесса (и для OpenCS).
/// </summary>
public static class ExternalToolPolicy
{
    public const string EnvironmentVariable = "OPENCS_EXTERNAL_TOOLS";

    static readonly AsyncLocal<ExternalToolSource?> scoped = new();

    public static ExternalToolSource Current => scoped.Value ?? FromEnvironment();

    /// <summary>Включает источник до Dispose (в этом потоке выполнения и порождённых им).</summary>
    public static IDisposable Use(ExternalToolSource source)
    {
        var previous = scoped.Value;
        scoped.Value = source;
        return new Restore(previous);
    }

    static ExternalToolSource FromEnvironment() =>
        string.Equals(Environment.GetEnvironmentVariable(EnvironmentVariable)?.Trim(), "python", StringComparison.OrdinalIgnoreCase)
            ? ExternalToolSource.PythonPackages
            : ExternalToolSource.Auto;

    sealed class Restore(ExternalToolSource? previous) : IDisposable
    {
        bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            scoped.Value = previous;
        }
    }
}
