using System.Globalization;
using System.Text;

namespace CScore.Import;

/// <summary>Оси колонок таблицы усилий.</summary>
public enum ForceTableAxes
{
    /// <summary>По заголовкам: есть Mz, Qz или Mk — ЛИРА-САПР, иначе OpenCS.</summary>
    Auto,
    /// <summary>Местные оси стержня ЛИРА-САПР (N, Mk/Mx, My, Mz, Qy, Qz) — переводятся в оси сечения OpenCS.</summary>
    Lira,
    /// <summary>Оси сечения OpenCS (N, Mx, My, Vx/Qx, Vy/Qy, T/Mk).</summary>
    OpenCs,
}

/// <summary>Параметры разбора таблицы усилий.</summary>
public sealed class ForceTableOptions
{
    public ForceTableAxes Axes { get; init; } = ForceTableAxes.Auto;

    /// <summary>Множитель к кН (кН·м) для колонок без единиц в заголовке.</summary>
    public double DefaultScale { get; init; } = 1;

    /// <summary>Множитель т → кН.</summary>
    public double TonToKn { get; init; } = 9.80665;

    /// <summary>Только строки этих КЭ; null — все.</summary>
    public IReadOnlySet<int>? Elements { get; init; }
}

/// <summary>Результат разбора: набор стержней, выбранные оси, описание переводов колонок.</summary>
public sealed class ForceTableResult
{
    public ForceSet? Set { get; set; }
    public ForceTableAxes Axes { get; set; }
    /// <summary>Переводы колонок: «My (кН*м) → Mx (−)».</summary>
    public List<string> Columns { get; } = [];
    public List<string> Warnings { get; } = [];
    public string? Error { get; set; }
    public bool Success => Error == null;
}

/// <summary>
/// Разбор таблицы усилий стержней из ячеек (<see cref="ForceTableReader"/>): строка заголовков — первая, где есть
/// колонка элемента и хотя бы одна колонка усилия; единицы — из скобок заголовка; оси ЛИРА-САПР переводятся
/// <see cref="LiraForceMapper"/>; пустые «элемент/сечение» — значение сверху; прочие колонки — в метку строки.
/// </summary>
public static class ForceTableParser
{
    enum Role { Element, Section, Label, Force, Extra }

    sealed record Column(int Index, Role Role, string Key, string Display, double Scale);

    static readonly HashSet<string> ElementKeys = Keys("ЭЛ", "ЭЛЕМ", "ЭЛЕМЕНТ", "КЭ", "НОМЕРКЭ", "ELEM", "ELEMENT", "EL");
    static readonly HashSet<string> SectionKeys = Keys("НС", "СЕЧ", "СЕЧЕНИЕ", "НОМЕРСЕЧЕНИЯ", "SEC", "SECT", "SECTION");
    static readonly HashSet<string> LabelKeys = Keys("МЕТКА", "LABEL", "ИМЯ", "NAME");
    static readonly HashSet<string> ForceKeys = Keys("N", "MX", "MY", "MZ", "MK", "QX", "QY", "QZ", "VX", "VY", "T");
    static readonly HashSet<string> LiraOnly = Keys("MZ", "QZ", "MK");

    static HashSet<string> Keys(params string[] names) => names.Select(Normalize).ToHashSet();

    /// <summary>Ключ имени колонки: без единиц в скобках, пробелов, точек и «№», заглавными, кириллица-двойники — латиницей.</summary>
    public static string Normalize(string header)
    {
        header = UnitsInParentheses(header);
        const string cyr = "АВЕКМНОРСТХУ", lat = "ABEKMHOPCTXY";
        var sb = new StringBuilder();
        int depth = 0;
        foreach (char raw in header)
        {
            if (raw is '(' or '[') { depth++; continue; }
            if (raw is ')' or ']') { depth = Math.Max(0, depth - 1); continue; }
            if (depth > 0 || char.IsWhiteSpace(raw) || raw is '.' or '№' or '#' or ':' or ',' or '_' or '-') continue;
            char c = char.ToUpperInvariant(raw);
            int i = cyr.IndexOf(c);
            sb.Append(i >= 0 ? lat[i] : c);
        }
        return sb.ToString();
    }

    /// <summary>Текст в скобках заголовка (единицы); null — нет.</summary>
    /// <summary>«N, т» → «N (т)»: единицы после запятой — как в скобках.</summary>
    static string UnitsInParentheses(string header)
    {
        int comma = header.IndexOf(',');
        return comma > 0 && header.IndexOfAny(['(', '[']) < 0
            ? $"{header[..comma].Trim()} ({header[(comma + 1)..].Trim()})"
            : header;
    }

    static string? UnitOf(string header)
    {
        header = UnitsInParentheses(header);
        int open = header.LastIndexOfAny(['(', '[']);
        int close = header.LastIndexOfAny([')', ']']);
        return open >= 0 && close > open ? header[(open + 1)..close].Trim() : null;
    }

