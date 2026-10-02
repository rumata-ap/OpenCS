using System.Text.Json;

namespace CScore.Import;

/// <summary>
/// ЖБ-группы SCAD схемы: хранение (JSON вложения схемы) и поиск группы КЭ.
/// КЭ, входящий в несколько групп (основная + подчинённая, §14.3 спеки), относится к группе с меньшим номером.
/// </summary>
public sealed class ScadConcreteGroupIndex
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    readonly Dictionary<int, ScadConcreteGroup> _byElement = [];

    /// <summary>Индекс по списку групп.</summary>
    public ScadConcreteGroupIndex(IEnumerable<ScadConcreteGroup> groups)
    {
        Groups = groups.OrderBy(g => g.Num).ToList();
        var multi = new HashSet<int>();
        foreach (var g in Groups)
            foreach (int id in g.ElementIds)
                if (!_byElement.TryAdd(id, g)) multi.Add(id);
        MultiGroupElements = multi.Count;
    }

    /// <summary>Группы по возрастанию номера.</summary>
    public IReadOnlyList<ScadConcreteGroup> Groups { get; }

    /// <summary>Число КЭ, входящих в несколько групп.</summary>
    public int MultiGroupElements { get; }

    /// <summary>Группа КЭ; null — КЭ ни в одной группе.</summary>
    public ScadConcreteGroup? Find(int elementId) => _byElement.GetValueOrDefault(elementId);

    /// <summary>Сериализация групп для хранения.</summary>
    public static string ToJson(IEnumerable<ScadConcreteGroup> groups) =>
        JsonSerializer.Serialize(groups.ToList(), JsonOptions);

    /// <summary>Группы из JSON хранения.</summary>
    /// <exception cref="InvalidDataException">JSON повреждён.</exception>
    public static List<ScadConcreteGroup> GroupsFromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<ScadConcreteGroup>>(json, JsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"ЖБ-группы SCAD схемы повреждены: {ex.Message}", ex);
        }
    }

    /// <summary>Индекс из JSON хранения.</summary>
    public static ScadConcreteGroupIndex FromJson(string json) => new(GroupsFromJson(json));

    /// <summary>
    /// Привязки арматуры пластины a1..a4 группы, м (как S1..S4 подбора: низ X, верх X, низ Y, верх Y).
    /// a3/a4 = 0 — «не задано», берутся a1/a2 (так считает SCAD).
    /// </summary>
    public static (double A1, double A2, double A3, double A4) PlateCovers(ScadConcreteGroup g)
    {
        double a1 = At(g, 0), a2 = At(g, 1), a3 = At(g, 2), a4 = At(g, 3);
        return (a1, a2, a3 == 0 ? a1 : a3, a4 == 0 ? a2 : a4);
    }

    /// <summary>
    /// Причина «подбора нет» для КЭ, которого нет в выгрузке плагина: КЭ из ЖБ-группы SCAD должен был получить
    /// подбор — значит, SCAD его не выдал (ошибка подбора или КЭ исключён); КЭ вне групп в подбор не входит.
    /// </summary>
    public static string NoSelectionReason(ScadConcreteGroupIndex? groups, int? elementId) =>
        groups != null && elementId is int id && groups.Find(id) is { } g
            ? $"SCAD не выдал подбор для КЭ (ЖБ-группа {g.Num} «{g.Name}») — проверьте результаты подбора в SCAD"
            : "КЭ нет в подборе SCAD";

    /// <summary>Привязки арматуры стержня a1 (низ, −Z1) и a2 (верх, +Z1) группы, м.</summary>
    public static (double A1, double A2) BarCovers(ScadConcreteGroup g) => (At(g, 0), At(g, 1));

    static double At(ScadConcreteGroup g, int i) => g.RangeM is { } r && i < r.Length ? r[i] : 0;
}
