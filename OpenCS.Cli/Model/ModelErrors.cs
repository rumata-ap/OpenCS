namespace OpenCS.Cli.Model;

/// <summary>Ошибки документа opencs-model с путём в JSON («materials[2].chars.N.e: …»).</summary>
sealed class ModelErrors
{
    readonly List<string> _items = [];
    public int Count => _items.Count;
    public IReadOnlyList<string> Items => _items;
    public void Add(string path, string message) => _items.Add($"{path}: {message}");

    /// <summary>Есть ошибки — консоль печатает все и завершается кодом Usage; в проект ничего не записано.</summary>
    public void ThrowIfAny()
    {
        if (_items.Count > 0)
            throw new CliException(CliExitCode.Usage, string.Join(Environment.NewLine, _items));
    }
}
