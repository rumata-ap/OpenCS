using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace OpenCS.Cli;

/// <summary>Коды возврата консоли.</summary>
static class CliExitCode
{
    /// <summary>Всё выполнено (непрохождение проверки — тоже результат расчёта).</summary>
    public const int Ok = 0;

    /// <summary>Хотя бы один расчёт не выполнен, не сошёлся, прерван или завершился ошибкой.</summary>
    public const int Failed = 1;

    /// <summary>Аргументы, файл проекта, версия БД, занятость файла.</summary>
    public const int Usage = 2;
}

/// <summary>Ошибка, которую консоль показывает одной строкой и завершается с кодом <see cref="ExitCode"/>.</summary>
sealed class CliException(int exitCode, string message) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}

/// <summary>Вывод: таблицы и строки «имя: значение» для человека, JSON для программ.</summary>
static class CliOutput
{
    /// <summary>JSON с отступами, кириллица без \u-экранирования.</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void WriteJson(TextWriter writer, object? value) =>
        writer.WriteLine(JsonSerializer.Serialize(value, Json));

    /// <summary>Разбирает JSON результата; при ошибке разбора — строкой как есть.</summary>
    public static JsonNode? ParseData(string dataJson)
    {
        try { return JsonNode.Parse(dataJson); }
        catch (JsonException) { return JsonValue.Create(dataJson); }
    }

    /// <summary>Таблица с выравниванием по самой длинной ячейке столбца.</summary>
    public static void WriteTable(TextWriter writer, IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        var all = rows.ToList();
        var widths = headers.Select((h, i) => Math.Max(h.Length, all.Count == 0 ? 0 : all.Max(r => r[i].Length))).ToArray();
        writer.WriteLine(Line(headers));
        writer.WriteLine(string.Join("  ", widths.Select(w => new string('-', w))));
        foreach (var row in all) writer.WriteLine(Line(row));

        string Line(IReadOnlyList<string> cells) =>
            string.Join("  ", cells.Select((c, i) => c.PadRight(widths[i]))).TrimEnd();
    }

    /// <summary>Строки «имя: значение» с выравниванием значений.</summary>
    public static void WritePairs(TextWriter writer, IEnumerable<(string Name, string Value)> pairs)
    {
        var all = pairs.ToList();
        int width = all.Max(p => p.Name.Length) + 1;
        foreach (var (name, value) in all)
            writer.WriteLine((name + ":").PadRight(width + 1) + value);
    }
}
