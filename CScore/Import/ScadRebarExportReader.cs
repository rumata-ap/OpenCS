using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CScore.Import;

/// <summary>
/// Подобранная SCAD арматура пластины (выгрузка плагина «Экспорт для OpenCS»), см²/м.
/// Null — SCAD вернул NaN/∞ (подбор не выполнен).
/// </summary>
/// <param name="ElementId">Номер КЭ SCAD.</param>
/// <param name="As1">Нижняя (Z−) по X1.</param>
/// <param name="As2">Верхняя (Z+) по X1.</param>
/// <param name="As3">Нижняя (Z−) по Y1.</param>
/// <param name="As4">Верхняя (Z+) по Y1.</param>
/// <param name="AswX">Поперечная ASWx.</param>
/// <param name="AswY">Поперечная ASWy.</param>
public sealed record ScadSelectedPlate(
    int ElementId, double? As1, double? As2, double? As3, double? As4, double? AswX, double? AswY);

/// <summary>
/// Подбор SCAD в одном сечении стержня: каждая S — площадь одной грани, см²; поперечная — см²/м.
/// Null в поле — SCAD вернул NaN/∞.
/// </summary>
/// <param name="As1">Грань −Z1 (низ), с угловыми.</param>
/// <param name="As2">Грань +Z1 (верх), с угловыми.</param>
/// <param name="As3">Грань −Y1.</param>
/// <param name="As4">Грань +Y1.</param>
/// <param name="IwZ">Поперечная с ветвями ‖ Z1 (IWx плагина, IWz экрана SCAD; на Qz).</param>
/// <param name="IwY">Поперечная с ветвями ‖ Y1 (IWy; на Qy).</param>
public sealed record ScadSelectedBarSection(
    double? As1, double? As2, double? As3, double? As4, double? IwZ, double? IwY)
{
    /// <summary>Вся продольная арматура сечения S1 + S2 + S3 + S4, см²; null — нет ни одной площади.</summary>
    public double? LongitudinalSum =>
        As1 == null && As2 == null && As3 == null && As4 == null
            ? null
            : (As1 ?? 0) + (As2 ?? 0) + (As3 ?? 0) + (As4 ?? 0);

    /// <summary>Все четыре продольные площади известны.</summary>
    public bool IsComplete => As1 != null && As2 != null && As3 != null && As4 != null;
}

/// <summary>Подобранная SCAD арматура стержня по сечениям вдоль КЭ.</summary>
/// <param name="ElementId">Номер КЭ SCAD.</param>
/// <param name="Sections">Сечения в порядке SCAD (1, 2, …); null — SCAD не выдал результат сечения.</param>
public sealed record ScadSelectedBar(int ElementId, IReadOnlyList<ScadSelectedBarSection?> Sections)
{
    /// <summary>Поэлементный максимум по сечениям без null; null — ни одного сечения с результатом.</summary>
    public ScadSelectedBarSection? Envelope { get; } = BuildEnvelope(Sections);

    static ScadSelectedBarSection? BuildEnvelope(IReadOnlyList<ScadSelectedBarSection?> sections)
    {
        var present = sections.Where(s => s != null).Select(s => s!).ToList();
        if (present.Count == 0) return null;
        static double? Max(IEnumerable<double?> values)
        {
            double? max = null;
            foreach (var v in values)
                if (v is double d && (max == null || d > max)) max = d;
            return max;
        }
        return new ScadSelectedBarSection(
            Max(present.Select(s => s.As1)), Max(present.Select(s => s.As2)),
            Max(present.Select(s => s.As3)), Max(present.Select(s => s.As4)),
            Max(present.Select(s => s.IwZ)), Max(present.Select(s => s.IwY)));
    }
}

/// <summary>Настройка единиц сеанса SCAD из выгрузки плагина (только для диагностики).</summary>
/// <param name="Id">Имя настройки SCAD (theResultArmSquare …).</param>
/// <param name="Name">Обозначение единицы (UnitsName).</param>
/// <param name="Title">Название единицы (UserFriendlyName).</param>
/// <param name="Factor">Множитель показа SCAD.</param>
/// <param name="Error">Ошибка SCAD при чтении настройки; null — прочитана.</param>
public sealed record ScadExportUnit(string Id, string? Name, string? Title, double? Factor, string? Error);

