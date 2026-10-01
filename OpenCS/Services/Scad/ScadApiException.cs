namespace OpenCS.Services.Scad;

/// <summary>
/// Ошибка работы с SCADAPIX.dll. Текст для пользователя — ресурс <see cref="ResourceKey"/>
/// с аргументами <see cref="Args"/> (форматируется в потоке UI, см. <see cref="Format"/>), плюс
/// сообщения самой DLL (<see cref="Details"/>, ApiGetPhrase) — их SCAD выдаёт по-русски.
/// </summary>
public sealed class ScadApiException : Exception
{
    public ScadApiException(string resourceKey, object?[] args, string? details = null, Exception? inner = null)
        : base(BuildFallback(resourceKey, args, details), inner)
    {
        ResourceKey = resourceKey;
        Args = args;
        Details = details;
    }

    /// <summary>Ключ строки в Strings.*.xaml (формат string.Format).</summary>
    public string ResourceKey { get; }

    /// <summary>Аргументы строки ресурса.</summary>
    public object?[] Args { get; }

    /// <summary>Сообщения DLL (ApiGetPhrase) через перенос строки; null — нет.</summary>
    public string? Details { get; }

    /// <summary>Текст для пользователя по функции поиска ресурса (Loc.S).</summary>
    public string Format(Func<string, string> resource)
    {
        string text;
        try { text = string.Format(resource(ResourceKey), Args); }
        catch (FormatException) { text = ResourceKey; }
        return string.IsNullOrWhiteSpace(Details) ? text : text + Environment.NewLine + Details;
    }

    static string BuildFallback(string key, object?[] args, string? details) =>
        key + (args.Length > 0 ? " (" + string.Join("; ", args) + ")" : "") +
        (string.IsNullOrWhiteSpace(details) ? "" : ": " + details);
}
