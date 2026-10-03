using System.Text.Json;

namespace CScore.Import;

/// <summary>
/// Стальная группа SCAD (ApiSteelElem): КЭ и параметры проверки по СП 16. Длины — м (из единиц проекта SCAD).
/// «Плоскость XoZ» — изгиб вокруг Y1 (прогиб по Z1), в осях сечения OpenCS (x ‖ Y1) — относительно оси x.
/// </summary>
/// <param name="Num">Номер группы (с 1).</param>
/// <param name="Name">Имя группы.</param>
/// <param name="ElementIds">КЭ группы.</param>
/// <param name="SteelMark">Марка стали («C255», буква может быть латинской); пусто — задано <paramref name="Ry"/>.</param>
/// <param name="SteelMarkUser">Имя стали при заданном пользователем Ry.</param>
/// <param name="Ry">Расчётное сопротивление при незаданной марке — в единицах проекта SCAD, справочно.</param>
/// <param name="IsMember">Конструктивный элемент (What_is = 0): расчётные длины — от длины всей цепочки КЭ;
/// иначе — группа элементов, длина каждого КЭ.</param>
/// <param name="ConstructionType">Тип конструкции: 0 — общего вида, 1 — стойка, 2 — балка, 3–6 — элементы ферм.</param>
/// <param name="GammaN">γn (первое предельное состояние).</param>
/// <param name="GammaC">γc.</param>
/// <param name="MuXoZ">Коэффициент расчётной длины в плоскости XoZ (при <paramref name="LengthXoZ"/> = null).</param>
/// <param name="MuYoZ">То же в плоскости YoZ.</param>
/// <param name="LengthXoZ">Заданная расчётная длина в плоскости XoZ, м; null — по коэффициенту.</param>
/// <param name="LengthYoZ">То же в плоскости YoZ.</param>
/// <param name="CompressionLimit">Предельная гибкость сжатых элементов A в λu = A − k·α.</param>
/// <param name="CompressionLimitAlpha">k в λu = A − k·α (0 — постоянный предел).</param>
/// <param name="TensionLimit">Предельная гибкость растянутых элементов.</param>
/// <param name="StepOutPlane">Шаг раскреплений из плоскости, м; null — задан коэффициентом.</param>
/// <param name="StepOutPlaneRatio">Шаг раскреплений как доля геометрической длины.</param>
/// <param name="SlaveGroup">Дополнительная группа (≠ 0).</param>
/// <param name="DesignType">0 — не задано, 1 — стальное сечение, 2 — тонкостенное.</param>
public sealed record ScadSteelGroup(
    int Num, string Name, int[] ElementIds,
    string SteelMark, string SteelMarkUser, double Ry,
    bool IsMember, int ConstructionType,
    double GammaN, double GammaC,
    double MuXoZ, double MuYoZ, double? LengthXoZ, double? LengthYoZ,
    double CompressionLimit, double CompressionLimitAlpha, double TensionLimit,
    double? StepOutPlane, double StepOutPlaneRatio,
    int SlaveGroup, int DesignType);

/// <summary>
/// Стальные группы SCAD схемы: хранение (JSON вложения схемы) и поиск группы КЭ.
/// КЭ, входящий в несколько групп, относится к группе с меньшим номером (как у ЖБ-групп).
/// </summary>
public sealed class ScadSteelGroupIndex
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    readonly Dictionary<int, ScadSteelGroup> _byElement = [];

    /// <summary>Индекс по списку групп.</summary>
    public ScadSteelGroupIndex(IEnumerable<ScadSteelGroup> groups)
    {
        Groups = groups.OrderBy(g => g.Num).ToList();
        foreach (var g in Groups)
            foreach (int id in g.ElementIds)
                _byElement.TryAdd(id, g);
    }

    /// <summary>Группы по возрастанию номера.</summary>
    public IReadOnlyList<ScadSteelGroup> Groups { get; }

    /// <summary>Группа КЭ; null — КЭ ни в одной стальной группе.</summary>
    public ScadSteelGroup? Find(int elementId) => _byElement.GetValueOrDefault(elementId);

    /// <summary>Сериализация групп для хранения.</summary>
    public static string ToJson(IEnumerable<ScadSteelGroup> groups) =>
        JsonSerializer.Serialize(groups.ToList(), JsonOptions);

    /// <summary>Индекс из JSON хранения.</summary>
    /// <exception cref="InvalidDataException">JSON повреждён.</exception>
    public static ScadSteelGroupIndex FromJson(string json)
    {
        try
        {
            return new(JsonSerializer.Deserialize<List<ScadSteelGroup>>(json, JsonOptions) ?? []);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Стальные группы SCAD схемы повреждены: {ex.Message}", ex);
        }
    }

    /// <summary>Имя группы для сообщений: «2 «Балки»».</summary>
    public static string Label(ScadSteelGroup g) =>
        string.IsNullOrWhiteSpace(g.Name) ? g.Num.ToString() : $"{g.Num} «{g.Name.Trim()}»";
}