    /// <summary>Заголовок для метки строки: без единиц в скобках.</summary>
    static string DisplayOf(string header)
    {
        header = UnitsInParentheses(header);
        int open = header.IndexOfAny(['(', '[']);
        return (open >= 0 ? header[..open] : header).Trim();
    }

    /// <summary>Множитель единиц к кН / кН·м; null — единицы не распознаны.</summary>
    static double? UnitScale(string unit, double tonToKn)
    {
        string u = unit.ToLowerInvariant().Replace(" ", "").Replace('·', '*').Replace('×', '*').Replace('х', '*')
            .Replace('x', '*');
        string force = u, length = "";
        int star = u.IndexOf('*');
        if (star >= 0) { force = u[..star]; length = u[(star + 1)..]; }
        else if (u.Length > 1 && (u.EndsWith('м') || u.EndsWith('m')) && u is not ("мн" or "mn"))
        {
            // «кНм», «тм» без знака умножения.
            force = u[..^1];
            length = u[^1..];
        }
        double? f = force switch
        {
            "кн" or "kn" => 1,
            "т" or "тс" or "t" or "tf" => tonToKn,
            "н" or "n" => 1e-3,
            "мн" or "mn" => 1e3,
            _ => null,
        };
        double? l = length switch
        {
            "" or "м" or "m" => 1,
            "см" or "cm" => 1e-2,
            "мм" or "mm" => 1e-3,
            _ => null,
        };
        return f * l;
    }

    public static ForceTableResult Parse(IReadOnlyList<ForceTable> tables, ForceTableOptions options)
    {
        var result = new ForceTableResult();
        foreach (var table in tables)
            for (int r = 0; r < table.Rows.Count; r++)
            {
                var header = table.Rows[r];
                var keys = header.Select(Normalize).ToList();
                if (!keys.Any(ElementKeys.Contains) || !keys.Any(ForceKeys.Contains)) continue;
                ParseTable(table, r, options, result);
                return result;
            }
        result.Error = "Не найдена строка заголовков: нужны колонка элемента (Элем, КЭ) и колонки усилий (N, My, Qz…).";
        return result;
    }

    static void ParseTable(ForceTable table, int headerRow, ForceTableOptions options, ForceTableResult result)
    {
        var header = table.Rows[headerRow];
        var columns = new List<Column>();
        for (int i = 0; i < header.Count; i++)
        {
            string text = header[i].Trim();
            if (text.Length == 0) continue;
            string key = Normalize(text);
            Role role = ElementKeys.Contains(key) && !columns.Any(c => c.Role == Role.Element) ? Role.Element
                : SectionKeys.Contains(key) && !columns.Any(c => c.Role == Role.Section) ? Role.Section
                : LabelKeys.Contains(key) && !columns.Any(c => c.Role == Role.Label) ? Role.Label
                : ForceKeys.Contains(key) ? Role.Force
                : Role.Extra;
            double scale = 1;
            if (role == Role.Force)
            {
                if (columns.Any(c => c.Role == Role.Force && c.Key == key))
                {
                    result.Error = $"Колонка «{text}» повторяется.";
                    return;
                }
                string? unit = UnitOf(text);
                if (unit == null) scale = options.DefaultScale;
                else if (UnitScale(unit, options.TonToKn) is { } s) scale = s;
                else
                {
                    result.Error = $"Колонка «{text}»: единицы «{unit}» не распознаны (кН, кН*м, т, т*м, Н, МН).";
                    return;
                }
            }
            columns.Add(new Column(i, role, key, DisplayOf(text), scale));
        }

        var forces = columns.Where(c => c.Role == Role.Force).ToList();
        var axes = options.Axes;
        if (axes == ForceTableAxes.Auto)
            axes = forces.Any(c => LiraOnly.Contains(c.Key)) ? ForceTableAxes.Lira : ForceTableAxes.OpenCs;
        result.Axes = axes;
        foreach (var c in forces)
        {
            string? target = Target(c.Key, axes);
            if (target == null)
            {
                result.Error = $"Колонка «{header[c.Index].Trim()}» не относится к осям {(axes == ForceTableAxes.Lira ? "ЛИРА-САПР" : "OpenCS")}.";
                return;
            }
            string scale = Math.Abs(c.Scale - 1) < 1e-12 ? "" : $" ×{c.Scale.ToString("0.#####", CultureInfo.InvariantCulture)}";
            result.Columns.Add($"{header[c.Index].Trim()} → {target}{scale}");
        }

        var set = new ForceSet { Kind = "bar", SourceType = "table" };
        var element = columns.First(c => c.Role == Role.Element);
        var section = columns.FirstOrDefault(c => c.Role == Role.Section);
        var label = columns.FirstOrDefault(c => c.Role == Role.Label);
        var extras = columns.Where(c => c.Role == Role.Extra).ToList();
        string lastElement = "", lastSection = "";
        for (int r = headerRow + 1; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            string Cell(Column c) => c.Index < row.Count ? row[c.Index].Trim() : "";
            if (row.All(string.IsNullOrWhiteSpace)) continue;

            string elementText = Cell(element);
            string sectionText = section == null ? "" : Cell(section);
            if (elementText.Length > 0) { lastElement = elementText; lastSection = ""; }
            else elementText = lastElement;
            if (sectionText.Length > 0) lastSection = sectionText;
            else sectionText = lastSection;

            // Строка без единого числа в колонках усилий — подзаголовок или пустая строка, не данные.
            if (!forces.Any(c => TryNumber(Cell(c), out _))) continue;
            var values = new Dictionary<string, double>();
            foreach (var c in forces)
            {
                string text = Cell(c);
                if (text.Length == 0) continue;
                if (!TryNumber(text, out double v))
                {
                    result.Error = $"{table.Name}, строка {r + 1}, колонка «{header[c.Index].Trim()}»: «{text}» — не число.";
                    return;
                }
                values[c.Key] = v * c.Scale;
            }

            int? elementNum = TryInt(elementText);
            if (elementNum == null)
            {
                result.Error = $"{table.Name}, строка {r + 1}: номер элемента «{elementText}» — не целое число.";
                return;
            }
            if (options.Elements != null && !options.Elements.Contains(elementNum.Value)) continue;
            int? sectionNum = TryInt(sectionText);

            var item = axes == ForceTableAxes.Lira ? MapLira(values) : MapOpenCs(values);
            item.Num = set.Items.Count + 1;
            item.SourceElementNum = elementNum;
            item.SourceSectionNum = sectionNum;
            item.Label = label != null && Cell(label).Length > 0
                ? Cell(label)
                : BuildLabel(elementNum.Value, sectionText, extras.Select(e => (e.Display, Cell(e))));
            set.Items.Add(item);
        }

        if (set.Items.Count == 0)
        {
            result.Error = options.Elements == null ? "В таблице нет строк с усилиями." : "Нет строк для заданных КЭ.";
            return;
        }
        result.Set = set;
    }

