using System.Globalization;

namespace CScore.Fem;

/// <summary>Список тегов из строки вида «1-50, 75 80; 101–103»: числовые диапазоны раскрываются, прочие теги — как есть.</summary>
public static class FemTagList
{
    /// <summary>Наибольшая длина одного диапазона — защита от опечатки «1-1000000000».</summary>
    public const int MaxRange = 1_000_000;

    /// <summary>Теги без повторов в порядке появления; <paramref name="error"/> — первая ошибка разбора или null.</summary>
    public static IReadOnlyList<string> Parse(string? text, out string? error)
    {
        error = null;
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text)) return result;
        foreach (var raw in text.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Replace('–', '-').Replace('—', '-');
            int dash = part.IndexOf('-', 1);
            if (dash > 0 && int.TryParse(part[..dash], NumberStyles.Integer, CultureInfo.InvariantCulture, out int a) &&
                int.TryParse(part[(dash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int b))
            {
                if (b < a || b - a >= MaxRange) { error ??= $"Неверный диапазон «{raw}»."; continue; }
                for (int n = a; n <= b; n++)
                {
                    string tag = n.ToString(CultureInfo.InvariantCulture);
                    if (seen.Add(tag)) result.Add(tag);
                }
                continue;
            }
            if (seen.Add(part)) result.Add(part);
        }
        return result;
    }
}
