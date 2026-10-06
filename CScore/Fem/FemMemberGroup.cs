using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace CScore.Fem;

/// <summary>Группа — единица нормативной проверки и импорта усилий. Два вида (<see cref="Kind"/>):
/// группа КЭ — состав задан номерами КЭ сетки (так приходят группы ЛИРЫ и SCAD); группа КонЭ — состав
/// задан тегами конструктивных элементов (FemMember) и следует за ними при переименовании и удалении.
/// Сечение и GJ-стратегия у группы не хранятся: они назначаются напрямую каждому FemMember / КЭ.
/// Tag и MemberType уведомляют об изменении — их показывает дерево проекта при переименовании.</summary>
public class FemMemberGroup : IFemCheckable, INotifyPropertyChanged
{
    /// <summary>Группа КЭ: теги — номера КЭ сетки.</summary>
    public const string KindMesh = "mesh";
    /// <summary>Группа КонЭ: теги — теги конструктивных элементов.</summary>
    public const string KindMembers = "members";

    /// <summary>Создана пользователем.</summary>
    public const string OriginManual = "manual";
    /// <summary>Создана автоматикой OpenCS (авто-группировка по сечению).</summary>
    public const string OriginAuto = "auto";
    /// <summary>Префикс происхождения из импорта: «import:lira», «import:scad», «import:robot»…</summary>
    public const string OriginImportPrefix = "import:";

    /// <summary>Происхождение группы, пришедшей из импорта программы <paramref name="sourceType"/>.</summary>
    public static string ImportOrigin(string sourceType) => OriginImportPrefix + sourceType;

    string  _tag = "";
    string? _memberType;
    string  _tagsJson = "[]";
    IReadOnlyList<string>? _tags;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int     Id               { get; set; }
    public int     SchemaId         { get; set; }
    public string  Tag
    {
        get => _tag;
        set { if (_tag == value) return; _tag = value; PropertyChanged?.Invoke(this, new(nameof(Tag))); }
    }
    /// <summary>Код типа (<see cref="FemMemberTypes"/>); null — тип не задан (определяется по составу КЭ).</summary>
    public string? MemberType
    {
        get => _memberType;
        set { if (_memberType == value) return; _memberType = value; PropertyChanged?.Invoke(this, new(nameof(MemberType))); }
    }
    /// <summary>Вид группы: <see cref="KindMesh"/> или <see cref="KindMembers"/>.</summary>
    public string  Kind             { get; set; } = KindMembers;
    /// <summary>Кто создал группу: <see cref="OriginManual"/>, <see cref="OriginAuto"/>, «import:&lt;источник&gt;».</summary>
    public string  Origin           { get; set; } = OriginManual;
    /// <summary>JSON-массив тегов состава (строки). Читается и прежний формат — массив чисел.
    /// Писать состав — через <see cref="SetTags"/>.</summary>
    public string  MemberTagsJson
    {
        get => _tagsJson;
        set { _tagsJson = value; _tags = null; }
    }
    /// <summary>FK → plate_sections.id. Сечение для нормативных проверок (пластины/стены) — оболочки вне рамок этого среза.</summary>
    public int?    PlateSectionId   { get; set; }
    /// <summary>FK → force_sets.id. Набор усилий (source_type='fea').</summary>
    public int?    ForceSetId       { get; set; }
    /// <summary>JSON-сериализация SteelDesignParams (lef, γc, профиль и условия проверки).</summary>
    public string? DesignParamsJson { get; set; }
    /// <summary>Проверки, привязанные к этой группе (eager-loaded).</summary>
    public ObservableCollection<FemCheck> Checks { get; } = [];

    /// <summary>Группа КЭ (состав — номера КЭ сетки).</summary>
    public bool IsMeshGroup => Kind == KindMesh;

    /// <summary>Теги состава в порядке хранения.</summary>
    public IReadOnlyList<string> Tags => _tags ??= ParseTags(_tagsJson);

    /// <summary>Заменяет состав: теги без пустых и без повторов, в порядке первого появления.</summary>
    public void SetTags(IEnumerable<string> tags)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = tags.Select(t => t.Trim()).Where(t => t.Length > 0 && seen.Add(t)).ToList();
        _tagsJson = JsonSerializer.Serialize(list);
        _tags = list;
    }

    /// <summary>Разбирает JSON состава: массив строк или (прежний формат) массив чисел. Битый JSON — пусто.</summary>
    public static IReadOnlyList<string> ParseTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            var result = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                string? tag = item.ValueKind switch
                {
                    JsonValueKind.String => item.GetString(),
                    JsonValueKind.Number => item.TryGetInt64(out long n)
                        ? n.ToString(CultureInfo.InvariantCulture)
                        : item.GetRawText(),
                    _ => null,
                };
                if (!string.IsNullOrWhiteSpace(tag)) result.Add(tag.Trim());
            }
            return result;
        }
        catch (JsonException) { return []; }
    }
}

/// <summary>Коды типов групп и конструктивных элементов. В БД хранится код, в интерфейсе — локализованное имя.</summary>
public static class FemMemberTypes
{
    public const string Beam     = "beam";
    public const string Column   = "column";
    public const string Plate    = "plate";
    public const string Wall     = "wall";
    /// <summary>Пластины без уточнения (плита или стена) — так типизирует импорт по жёсткостям.</summary>
    public const string Shell    = "shell";
    public const string Truss    = "truss";
    public const string Diagonal = "diagonal";
    public const string Bracing  = "bracing";
    public const string Other    = "other";

    /// <summary>Коды, предлагаемые пользователю, в порядке списка.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Beam, Column, Plate, Wall, Shell, Truss, Diagonal, Bracing, Other];

    /// <summary>Тип плоского элемента (сечение — пластинчатое).</summary>
    public static bool IsPlanar(string? code) => code is Plate or Wall or Shell;

    /// <summary>Код по прежнему свободному тексту (русские имена из списков интерфейса) или по коду.
    /// Неизвестное значение возвращается как есть; пустое — null.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (All.Contains(v)) return v;
        return v.ToLowerInvariant() switch
        {
            "балка"   => Beam,
            "колонна" => Column,
            "плита"   => Plate,
            "стена"   => Wall,
            "пластина" or "пластины" => Shell,
            "ферма"   => Truss,
            "раскос"  => Diagonal,
            "связь"   => Bracing,
            "другое"  => Other,
            _ => v,
        };
    }
}