    static string? Target(string key, ForceTableAxes axes) => axes == ForceTableAxes.Lira
        ? key switch
        {
            "N" => "N", "MX" or "MK" or "T" => "T", "MY" => "Mx (−)", "MZ" => "My (−)", "QZ" => "Vy", "QY" => "Vx",
            _ => null,
        }
        : key switch
        {
            "N" => "N", "MX" => "Mx", "MY" => "My", "QX" or "VX" => "Vx", "QY" or "VY" => "Vy", "T" or "MK" => "T",
            _ => null,
        };

    static LoadItem MapLira(Dictionary<string, double> v)
    {
        // Ключи LiraForceMapper: MX — кручение; Mk — его другое имя.
        var src = new Dictionary<string, double>();
        foreach (var (k, value) in v) src[k is "MK" or "T" ? "MX" : k] = value;
        return LiraForceMapper.MapBar(src, new LiraUnitScales(1, 1, 1, 1, 1), LiraImportOptions.Default);
    }

    static LoadItem MapOpenCs(Dictionary<string, double> v)
    {
        double Get(params string[] keys) => keys.Select(k => v.TryGetValue(k, out double x) ? x : 0).Sum();
        return new LoadItem
        {
            N = Get("N"), Mx = Get("MX"), My = Get("MY"), Vx = Get("QX", "VX"), Vy = Get("QY", "VY"), T = Get("T", "MK"),
        };
    }

    static string BuildLabel(int element, string section, IEnumerable<(string Header, string Value)> extras)
    {
        var parts = new List<string> { $"Эл. {element}" };
        if (section.Length > 0) parts.Add($"сеч. {section}");
        parts.AddRange(extras.Where(e => e.Value.Length > 0).Select(e => e.Header.Length > 0 ? $"{e.Header} {e.Value}" : e.Value));
        return string.Join(", ", parts);
    }

    /// <summary>Число с точкой или запятой; «−» — минус; пробелы-разделители тысяч убираются.</summary>
    public static bool TryNumber(string text, out double value)
    {
        string s = text.Trim().Replace('−', '-').Replace(" ", "").Replace(" ", "").Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    static int? TryInt(string text) =>
        TryNumber(text, out double v) && Math.Abs(v - Math.Round(v)) < 1e-9 && Math.Abs(v) < int.MaxValue ? (int)Math.Round(v) : null;
}

/// <summary>Импорт таблицы усилий стержней из файла: чтение (<see cref="ForceTableReader"/>) и разбор (<see cref="ForceTableParser"/>).</summary>
public static class ForceTableImporter
{
    public static ForceTableResult ImportFile(string path, ForceTableOptions options, string? sheet = null)
    {
        try
        {
            var result = ForceTableParser.Parse(ForceTableReader.Read(path, sheet), options);
            if (result.Set != null) result.Set.Tag = Path.GetFileNameWithoutExtension(path);
            return result;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException
                                       or NotSupportedException or UnauthorizedAccessException
                                       or System.Xml.XmlException or ExcelDataReader.Exceptions.ExcelReaderException)
        {
            return new ForceTableResult { Error = ex.Message };
        }
    }
}