/// <summary>Выгрузка плагина SCAD «Экспорт для OpenCS» (*.opencs-scad.json).</summary>
public sealed class ScadSelectedRebarFile
{
    /// <summary>Путь к проекту SCAD (*.SPR), из которого сделана выгрузка.</summary>
    public string Project { get; init; } = "";
    /// <summary>Имя проекта SCAD.</summary>
    public string Name { get; init; } = "";
    /// <summary>Момент выгрузки (UTC); null — не распознан.</summary>
    public DateTimeOffset? Exported { get; init; }
    /// <summary>Настройки единиц сеанса SCAD по имени.</summary>
    public IReadOnlyDictionary<string, ScadExportUnit> Units { get; init; } = new Dictionary<string, ScadExportUnit>();
    /// <summary>Пластины по номеру КЭ SCAD (площади в см²/м).</summary>
    public IReadOnlyDictionary<int, ScadSelectedPlate> Plates { get; init; } = new Dictionary<int, ScadSelectedPlate>();
    /// <summary>Стержни по номеру КЭ SCAD (площади в см², поперечная — см²/м).</summary>
    public IReadOnlyDictionary<int, ScadSelectedBar> Bars { get; init; } = new Dictionary<int, ScadSelectedBar>();
    /// <summary>Предупреждения разбора.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Сверка номеров и видов КЭ выгрузки со схемой.</summary>
    public LiraAspSchemaMatch MatchSchema(IEnumerable<(int Id, bool IsPlate)> elements) =>
        LiraAspSchemaMatch.Check(Plates.Keys, Bars.Keys, elements);
}

/// <summary>
/// Читатель выгрузки плагина SCAD «Экспорт для OpenCS» (формат <c>opencs-scad-rebar</c>, версия 1).
/// Плагин пишет значения во внутренних единицах SCAD (СИ): м² у стержней, м²/м у пластин и поперечной;
/// <c>factor</c> настройки <c>theResultArmSquare</c> описывает только показ. Значения переводятся ×1e4 в см² и см²/м.
/// </summary>
/// <remarks>
/// Кодировка — по BOM (плагин пишет UTF-16 LE с BOM; UTF-8 с BOM и без него тоже читается).
/// <code>
/// {"format":"opencs-scad-rebar","version":1,"project":"C:\\…\\x.SPR","name":"…",
///  "exported":"Thu, 1 Oct 2026 12:00:00 UTC",
///  "units":[{"id":"theResultArmSquare","name":"см2","title":"…","factor":0.0001} | {"id":…,"error":…}, …],
///  "plates":[{"e":1,"as":[AS1,AS2,AS3,AS4],"asw":[ASWx,ASWy]}, …],
///  "bars":[{"e":5,"sections":[{"as":[AS1..AS4],"iw":[IWx,IWy]} | null, …]}, …]}
/// </code>
/// NaN/∞ плагин пишет как null.
/// </remarks>
public static class ScadRebarExportReader
{
    /// <summary>Значение поля <c>format</c>.</summary>
    public const string Format = "opencs-scad-rebar";

    /// <summary>Старшая поддерживаемая версия формата.</summary>
    public const int MaxVersion = 1;

    /// <summary>Расширение файла выгрузки (рядом с *.SPR).</summary>
    public const string FileSuffix = ".opencs-scad.json";

    /// <summary>Множитель «внутренние единицы SCAD (м²) → см²».</summary>
    public const double ToCm2 = 1e4;

    const string AreaUnitId = "theResultArmSquare";
    const double StandardAreaFactor = 1e-4;
    const int MaxListed = 10;

    /// <summary>Прочитать файл выгрузки.</summary>
    public static ScadSelectedRebarFile Read(string path) => Read(File.ReadAllBytes(path));

