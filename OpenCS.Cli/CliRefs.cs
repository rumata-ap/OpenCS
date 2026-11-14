using System.Globalization;
using OpenCS.Utilites;

namespace OpenCS.Cli;

/// <summary>
/// Ссылки на объекты проекта в аргументах правящих команд: целое число — id, иначе точный тег.
/// Не найдено или по тегу несколько — ошибка использования (код 2) со списком подходящих.
/// </summary>
static class CliRefs
{
    public static T Resolve<T>(IEnumerable<T> items, string spec, Func<T, int> id, Func<T, string> tag, string kindKey)
    {
        var all = items.ToList();
        string text = spec.Trim();
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            return all.FirstOrDefault(i => id(i) == value)
                ?? throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrNotFound"), Loc.S(kindKey), value));
        var byTag = all.Where(i => string.Equals(tag(i), text, StringComparison.Ordinal)).ToList();
        return byTag.Count switch
        {
            1 => byTag[0],
            0 => throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrRefNotFound"), Loc.S(kindKey), text)),
            _ => throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrRefAmbiguous"), Loc.S(kindKey), text,
                string.Join("; ", byTag.Select(i => $"#{id(i)}")))),
        };
    }
}

/// <summary>Список номеров КЭ «1,2,5-8» и его сжатая запись.</summary>
static class ElementList
{
    public static IReadOnlySet<int> Parse(string text)
    {
        var result = new SortedSet<int>();
        foreach (string raw in text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            string part = raw.Trim();
            int dash = part.IndexOf('-', 1);
            if (dash > 0 && int.TryParse(part[..dash], NumberStyles.Integer, CultureInfo.InvariantCulture, out int from)
                         && int.TryParse(part[(dash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int to)
                         && from <= to && to - from < 1_000_000)
                for (int i = from; i <= to; i++) result.Add(i);
            else if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int one))
                result.Add(one);
            else
                throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrElements"), text));
        }
        if (result.Count == 0) throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrElements"), text));
        return result;
    }

    /// <summary>«1-3, 7» по возрастанию; пусто — пустая строка.</summary>
    public static string Format(IEnumerable<int> elements)
    {
        var sorted = elements.Distinct().Order().ToList();
        var parts = new List<string>();
        for (int i = 0; i < sorted.Count;)
        {
            int j = i;
            while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1) j++;
            parts.Add(j == i ? sorted[i].ToString(CultureInfo.InvariantCulture)
                : j == i + 1 ? $"{sorted[i]}, {sorted[j]}" : $"{sorted[i]}-{sorted[j]}");
            i = j + 1;
        }
        return string.Join(", ", parts);
    }
}
