using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CScore.Import;

/// <summary>Стальной профиль жёсткости SCAD, разрешённый по сортаменту: профиль либо причина, почему его нет.</summary>
/// <param name="Num">Номер жёсткости.</param>
/// <param name="Source">Ссылка на сортамент («RUSSIAN okv2012 59»).</param>
/// <param name="Shape">Профиль; null — см. <paramref name="Reason"/>.</param>
/// <param name="Reason">Причина отсутствия профиля.</param>
public sealed record ScadSteelProfileEntry(int Num, string Source, ImportedSteelShape? Shape, string? Reason);

/// <summary>
/// Стальные профили жёсткостей <c>STZ &lt;база&gt; &lt;таблица&gt; &lt;номер&gt;</c> схемы SCAD. Сортамент
/// (<c>&lt;каталог SCAD&gt;\64\&lt;база&gt;.PRF</c>) есть только на ПК с SCAD, поэтому профили разрешаются при чтении
/// .SPR и хранятся при схеме (вложение JSON).
/// </summary>
public sealed class ScadSteelProfileIndex
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    readonly Dictionary<int, ScadSteelProfileEntry> _byNum;

    /// <summary>Индекс по списку профилей.</summary>
    public ScadSteelProfileIndex(IEnumerable<ScadSteelProfileEntry> entries)
    {
        Entries = entries.OrderBy(e => e.Num).ToList();
        _byNum = Entries.GroupBy(e => e.Num).ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>Профили по возрастанию номера жёсткости.</summary>
    public IReadOnlyList<ScadSteelProfileEntry> Entries { get; }

    /// <summary>Профиль жёсткости; null — жёсткость не стальная из сортамента (или не разрешалась).</summary>
    public ScadSteelProfileEntry? Find(int stiffnessNum) => _byNum.GetValueOrDefault(stiffnessNum);

    /// <summary>
    /// Ссылка на сортамент из строки жёсткости SCAD: <c>STZ &lt;база&gt; &lt;таблица&gt; &lt;номер&gt; …</c>;
    /// null — жёсткость не из сортамента.
    /// </summary>
    public static (string Base, string Table, int Row)? SteelRef(string stiffnessParams)
    {
        var parts = stiffnessParams.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4 || parts[0] != "STZ") return null;
        return int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int row)
            ? (parts[1], parts[2], row) : null;
    }

    /// <summary>
    /// Разрешает профили всех жёсткостей STZ. Сортамент базы загружается один раз; не найден или повреждён —
    /// у профилей базы причина.
    /// </summary>
    /// <param name="stiffnesses">Номер и строка жёсткости SCAD.</param>
    /// <param name="loadBase">Сортамент по имени базы («RUSSIAN»); null — файла нет.
    /// <see cref="InvalidDataException"/> — файл повреждён.</param>
    public static List<ScadSteelProfileEntry> Resolve(IEnumerable<(int Num, string Params)> stiffnesses,
        Func<string, ScadPrfFile?> loadBase)
    {
        var bases = new Dictionary<string, (ScadPrfFile? File, string? Error)>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ScadSteelProfileEntry>();
        foreach (var (num, text) in stiffnesses)
        {
            if (SteelRef(text) is not var (baseName, table, row)) continue;
            string source = $"{baseName} {table} {row}";
            if (!bases.TryGetValue(baseName, out var loaded))
            {
                try
                {
                    var file = loadBase(baseName);
                    loaded = (file, file == null ? $"нет сортамента SCAD {baseName}.PRF" : null);
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    loaded = (null, $"сортамент SCAD {baseName}.PRF не прочитан: {ex.Message}");
                }
                bases[baseName] = loaded;
            }
            if (loaded.File == null)
            {
                result.Add(new(num, source, null, loaded.Error));
                continue;
            }
            var (shape, reason) = ScadSteelProfiles.Resolve(loaded.File, table, row);
            result.Add(new(num, source, shape, reason));
        }
        return result;
    }

    /// <summary>Сериализация для хранения.</summary>
    public static string ToJson(IEnumerable<ScadSteelProfileEntry> entries) =>
        JsonSerializer.Serialize(entries.ToList(), JsonOptions);

    /// <summary>Индекс из JSON хранения.</summary>
    /// <exception cref="InvalidDataException">JSON повреждён.</exception>
    public static ScadSteelProfileIndex FromJson(string json)
    {
        try
        {
            return new(JsonSerializer.Deserialize<List<ScadSteelProfileEntry>>(json, JsonOptions) ?? []);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Стальные профили SCAD схемы повреждены: {ex.Message}", ex);
        }
    }
}
