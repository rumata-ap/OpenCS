using CScore;
using OpenCS.Services;

namespace OpenCS.Utilites;

/// <summary>CSV пула кругов: «Tag;X;Y;Radius», координаты и радиус в метрах.</summary>
public static class CircleCsv
{
    public const string Header = "Tag;X;Y;Radius";

    /// <summary>
    /// Разбирает строки CSV (первая строка-заголовок пропускается). Разделитель полей — «;» или табуляция; десятичный —
    /// «,» или «.» (<see cref="NumberInput"/>). Строки с нечислами и кругами, не прошедшими
    /// <see cref="ContourAuthoring.ValidateCircle"/>, пропускаются и считаются в <paramref name="skipped"/>.
    /// </summary>
    public static List<CircleP> Parse(IEnumerable<string> lines, out int skipped)
    {
        var list = new List<CircleP>();
        skipped = 0;
        bool first = true;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split(line.Contains(';') ? ';' : '\t');
            if (parts.Length >= 4 && NumberInput.TryParse(parts[1], out double x) && NumberInput.TryParse(parts[2], out double y)
                && NumberInput.TryParse(parts[3], out double r))
            {
                var c = new CircleP(x, y, r) { Tag = parts[0].Trim() };
                if (ContourAuthoring.ValidateCircle(c).Count == 0) list.Add(c); else skipped++;
            }
            else if (!first) skipped++;
            first = false;
        }
        return list;
    }

    /// <summary>Строки CSV (десятичная точка) — обратное к <see cref="Parse"/>.</summary>
    public static List<string> Format(IEnumerable<CircleP> circles)
    {
        var lines = new List<string> { Header };
        lines.AddRange(circles.Select(c => FormattableString.Invariant($"{c.Tag};{c.X:R};{c.Y:R};{c.Radius:R}")));
        return lines;
    }
}
