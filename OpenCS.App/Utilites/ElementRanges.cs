using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenCS.Utilites;

/// <summary>
/// Списки номеров КЭ «101-103 106, 116-118; 121»: один разбор для окон, импорта и консоли. Разделители — пробел,
/// табуляция, запятая, точка с запятой; диапазон — «a-b» (допускаются пробелы вокруг дефиса и тире «–»). Мусор и
/// обратный диапазон — ошибка с текстом, а не молчаливый пропуск.
/// </summary>
public static partial class ElementRanges
{
    /// <summary>Наибольшая длина одного диапазона (защита от опечатки «1-10000000000»).</summary>
    public const int MaxRangeLength = 1_000_000;

    [GeneratedRegex(@"\s*[-–]\s*")]
    private static partial Regex DashRegex();

    static readonly char[] Separators = [' ', '\t', ',', ';', '\r', '\n'];

    /// <summary>Разбирает список номеров; <paramref name="error"/> — текст для пользователя.</summary>
    /// <param name="allowEmpty">Пустая строка — пустой список без ошибки.</param>
    public static bool TryParse(string? text, out SortedSet<int> ids, out string? error, bool allowEmpty = false)
    {
        ids = [];
        error = null;
        string normalized = DashRegex().Replace(text ?? "", "-");
        foreach (string part in normalized.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            int dash = part.IndexOf('-', 1);
            if (dash > 0)
            {
                if (!TryNumber(part[..dash], out int from) || !TryNumber(part[(dash + 1)..], out int to))
                    return Fail(ids, out error, string.Format(Loc.S("ElemRangeErrToken"), part));
                if (from > to)
                    return Fail(ids, out error, string.Format(Loc.S("ElemRangeErrReversed"), part));
                if (to - from >= MaxRangeLength)
                    return Fail(ids, out error, string.Format(Loc.S("ElemRangeErrTooLong"), part,
                        MaxRangeLength.ToString("N0", CultureInfo.CurrentCulture)));
                for (int i = from; i <= to; i++) ids.Add(i);
            }
            else if (TryNumber(part, out int one))
                ids.Add(one);
            else
                return Fail(ids, out error, string.Format(Loc.S("ElemRangeErrToken"), part));
        }
        if (ids.Count == 0 && !allowEmpty)
            return Fail(ids, out error, Loc.S("ElemRangeErrEmpty"));
        return true;
    }

    static bool TryNumber(string s, out int value) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;

    static bool Fail(SortedSet<int> ids, out string? error, string message)
    {
        ids.Clear();
        error = message;
        return false;
    }

    /// <summary>
    /// Запись в стиле ЛИРЫ «101-103 106» (её можно вставить в ЛИРУ и обратно). Нечисловые теги дописываются в конец
    /// как есть.
    /// </summary>
    public static string FormatTags(IEnumerable<string> tags)
    {
        var numbers = new SortedSet<int>();
        var other = new List<string>();
        foreach (var tag in tags)
            if (int.TryParse(tag, NumberStyles.None, CultureInfo.InvariantCulture, out int n)) numbers.Add(n);
            else other.Add(tag);
        var parts = Ranges(numbers).Select(r => r.From == r.To ? Num(r.From) : $"{Num(r.From)}-{Num(r.To)}").ToList();
        parts.AddRange(other);
        return string.Join(" ", parts);
    }

    /// <summary>Запись в стиле ЛИРЫ для номеров.</summary>
    public static string Format(IEnumerable<int> ids) =>
        FormatTags(ids.Select(Num));

    static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

    /// <summary>Сплошные отрезки отсортированных номеров.</summary>
    public static IEnumerable<(int From, int To)> Ranges(IEnumerable<int> ids)
    {
        int? from = null, to = null;
        foreach (int n in ids.Distinct().Order())
        {
            if (to == n - 1) { to = n; continue; }
            if (from != null) yield return (from.Value, to!.Value);
            from = to = n;
        }
        if (from != null) yield return (from.Value, to!.Value);
    }
}
