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
