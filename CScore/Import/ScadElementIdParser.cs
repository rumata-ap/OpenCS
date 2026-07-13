namespace CScore.Import;

/// <summary>Разбор списка номеров элементов SCAD: "12, 15-20".</summary>
public static class ScadElementIdParser
{
    public static bool TryParse(string text, out HashSet<int> ids, out string? error)
    {
        ids = new HashSet<int>();
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Пустой список элементов.";
            return false;
        }

        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Contains('-', StringComparison.Ordinal))
            {
                var ab = part.Split('-', StringSplitOptions.TrimEntries);
                if (ab.Length != 2
                    || !int.TryParse(ab[0], out int a)
                    || !int.TryParse(ab[1], out int b)
                    || a <= 0 || b <= 0 || a > b)
                {
                    error = $"Некорректный диапазон: '{part}'.";
                    ids.Clear();
                    return false;
                }
                for (int i = a; i <= b; i++)
                    ids.Add(i);
            }
            else
            {
                if (!int.TryParse(part, out int n) || n <= 0)
                {
                    error = $"Некорректный номер: '{part}'.";
                    ids.Clear();
                    return false;
                }
                ids.Add(n);
            }
        }

        if (ids.Count == 0)
        {
            error = "Пустой список элементов.";
            return false;
        }
        return true;
    }
}
