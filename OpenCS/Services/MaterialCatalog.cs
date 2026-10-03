using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using CScore;
using CsvHelper;
using CsvHelper.Configuration;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Создание материала по классу из справочника <c>DataSource/*.csv</c> без диалога выбора — с теми же
/// значениями, что даёт справочник материалов при настройках по умолчанию (γb2 = γb3 = 1, влажность 40–75 %).
/// </summary>
public static partial class MaterialCatalog
{
    const string HeavyConcretePrefix = "Бетон_тяжелый";
    const string RebarPrefix = "Арматура стальная";

    /// <summary>Каталог справочника рядом с приложением.</summary>
    public static string DefaultDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DataSource");

    /// <summary>
    /// Ключ класса для сравнения: буквы и число без пробелов, диапазона диаметров и суффиксов
    /// («A500 (6-40 мм)», «А500С» → «A500»); кириллические буквы-двойники — латиницей. Пусто — класс не распознан.
    /// </summary>
    public static string ClassKey(string? tagOrClass)
    {
        var m = ClassRegex().Match((tagOrClass ?? "").Trim().ToUpperInvariant());
        if (!m.Success) return "";
        const string cyr = "АВСЕКМНОРТХ", lat = "ABCEKMHOPTX";
        var letters = m.Groups[1].Value.Select(c => cyr.IndexOf(c) is >= 0 and var i ? lat[i] : c);
        return new string([.. letters]) + m.Groups[2].Value.Replace(',', '.');
    }

    /// <summary>Материал проекта по классу: бетон либо арматура; null — класс не распознан или такого нет.</summary>
    public static Material? FindByClass(IEnumerable<Material> materials, string? materialClass, bool concrete)
    {
        string key = ClassKey(materialClass);
        return key.Length == 0 ? null : materials.FirstOrDefault(m =>
            (concrete ? m.Type == MatType.Concrete : m.Type is MatType.ReSteelF or MatType.ReSteelU)
            && ClassKey(m.Tag) == key);
    }

    /// <summary>Тяжёлый бетон заданного класса; null — класса нет в справочнике.</summary>
    public static Material? CreateHeavyConcrete(string concreteClass, string? directory = null)
    {
        directory ??= DefaultDirectory;
        string key = ClassKey(concreteClass);
        var c = Find(directory, $"{HeavyConcretePrefix}_C.csv", key);
        var cl = Find(directory, $"{HeavyConcretePrefix}_CL.csv", key);
        var n = Find(directory, $"{HeavyConcretePrefix}_N.csv", key);
        var nl = Find(directory, $"{HeavyConcretePrefix}_NL_2.csv", key);
        if (c == null || cl == null || n == null || nl == null) return null;

        // Как в справочнике материалов: приведённые деформации εb1/εbt1 — по 0,6·R/E.
        foreach (var chars in (MaterialChars[])[c, cl, n, nl])
        {
            chars.Et1 = 0.6 * chars.Ft / chars.E;
            chars.Ec1 = 0.6 * chars.Fc / chars.E;
        }
        return Make(n.Tag, "Бетон тяжелый по СП 63.13330", MatType.Concrete, c, cl, n, nl);
    }

    /// <summary>Стальная арматура заданного класса; null — класса нет в справочнике.</summary>
    public static Material? CreateRebar(string rebarClass, string? directory = null)
    {
        directory ??= DefaultDirectory;
        string key = ClassKey(rebarClass);
        var c = Find(directory, $"{RebarPrefix}_C.csv", key);
        var cl = Find(directory, $"{RebarPrefix}_CL.csv", key);
        var n = Find(directory, $"{RebarPrefix}_N.csv", key);
        var nl = Find(directory, $"{RebarPrefix}_NL.csv", key);
        if (c == null || cl == null || n == null || nl == null) return null;
        return Make(n.Tag, "Арматура стальная по СП 63.13330", n.Type, c, cl, n, nl);
    }

    /// <summary>
    /// Конструкционная сталь по СП 16.13330.2017: первая строка справочника, марка которой начинается с
    /// <paramref name="mark"/> («С245» → «С245 (2-20 мм)»); C = CL, N = NL, как при добавлении стали из справочника.
    /// null — марки нет.
    /// </summary>
    public static Material? CreateStructuralSteel(string mark, string? directory = null)
    {
        directory ??= DefaultDirectory;
        var c = FindSteel(directory, "Конструкционная_сталь_C_СП_16_13330_2017.csv", mark);
        var n = FindSteel(directory, "Конструкционная_сталь_N_СП_16_13330_2017.csv", mark);
        if (c == null || n == null) return null;
        c.Type = n.Type = MatType.Steel;
        return Make(n.Tag, "Сталь по СП 16.13330.2017", MatType.Steel, c, c.Clone(), n, n.Clone());
    }

    const string ShapedRolled = "Фасонный прокат";
    const string SteelFileC = "Конструкционная_сталь_C_СП_16_13330_2017.csv";
    const string SteelFileN = "Конструкционная_сталь_N_СП_16_13330_2017.csv";

    /// <summary>
    /// Марка стали схемы-источника в виде справочника: первое слово, заглавными, латинские C/K/T — кириллицей
    /// («C255 ГОСТ 27772-2015» → «С255»). Пусто — марки нет.
    /// </summary>
    public static string NormalizeSteelMark(string? mark)
    {
        string first = (mark ?? "").Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        const string lat = "CKT", cyr = "СКТ";
        return new string([.. first.ToUpperInvariant().Select(ch => lat.IndexOf(ch) is >= 0 and var i ? cyr[i] : ch)]);
    }

    /// <summary>
    /// Конструкционная сталь по СП 16.13330.2017 для профиля: строка марки с диапазоном толщин, в который попадает
    /// <paramref name="thicknessM"/> (наибольшая толщина элементов профиля), — для фасонного проката (<paramref name="shaped"/>)
    /// сначала среди строк «Фасонный прокат», затем среди листового. Тег фасонного — «С255 (4-10 мм), фасонный прокат»
    /// (у листового Ry той же строки другое). null — марки нет или толщина вне диапазонов.
    /// </summary>
    public static Material? CreateStructuralSteel(string mark, double thicknessM, bool shaped, string? directory = null)
    {
        directory ??= DefaultDirectory;
        string key = NormalizeSteelMark(mark);
        if (key.Length == 0) return null;
        var rowsC = SteelRows(directory, SteelFileC, key);
        var rowsN = SteelRows(directory, SteelFileN, key);
        double tMm = thicknessM * 1000;
        foreach (bool shapedRows in shaped ? [true, false] : new[] { false })
        {
            var c = PickByThickness(rowsC, tMm, shapedRows);
            var n = c == null ? null : rowsN.FirstOrDefault(r => r.Chars.Tag == c.Tag && r.Shaped == shapedRows).Chars;
            if (c == null || n == null) continue;
            c.Type = n.Type = MatType.Steel;
            string tag = shapedRows ? $"{n.Tag}, фасонный прокат" : n.Tag;
            return Make(tag, "Сталь по СП 16.13330.2017", MatType.Steel, c, c.Clone(), n, n.Clone());
        }
        return null;
    }

    /// <summary>
    /// Стальной материал проекта, равный <paramref name="steel"/> из справочника: та же строка справочника в теге («С255 (4-10 мм)»,
    /// без «, фасонный прокат») и те же Ry, Ru для C и N; null — такого нет.
    /// </summary>
    public static Material? FindSameSteel(IEnumerable<Material> materials, Material steel)
    {
        static string Row(string tag) => tag.Split(',')[0].Trim();
        static bool Same(MaterialChars? a, MaterialChars? b) =>
            a != null && b != null && Math.Abs(a.Ry - b.Ry) < 1e-6 && Math.Abs(a.Ru - b.Ru) < 1e-6;
        return materials.FirstOrDefault(m => m.Type == MatType.Steel && Row(m.Tag) == Row(steel.Tag)
                                             && Same(m.C, steel.C) && Same(m.N, steel.N));
    }

    static MaterialChars? PickByThickness(List<(MaterialChars Chars, bool Shaped)> rows, double tMm, bool shaped)
    {
        var candidates = rows.Where(r => r.Shaped == shaped).Select(r => (r.Chars, Range: ThicknessRange(r.Chars.Tag)))
            .Where(r => r.Range != null).OrderBy(r => r.Range!.Value.From).ToList();
        for (int i = 0; i < candidates.Count; i++)
        {
            var (from, to) = candidates[i].Range!.Value;
            // Границы диапазонов общие («4-10», «10-20»): толщина на границе — в нижний диапазон.
            if ((tMm > from || i == 0 && tMm >= from - 1e-9) && tMm <= to + 1e-9) return candidates[i].Chars;
        }
        return null;
    }

    static (double From, double To)? ThicknessRange(string tag)
    {
        var m = ThicknessRegex().Match(tag);
        return m.Success
            ? (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))
            : null;
    }

    /// <summary>Строки справочника стали с маркой <paramref name="key"/> и признаком фасонного проката.</summary>
    static List<(MaterialChars Chars, bool Shaped)> SteelRows(string directory, string fileName, string key)
    {
        var rows = new List<(MaterialChars, bool)>();
        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path)) return rows;
        var config = new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" };
        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, config);
        csv.Context.RegisterClassMap<MaterialCharsMap>();
        csv.Read();
        csv.ReadHeader();
        while (csv.Read())
        {
            var chars = csv.GetRecord<MaterialChars>();
            if (chars == null || NormalizeSteelMark(chars.Tag.Split('(')[0]) != key) continue;
            rows.Add((chars, csv.GetField("ProkatType") == ShapedRolled));
        }
        return rows;
    }

    [GeneratedRegex(@"\((\d+(?:\.\d+)?)\s*-\s*(\d+(?:\.\d+)?)\s*мм\)")]
    private static partial Regex ThicknessRegex();

    static MaterialChars? FindSteel(string directory, string fileName, string mark)
    {
        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path)) return null;
        var config = new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" };
        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, config);
        csv.Context.RegisterClassMap<MaterialCharsMap>();
        return csv.GetRecords<MaterialChars>().FirstOrDefault(r =>
            r.Tag.Equals(mark, StringComparison.OrdinalIgnoreCase) || r.Tag.StartsWith(mark + " ", StringComparison.OrdinalIgnoreCase));
    }
    static Material Make(string tag, string description, MatType type,
        MaterialChars c, MaterialChars cl, MaterialChars n, MaterialChars nl)
    {
        var material = new Material(0) { Tag = tag, Description = description, Type = type };
        material.C = c;
        material.CL = cl;
        material.N = n;
        material.NL = nl;
        material.E = material.N!.E;
        material.SetJson();
        return material;
    }

    static MaterialChars? Find(string directory, string fileName, string key)
    {
        string path = Path.Combine(directory, fileName);
        if (key.Length == 0 || !File.Exists(path)) return null;

        var config = new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" };
        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, config);
        csv.Context.RegisterClassMap<MaterialCharsMap>();
        return csv.GetRecords<MaterialChars>().FirstOrDefault(r => ClassKey(r.Tag) == key);
    }

    [GeneratedRegex(@"^([^\d\s(]+)\s*(\d+(?:[.,]\d+)?)")]
    private static partial Regex ClassRegex();
}
