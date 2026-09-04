using System.Globalization;
using System.Text;

namespace OpenCS.Reporting;

/// <summary>Единая политика определения и нормализации размеров SVG-иллюстраций отчёта.
/// Используется всеми рендерерами вместо приватной логики каждого из них.</summary>
public static class SvgSizing
{
    /// <summary>Максимальная ширина картинки в px: помещается в рабочую ширину A4 с запасом.</summary>
    public const double MaxWidth = 620;

    /// <summary>Размер иллюстрации в px.</summary>
    public readonly record struct Size(double Width, double Height);

    /// <summary>Определяет размер SVG. Валидные <c>width</c>+<c>height</c> корневого тега
    /// побеждают независимо от <c>viewBox</c>; иначе используется <c>viewBox</c>.
    /// Поддерживаются только голое число и суффикс <c>px</c>.</summary>
    /// <exception cref="FormatException">Ни один источник не даёт валидного положительного размера.</exception>
    public static Size Resolve(string svg)
    {
        ArgumentNullException.ThrowIfNull(svg);
        string opening = OpeningTag(svg);

        double? width = ParseDimension(Attribute(opening, "width"));
        double? height = ParseDimension(Attribute(opening, "height"));
        if (width is > 0 && height is > 0)
            return new Size(width.Value, height.Value);

        string? viewBox = Attribute(opening, "viewBox") ?? Attribute(opening, "viewbox");
        var parts = viewBox?.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts is { Length: 4 } &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double vw) && vw > 0 &&
            double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double vh) && vh > 0)
            return new Size(vw, vh);

        throw new FormatException(
            "Не удалось определить размеры SVG: нет валидных width/height и нет валидного viewBox.");
    }

    /// <summary>Записывает в корневой тег вычисленные <c>width</c>/<c>height</c> в px,
    /// заменяя (а не дублируя) существующие атрибуты.</summary>
    public static string EnsureExplicitDimensions(string svg)
    {
        var size = Resolve(svg);
        int start = svg.IndexOf("<svg", StringComparison.OrdinalIgnoreCase);
        int end = svg.IndexOf('>', start);
        string opening = svg[start..end];

        string cleaned = RemoveAttribute(RemoveAttribute(opening, "width"), "height").TrimEnd();
        bool selfClosing = cleaned.EndsWith('/');
        if (selfClosing) cleaned = cleaned[..^1].TrimEnd();

        var rebuilt = new StringBuilder(cleaned)
            .Append(" width=\"").Append(Px(size.Width)).Append('"')
            .Append(" height=\"").Append(Px(size.Height)).Append('"');
        if (selfClosing) rebuilt.Append(" /");

        return svg[..start] + rebuilt + svg[end..];
    }

    static string OpeningTag(string svg)
    {
        int start = svg.IndexOf("<svg", StringComparison.OrdinalIgnoreCase);
        int end = start >= 0 ? svg.IndexOf('>', start) : -1;
        if (start < 0 || end < 0)
            throw new FormatException("Строка не содержит корневого тега <svg>.");
        return svg[start..end];
    }

    // Ищет атрибут именно как отдельное имя: перед ним обязателен пробельный символ,
    // иначе "width" совпал бы с хвостом "stroke-width".
    static string? Attribute(string opening, string name)
    {
        foreach (var (valueStart, valueEnd) in AttributeSpans(opening, name))
            return opening[valueStart..valueEnd];
        return null;
    }

    static string RemoveAttribute(string opening, string name)
    {
        foreach (var (valueStart, valueEnd) in AttributeSpans(opening, name))
        {
            int cut = valueStart - 1;                       // открывающая кавычка
            while (cut > 0 && opening[cut] != ' ' && opening[cut] != '\t' &&
                   opening[cut] != '\r' && opening[cut] != '\n') cut--;
            return opening[..cut] + opening[(valueEnd + 1)..];
        }
        return opening;
    }

    // Возвращает не более одного диапазона значения атрибута (первое вхождение).
    static IEnumerable<(int Start, int End)> AttributeSpans(string opening, string name)
    {
        int i = 0;
        while (i < opening.Length)
        {
            int found = opening.IndexOf(name, i, StringComparison.OrdinalIgnoreCase);
            if (found < 0) yield break;

            bool boundedLeft = found > 0 && char.IsWhiteSpace(opening[found - 1]);
            int eq = found + name.Length;
            while (eq < opening.Length && char.IsWhiteSpace(opening[eq])) eq++;

            if (boundedLeft && eq < opening.Length && opening[eq] == '=')
            {
                int v = eq + 1;
                while (v < opening.Length && char.IsWhiteSpace(opening[v])) v++;
                if (v < opening.Length && opening[v] is '"' or '\'')
                {
                    int close = opening.IndexOf(opening[v], v + 1);
                    if (close > v) yield return (v + 1, close);
                }
                yield break;
            }
            i = found + 1;
        }
    }

    static double? ParseDimension(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string trimmed = value.Trim();
        if (trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^2];
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
               && double.IsFinite(result) && result > 0
            ? result
            : null;
    }

    static string Px(double value)
        => value.ToString("G8", CultureInfo.InvariantCulture) + "px";
}