    /// <summary>Разобрать содержимое файла выгрузки.</summary>
    /// <exception cref="InvalidDataException">Не выгрузка плагина OpenCS, версия новее поддерживаемой или JSON повреждён.</exception>
    public static ScadSelectedRebarFile Read(byte[] data)
    {
        string text = Decode(data);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Файл не является выгрузкой плагина SCAD для OpenCS: {ex.Message}", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Str(root, "format") != Format)
                throw new InvalidDataException(
                    $"Файл не является выгрузкой плагина SCAD для OpenCS (ожидался формат «{Format}»).");
            int version = root.TryGetProperty("version", out var v) && v.TryGetInt32(out int ver) ? ver : 0;
            if (version < 1 || version > MaxVersion)
                throw new InvalidDataException(
                    $"Версия выгрузки плагина SCAD {version} не поддерживается (поддерживается до {MaxVersion}) — обновите OpenCS.");

            var warnings = new List<string>();
            var units = ReadUnits(root, warnings);
            var negative = new SortedSet<int>();
            var duplicates = new SortedSet<int>();

            var plates = new Dictionary<int, ScadSelectedPlate>();
            foreach (var p in Array(root, "plates"))
            {
                if (Id(p) is not int id) continue;
                var a = Numbers(p, "as", 4);
                var w = Numbers(p, "asw", 2);
                var plate = new ScadSelectedPlate(id, a[0], a[1], a[2], a[3], w[0], w[1]);
                if (a.Concat(w).Any(x => x < 0)) negative.Add(id);
                if (!plates.TryAdd(id, plate)) duplicates.Add(id);
            }

            var bars = new Dictionary<int, ScadSelectedBar>();
            foreach (var b in Array(root, "bars"))
            {
                if (Id(b) is not int id) continue;
                var sections = new List<ScadSelectedBarSection?>();
                foreach (var s in Array(b, "sections"))
                {
                    if (s.ValueKind != JsonValueKind.Object) { sections.Add(null); continue; }
                    var a = Numbers(s, "as", 4);
                    var w = Numbers(s, "iw", 2);
                    if (a.Concat(w).Any(x => x < 0)) negative.Add(id);
                    sections.Add(new ScadSelectedBarSection(a[0], a[1], a[2], a[3], w[0], w[1]));
                }
                if (!bars.TryAdd(id, new ScadSelectedBar(id, sections))) duplicates.Add(id);
            }

            if (duplicates.Count > 0)
                warnings.Add($"КЭ встречаются в выгрузке SCAD дважды — оставлена первая запись: {List(duplicates)}.");
            if (negative.Count > 0)
                warnings.Add($"Отрицательные площади арматуры в выгрузке SCAD у КЭ: {List(negative)}.");

            return new ScadSelectedRebarFile
            {
                Project = Str(root, "project") ?? "",
                Name = Str(root, "name") ?? "",
                Exported = ParseDate(Str(root, "exported")),
                Units = units,
                Plates = plates,
                Bars = bars,
                Warnings = warnings,
            };
        }
    }

    /// <summary>Текст файла по BOM: UTF-16 LE/BE или UTF-8 (без BOM — UTF-8).</summary>
    static string Decode(byte[] data)
    {
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            return Encoding.Unicode.GetString(data, 2, data.Length - 2);
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return Encoding.UTF8.GetString(data, 3, data.Length - 3);
        return Encoding.UTF8.GetString(data);
    }

    static Dictionary<string, ScadExportUnit> ReadUnits(JsonElement root, List<string> warnings)
    {
        var units = new Dictionary<string, ScadExportUnit>(StringComparer.Ordinal);
        foreach (var u in Array(root, "units"))
        {
            if (u.ValueKind != JsonValueKind.Object || Str(u, "id") is not { Length: > 0 } id) continue;
            var unit = new ScadExportUnit(id, Str(u, "name"), Str(u, "title"), Number(u, "factor"), Str(u, "error"));
            units.TryAdd(id, unit);
            if (unit.Error != null)
                warnings.Add($"SCAD не выдал настройку единиц {id}: {unit.Error}.");
        }

        if (units.TryGetValue(AreaUnitId, out var area) && area.Error == null && area.Factor is double f
            && Math.Abs(f - StandardAreaFactor) > 0.01 * StandardAreaFactor)
            warnings.Add(string.Format(CultureInfo.InvariantCulture,
                "Единицы показа площадей арматуры SCAD нестандартные ({0}, множитель {1:G6}) — значения считаются " +
                "в СИ (м²), сверьте с экраном SCAD.", area.Name ?? "?", f));
        return units;
    }

    /// <summary>Разбор <c>Date.toUTCString()</c> («Thu, 1 Oct 2026 12:00:00 UTC»); null — не распознано.</summary>
    static DateTimeOffset? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        string t = s.Trim();
        if (t.EndsWith(" UTC", StringComparison.Ordinal)) t = t[..^4] + " GMT";
        string[] formats = ["ddd, d MMM yyyy HH:mm:ss 'GMT'", "ddd, dd MMM yyyy HH:mm:ss 'GMT'", "r", "o"];
        if (DateTimeOffset.TryParseExact(t, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var d))
            return d.ToUniversalTime();
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out d)
            ? d.ToUniversalTime() : null;
    }

    static IEnumerable<JsonElement> Array(JsonElement e, string name) =>
        e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray() : [];

    static int? Id(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty("e", out var v) && v.TryGetInt32(out int id) ? id : null;

    /// <summary>Массив чисел длины <paramref name="count"/>, переведённых ×1e4; null — нет числа.</summary>
    static double?[] Numbers(JsonElement e, string name, int count)
    {
        var result = new double?[count];
        if (!e.TryGetProperty(name, out var a) || a.ValueKind != JsonValueKind.Array) return result;
        int i = 0;
        foreach (var x in a.EnumerateArray())
        {
            if (i >= count) break;
            if (x.ValueKind == JsonValueKind.Number && x.TryGetDouble(out double d) && double.IsFinite(d))
                result[i] = d * ToCm2;
            i++;
        }
        return result;
    }

    static double? Number(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) ? d : null;

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static string List(IReadOnlyCollection<int> ids) =>
        string.Join(", ", ids.Take(MaxListed)) + (ids.Count > MaxListed ? $" … (всего {ids.Count})" : "");
}
