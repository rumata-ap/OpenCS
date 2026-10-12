using System.Globalization;
using System.Text.RegularExpressions;
using CScore.Fem;

namespace CScore.Import;

/// <summary>Каталог данных одной установленной версии ЛИРЫ (<c>…\Data</c>) и год версии из имени каталога.</summary>
public sealed record LiraDataDirectory(string Path, int? Version);

/// <summary>
/// Поиск текстового файла схемы ЛИРЫ (<c>Data\&lt;Title&gt;.txt</c>) по каталогам данных всех установленных версий:
/// <c>Общие документы\LIRA SAPR\LIRA SAPR *</c> (2013–2024) и <c>…\LIRASERVICE\LIRA-SAPPHIRE *</c> (2025+). Список
/// версий не зашит — каталоги перебираются по маске.
/// </summary>
public static partial class LiraTextFileLocator
{
    static readonly (string Parent, string Pattern)[] Roots =
        [("LIRA SAPR", "LIRA SAPR *"), ("LIRASERVICE", "LIRA-SAPPHIRE *")];

    [GeneratedRegex(@"(?<!\d)(\d{4})(?!\d)")]
    private static partial Regex YearPattern();

    /// <summary>Общие документы Windows (<c>C:\Users\Public\Documents</c>).</summary>
    public static string PublicDocuments => Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);

    /// <summary>Каталоги Data установленных версий в <paramref name="publicDocuments"/> (нет каталога — пропуск).</summary>
    public static List<LiraDataDirectory> DataDirectories(string publicDocuments)
    {
        var result = new List<LiraDataDirectory>();
        foreach (var (parent, pattern) in Roots)
        {
            string root = System.IO.Path.Combine(publicDocuments, parent);
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> versions;
            try { versions = Directory.EnumerateDirectories(root, pattern).Order(StringComparer.OrdinalIgnoreCase).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (string dir in versions)
            {
                string data = System.IO.Path.Combine(dir, "Data");
                if (Directory.Exists(data)) result.Add(new(data, Version(System.IO.Path.GetFileName(dir))));
            }
        }
        return result;
    }

    /// <summary>Год версии — последнее четырёхзначное число имени каталога («LIRA SAPR 2024» → 2024); нет — null.</summary>
    public static int? Version(string directoryName)
    {
        var matches = YearPattern().Matches(directoryName);
        return matches.Count > 0 ? int.Parse(matches[^1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>
    /// Текстовый файл схемы <paramref name="title"/> в каталогах <paramref name="directories"/>: при нескольких находках —
    /// из каталога версии <paramref name="preferredVersion"/> (версия открытой ЛИРЫ), иначе самый свежий по времени
    /// записи; не найден — null.
    /// </summary>
    public static string? Find(string? title, IEnumerable<LiraDataDirectory> directories, int? preferredVersion)
    {
        string? name = FileName(title);
        if (name == null) return null;
        var found = directories
            .Select(d => (d.Version, Path: System.IO.Path.Combine(d.Path, name)))
            .Where(f => File.Exists(f.Path))
            .Select(f => (f.Version, f.Path, Time: File.GetLastWriteTimeUtc(f.Path)))
            .ToList();
        if (found.Count == 0) return null;
        var preferred = found.Where(f => preferredVersion != null && f.Version == preferredVersion).ToList();
        return (preferred.Count > 0 ? preferred : found).MaxBy(f => f.Time).Path;
    }

    /// <summary>Имя текстового файла схемы: <c>&lt;Title&gt;.txt</c> (расширение .lir в имени отбрасывается); недопустимое имя — null.</summary>
    public static string? FileName(string? title)
    {
        title = title?.Trim();
        if (string.IsNullOrEmpty(title)) return null;
        if (title.EndsWith(".lir", StringComparison.OrdinalIgnoreCase)) title = title[..^4];
        if (title.Length == 0 || title.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) return null;
        return title + ".txt";
    }
}

/// <summary>
/// Итог сверки сетки схемы, импортированной из ЛИРЫ, с текстовым файлом: узлы — по номеру и координатам, КЭ — по номеру
/// и узлам. Расхождение означает файл другой схемы (или схема в ЛИРЕ изменена после импорта): переносить ГУ и нагрузки
/// по номерам нельзя.
/// </summary>
/// <param name="Nodes">Сверено узлов сетки.</param>
/// <param name="Elements">Сверено КЭ сетки.</param>
/// <param name="Missing">Узлов и КЭ сетки, которых нет в файле.</param>
/// <param name="Different">Узлов с другими координатами и КЭ с другими узлами.</param>
/// <param name="Example">Первое расхождение — для сообщения.</param>
public sealed record LiraTextMeshMatch(int Nodes, int Elements, int Missing, int Different, string? Example)
{
    /// <summary>Сетка совпадает с файлом.</summary>
    public bool Ok => Nodes > 0 && Missing == 0 && Different == 0;

    /// <summary>Допуск координат узла, м.</summary>
    public const double ToleranceM = 1e-3;

    /// <summary>Сверяет импортированные узлы и КЭ сетки (теги — номера ЛИРЫ) с документами 1 и 4 файла.</summary>
    public static LiraTextMeshMatch Compare(LiraTextSchema schema, IEnumerable<FemMeshNode> meshNodes,
        IEnumerable<FemElement> meshElements)
    {
        double lu = schema.Units.LengthUnitM;
        int nodes = 0, elements = 0, missing = 0, different = 0;
        string? example = null;
        void Note(string text) => example ??= text;

        foreach (var n in meshNodes.Where(n => n.Origin == FemMember.MeshSourceImported))
        {
            if (!int.TryParse(n.NodeTag, NumberStyles.None, CultureInfo.InvariantCulture, out int num)) continue;
            nodes++;
            if (num < 1 || num > schema.Nodes.Count) { missing++; Note($"узла {num} нет в файле"); continue; }
            var p = schema.Nodes[num - 1];
            double dx = p.X * lu - n.X, dy = p.Y * lu - n.Y, dz = p.Z * lu - n.Z;
            if (Math.Sqrt(dx * dx + dy * dy + dz * dz) > ToleranceM)
            {
                different++;
                Note(string.Format(CultureInfo.InvariantCulture,
                    "узел {0}: в схеме ({1:0.###}; {2:0.###}; {3:0.###}) м, в файле ({4:0.###}; {5:0.###}; {6:0.###}) м",
                    num, n.X, n.Y, n.Z, p.X * lu, p.Y * lu, p.Z * lu));
            }
        }

        var byNumber = new Dictionary<int, LiraTextElement>(schema.Elements.Count);
        foreach (var e in schema.Elements) byNumber.TryAdd(e.Number, e);
        foreach (var e in meshElements.Where(e => e.Origin == FemMember.MeshSourceImported))
        {
            if (!int.TryParse(e.ElemTag, NumberStyles.None, CultureInfo.InvariantCulture, out int num)) continue;
            elements++;
            if (!byNumber.TryGetValue(num, out var te)) { missing++; Note($"КЭ {num} нет в файле"); continue; }
            int[] ids;
            try { ids = System.Text.Json.JsonSerializer.Deserialize<int[]>(e.NodeIdsJson) ?? []; }
            catch (System.Text.Json.JsonException) { ids = []; }
            if (!ids.AsSpan().SequenceEqual(te.Nodes))
            {
                different++;
                Note($"КЭ {num}: узлы в схеме {string.Join(" ", ids)}, в файле {string.Join(" ", te.Nodes)}");
            }
        }
        return new(nodes, elements, missing, different, example);
    }
}
