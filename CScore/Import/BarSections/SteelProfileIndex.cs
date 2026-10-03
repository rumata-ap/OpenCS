using System.Text.Json;
using System.Text.Json.Serialization;

namespace CScore.Import;

/// <summary>Стальной профиль жёсткости схемы-источника, разрешённый по сортаменту: профиль либо причина, почему его нет.</summary>
/// <param name="Num">Номер жёсткости.</param>
/// <param name="Source">Ссылка на сортамент: SCAD — «RUSSIAN okv2012 59», ЛИРА — «gn-kv94.profiles.srt: 80 x 3».</param>
/// <param name="Shape">Профиль; null — см. <paramref name="Reason"/>.</param>
/// <param name="Reason">Причина отсутствия профиля.</param>
/// <param name="SteelMark">Марка стали из жёсткости (ЛИРА: <c>Steel = |…|</c>); null — не задана.</param>
public sealed record SteelProfileEntry(int Num, string Source, ImportedSteelShape? Shape, string? Reason,
    string? SteelMark = null);

/// <summary>
/// Стальные профили жёсткостей схемы-источника (STZ SCAD, вид 1018 ЛИРЫ). Сортаменты есть только на ПК с
/// программой-источником, поэтому профили разрешаются при импорте и хранятся при схеме (вложение JSON).
/// </summary>
public sealed class SteelProfileIndex
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    readonly Dictionary<int, SteelProfileEntry> _byNum;

    /// <summary>Индекс по списку профилей.</summary>
    public SteelProfileIndex(IEnumerable<SteelProfileEntry> entries)
    {
        Entries = entries.OrderBy(e => e.Num).ToList();
        _byNum = Entries.GroupBy(e => e.Num).ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>Профили по возрастанию номера жёсткости.</summary>
    public IReadOnlyList<SteelProfileEntry> Entries { get; }

    /// <summary>Профиль жёсткости; null — жёсткость не стальная из сортамента (или не разрешалась).</summary>
    public SteelProfileEntry? Find(int stiffnessNum) => _byNum.GetValueOrDefault(stiffnessNum);

    /// <summary>Сериализация для хранения.</summary>
    public static string ToJson(IEnumerable<SteelProfileEntry> entries) =>
        JsonSerializer.Serialize(entries.ToList(), JsonOptions);

    /// <summary>Индекс из JSON хранения.</summary>
    /// <exception cref="InvalidDataException">JSON повреждён.</exception>
    public static SteelProfileIndex FromJson(string json)
    {
        try
        {
            return new(JsonSerializer.Deserialize<List<SteelProfileEntry>>(json, JsonOptions) ?? []);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Стальные профили схемы повреждены: {ex.Message}", ex);
        }
    }
}
