namespace OpenCS.OpenSees.Runtime;

/// <summary>Разрешённый executable OpenSees и необработанный ответ версии.</summary>
public sealed class OpenSeesExecutableInfo
{
    /// <summary>Полный путь к executable.</summary>
    public string Path { get; init; } = "";

    /// <summary>Источник пути: explicit, environment или bundled.</summary>
    public string Source { get; init; } = "";

    /// <summary>Сырой stdout/stderr команды версии.</summary>
    public string RawVersionOutput { get; init; } = "";
}
