using System.Text;

namespace OpenCS.Reporting;

/// <summary>Вид сегмента формулы.</summary>
public enum FormulaSegmentKind
{
    /// <summary>Обычный текст.</summary>
    Plain,
    /// <summary>Подстрочный индекс.</summary>
    Subscript,
    /// <summary>Надстрочная степень.</summary>
    Superscript
}

/// <summary>Сегмент формулы: текст и способ его набора.</summary>
public readonly record struct FormulaSegment(string Text, FormulaSegmentKind Kind);

/// <summary>Разбор закрытого inline-контракта формул отчёта. Допускаются ровно два тега —
/// <c>&lt;sub&gt;</c> и <c>&lt;sup&gt;</c>, без вложенности и обязательно закрытые.
/// Единый источник правды для всех четырёх рендереров.</summary>
public static class FormulaMarkup
{
    /// <summary>Разбирает строку на сегменты.</summary>
    /// <exception cref="FormatException">Найден иной тег, незакрытый тег или вложенность.</exception>
    public static IReadOnlyList<FormulaSegment> Parse(string? text)
    {
        string source = text ?? "";
        var segments = new List<FormulaSegment>();
        var plain = new StringBuilder();
        int i = 0;

        void FlushPlain()
        {
            if (plain.Length == 0) return;
            segments.Add(new FormulaSegment(plain.ToString(), FormulaSegmentKind.Plain));
            plain.Clear();
        }

        while (i < source.Length)
        {
            // '<' начинает тег, только если следом идёт имя или '/'. Иначе это обычный
            // знак сравнения формулы («1 < 2», «ε < εult») — он экранируется как текст,
            // а не считается ошибкой разметки.
            if (source[i] != '<' || !StartsTag(source, i)) { plain.Append(source[i++]); continue; }

            FormulaSegmentKind kind;
            string close;
            if (StartsWithAt(source, i, "<sub>")) { kind = FormulaSegmentKind.Subscript; close = "</sub>"; }
            else if (StartsWithAt(source, i, "<sup>")) { kind = FormulaSegmentKind.Superscript; close = "</sup>"; }
            else throw new FormatException(
                $"Недопустимый тег в формуле в позиции {i}: '{Preview(source, i)}'. " +
                "Разрешены только <sub> и <sup>.");

            string open = kind == FormulaSegmentKind.Subscript ? "<sub>" : "<sup>";
            int contentStart = i + open.Length;
            int end = source.IndexOf(close, contentStart, StringComparison.Ordinal);
            if (end < 0)
                throw new FormatException($"Незакрытый тег {open} в формуле.");

            string content = source[contentStart..end];
            if (content.Contains('<'))
                throw new FormatException("Вложенные теги в формуле не допускаются.");

            FlushPlain();
            if (content.Length > 0) segments.Add(new FormulaSegment(content, kind));
            i = end + close.Length;
        }

        FlushPlain();
        return segments;
    }

    static bool StartsTag(string source, int index)
    {
        int next = index + 1;
        if (next >= source.Length) return false;
        return source[next] == '/' || char.IsLetter(source[next]);
    }

    static bool StartsWithAt(string source, int index, string token)
        => index + token.Length <= source.Length &&
           string.CompareOrdinal(source, index, token, 0, token.Length) == 0;

    static string Preview(string source, int index)
        => source[index..Math.Min(source.Length, index + 12)];
}
