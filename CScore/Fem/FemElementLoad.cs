using System.Globalization;
using System.Text.Json;

namespace CScore.Fem;

/// <summary>
/// Нагрузка на набор КЭ сетки (сеточный уровень схемы). Одна запись — один вид нагрузки с одними значениями на все КЭ
/// цели, как запись нагрузки SCAD. Цель задаётся КонЭ (их КЭ — по <see cref="FemElement.SourceMemberTag"/>), группой
/// или списком КЭ; пользовательская «нагрузка на пластины» — вид <see cref="FemElementLoadKinds.Uniform"/>.
/// </summary>
public sealed class FemElementLoad
{
    IReadOnlyList<string>? _tags;
    string _tagsJson = "[]";
    double[]? _values;
    string _valuesJson = "[]";

    public int Id { get; set; }
    public int SchemaId { get; set; }
    public int LoadCaseId { get; set; }

    /// <summary>Происхождение: <see cref="FemLoadOrigin.Manual"/> или «import:&lt;источник&gt;».</summary>
    public string Origin { get; set; } = FemLoadOrigin.Manual;

    /// <summary>Вид цели: <see cref="FemLoadTargetKinds"/>.</summary>
    public string TargetKind { get; set; } = FemLoadTargetKinds.Elements;

    /// <summary>JSON-массив тегов цели: теги КонЭ (<see cref="FemLoadTargetKinds.Members"/>) или КЭ
    /// (<see cref="FemLoadTargetKinds.Elements"/>). Писать — через <see cref="SetTargetTags"/>.</summary>
    public string TargetTagsJson
    {
        get => _tagsJson;
        set { _tagsJson = value; _tags = null; }
    }

    /// <summary>Группа цели (<see cref="FemLoadTargetKinds.Group"/>): КЭ или КонЭ по виду группы.</summary>
    public int? GroupId { get; set; }

    /// <summary>Вид нагрузки: <see cref="FemElementLoadKinds"/>.</summary>
    public string LoadKind { get; set; } = FemElementLoadKinds.Uniform;

    /// <summary>Система координат направления: "global" или "local" (местные оси КЭ; у пластины z — нормаль).</summary>
    public string CoordinateSystem { get; set; } = "global";

    /// <summary>Ось направления: "x", "y" или "z". Для собственного веса не используется (всегда −Z).</summary>
    public string Axis { get; set; } = "z";

    /// <summary>JSON-массив чисел вида (единицы СИ). Писать — через <see cref="SetValues"/>.
    /// <list type="bullet">
    /// <item><c>uniform</c>: [q] — стержень Н/м, пластина Па;</item>
    /// <item><c>nodal</c>: [q1…qn] — пластина, Па в узлах КЭ в порядке хранения узлов;</item>
    /// <item><c>point</c>: стержень [P, a] (Н, м от узла 1); пластина [P, x, y] (Н; м в местных осях КЭ от узла 1);</item>
    /// <item><c>self_weight</c>: [k] — коэффициент к собственному весу.</item>
    /// </list></summary>
    public string ValuesJson
    {
        get => _valuesJson;
        set { _valuesJson = value; _values = null; }
    }

    /// <summary>Теги цели в порядке хранения.</summary>
    public IReadOnlyList<string> TargetTags => _tags ??= FemMemberGroup.ParseTags(_tagsJson);

    /// <summary>Значения вида.</summary>
    public IReadOnlyList<double> Values => _values ??= ParseValues(_valuesJson);

    /// <summary>Заменяет теги цели: без пустых и повторов, в порядке первого появления.</summary>
    public void SetTargetTags(IEnumerable<string> tags)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = tags.Select(t => t.Trim()).Where(t => t.Length > 0 && seen.Add(t)).ToList();
        _tagsJson = JsonSerializer.Serialize(list);
        _tags = list;
    }

    /// <summary>Заменяет значения вида.</summary>
    public void SetValues(IEnumerable<double> values)
    {
        var array = values.ToArray();
        _valuesJson = JsonSerializer.Serialize(array);
        _values = array;
    }

    /// <summary>Местная система координат.</summary>
    public bool IsLocal => string.Equals(CoordinateSystem, "local", StringComparison.OrdinalIgnoreCase);

    /// <summary>Индекс оси 0–2; −1 — ось не распознана.</summary>
    public int AxisIndex => Axis.Trim().ToLowerInvariant() switch { "x" => 0, "y" => 1, "z" => 2, _ => -1 };

    static double[] ParseValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            return doc.RootElement.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.Number ? e.GetDouble()
                    : double.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN)
                .ToArray();
        }
        catch (JsonException) { return []; }
    }
}

/// <summary>Виды нагрузки на КЭ.</summary>
public static class FemElementLoadKinds
{
    /// <summary>Равномерная: стержень — по длине, пластина — по площади.</summary>
    public const string Uniform = "uniform";
    /// <summary>Пластина: интенсивность в узлах КЭ (трапециевидная SCAD).</summary>
    public const string Nodal = "nodal";
    /// <summary>Сосредоточенная сила в точке КЭ.</summary>
    public const string Point = "point";
    /// <summary>Собственный вес с коэффициентом.</summary>
    public const string SelfWeight = "self_weight";
}

/// <summary>Виды цели нагрузки.</summary>
public static class FemLoadTargetKinds
{
    /// <summary>Конструктивные элементы по тегам — их КЭ сетки.</summary>
    public const string Members = "members";
    /// <summary>Группа схемы (группа КЭ или группа КонЭ).</summary>
    public const string Group = "group";
    /// <summary>КЭ сетки по тегам.</summary>
    public const string Elements = "elements";
}

/// <summary>Происхождение нагрузок и загружений.</summary>
public static class FemLoadOrigin
{
    /// <summary>Задано пользователем.</summary>
    public const string Manual = "manual";
    /// <summary>Префикс импорта: «import:scad», «import:lira»…</summary>
    public const string ImportPrefix = "import:";

    /// <summary>Происхождение из импорта программы <paramref name="sourceType"/>.</summary>
    public static string Import(string sourceType) => ImportPrefix + sourceType;
}
