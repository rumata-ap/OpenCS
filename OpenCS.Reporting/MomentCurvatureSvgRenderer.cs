using System.Globalization;
using System.Net;
using System.Text;

namespace OpenCS.Reporting;

/// <summary>SVG-график кривых «момент–кривизна» (несколько серий, отметки точек, легенда).</summary>
public static class MomentCurvatureSvgRenderer
{
    const double Width = 900;
    const double Height = 520;
    const double PaddingLeft = 72;
    const double PaddingRight = 32;
    const double PaddingTop = 52;
    const double PaddingBottom = 60;

    /// <summary>Рисует серии ломаными, отметки — кружками с подписью.</summary>
    /// <param name="series">Серии: имя (легенда), цвет SVG и точки (X, Y).</param>
    /// <param name="marks">Отмеченные точки с подписью.</param>
    public static string Render(
        IReadOnlyList<(string Name, string Color, IReadOnlyList<(double X, double Y)> Points)> series,
        IReadOnlyList<(string Label, double X, double Y)> marks,
        string title, string xTitle, string yTitle)
    {
        var all = series.SelectMany(s => s.Points).Concat(marks.Select(m => (m.X, m.Y)))
            .Where(p => double.IsFinite(p.Item1) && double.IsFinite(p.Item2)).ToList();
        if (all.Count == 0) all = [(0, 0), (1, 1)];

        double minX = Math.Min(all.Min(p => p.Item1), 0), maxX = Math.Max(all.Max(p => p.Item1), 0);
        double minY = Math.Min(all.Min(p => p.Item2), 0), maxY = Math.Max(all.Max(p => p.Item2), 0);
        (minX, maxX) = Range(minX, maxX, 1e-3);
        (minY, maxY) = Range(minY, maxY, 1.0);
        double padY = (maxY - minY) * 0.05;
        maxY += padY;
        if (minY < 0) minY -= padY;

        double plotWidth = Width - PaddingLeft - PaddingRight;
        double plotHeight = Height - PaddingTop - PaddingBottom;
        (double X, double Y) Map(double x, double y) =>
            (PaddingLeft + (x - minX) / (maxX - minX) * plotWidth,
             Height - PaddingBottom - (y - minY) / (maxY - minY) * plotHeight);

        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"900\" height=\"520\" viewBox=\"0 0 900 520\" role=\"img\">");
        svg.Append("<title>").Append(E(title)).AppendLine("</title><rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
        DrawTicks(svg, Map, minX, maxX, minY, maxY);
        var origin = Map(0, 0);
        svg.Append("<line x1=\"").Append(F(PaddingLeft)).Append("\" y1=\"").Append(F(origin.Y))
            .Append("\" x2=\"").Append(F(Width - PaddingRight)).Append("\" y2=\"").Append(F(origin.Y))
            .AppendLine("\" stroke=\"#94a3b8\" stroke-width=\"1\"/>");
        svg.Append("<line x1=\"").Append(F(origin.X)).Append("\" y1=\"").Append(F(PaddingTop))
            .Append("\" x2=\"").Append(F(origin.X)).Append("\" y2=\"").Append(F(Height - PaddingBottom))
            .AppendLine("\" stroke=\"#94a3b8\" stroke-width=\"1\"/>");

        foreach (var s in series)
        {
            if (s.Points.Count == 0) continue;
            svg.Append("<polyline fill=\"none\" stroke=\"").Append(E(s.Color)).Append("\" stroke-width=\"2.2\" points=\"")
                .Append(string.Join(" ", s.Points.Select(p => { var q = Map(p.X, p.Y); return F(q.X) + "," + F(q.Y); })))
                .AppendLine("\"/>");
        }

        svg.Append("<g font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"11\" fill=\"#0f172a\">");
        foreach (var m in marks)
        {
            if (!double.IsFinite(m.X) || !double.IsFinite(m.Y)) continue;
            var q = Map(m.X, m.Y);
            svg.Append("<circle cx=\"").Append(F(q.X)).Append("\" cy=\"").Append(F(q.Y))
                .Append("\" r=\"4\" fill=\"white\" stroke=\"#0f172a\" stroke-width=\"1.5\"/>");
            svg.Append("<text x=\"").Append(F(q.X + 6)).Append("\" y=\"").Append(F(q.Y - 6)).Append("\">")
                .Append(E(m.Label)).AppendLine("</text>");
        }
        svg.AppendLine("</g>");

        svg.Append("<g font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"12\" fill=\"#334155\">")
            .Append("<text x=\"18\" y=\"26\" font-size=\"16\" font-weight=\"600\">").Append(E(title)).AppendLine("</text>")
            .Append("<text data-axis-title=\"x\" x=\"").Append(F(PaddingLeft + plotWidth / 2))
            .Append("\" y=\"").Append(F(Height - 10)).Append("\" text-anchor=\"middle\">").Append(E(xTitle)).AppendLine("</text>")
            .Append("<text data-axis-title=\"y\" x=\"18\" y=\"").Append(F(PaddingTop + plotHeight / 2))
            .Append("\" text-anchor=\"middle\" transform=\"rotate(-90 18 ").Append(F(PaddingTop + plotHeight / 2))
            .Append(")\">").Append(E(yTitle)).AppendLine("</text>");
        double ly = 28;
        foreach (var s in series)
        {
            svg.Append("<line x1=\"700\" y1=\"").Append(F(ly)).Append("\" x2=\"725\" y2=\"").Append(F(ly))
                .Append("\" stroke=\"").Append(E(s.Color)).Append("\" stroke-width=\"2.2\"/>")
                .Append("<text x=\"732\" y=\"").Append(F(ly + 4)).Append("\">").Append(E(s.Name)).AppendLine("</text>");
            ly += 20;
        }
        svg.AppendLine("</g></svg>");
        return svg.ToString();
    }

    static void DrawTicks(StringBuilder svg, Func<double, double, (double X, double Y)> map,
        double minX, double maxX, double minY, double maxY)
    {
        double xStep = NiceStep((maxX - minX) / 5.0);
        double yStep = NiceStep((maxY - minY) / 5.0);
        double xBottom = map(0, minY).Y, xTop = map(0, maxY).Y;
        double yLeft = map(minX, 0).X, yRight = map(maxX, 0).X;

        svg.Append("<g data-axis=\"x\" font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"10\" fill=\"#475569\">");
        for (double x = FloorToStep(minX, xStep); x <= maxX + xStep * 0.5; x += xStep)
        {
            if (x < minX - xStep * 0.01) continue;
            var point = map(x, minY);
            svg.Append("<line x1=\"").Append(F(point.X)).Append("\" y1=\"").Append(F(xTop))
                .Append("\" x2=\"").Append(F(point.X)).Append("\" y2=\"").Append(F(xBottom))
                .AppendLine("\" stroke=\"#e2e8f0\" stroke-width=\"0.8\"/>");
            svg.Append("<text data-tick-label=\"x\" x=\"").Append(F(point.X)).Append("\" y=\"")
                .Append(F(xBottom + 16)).Append("\" text-anchor=\"middle\">")
                .Append(FormatTick(x, xStep)).AppendLine("</text>");
        }
        svg.AppendLine("</g>");

        svg.Append("<g data-axis=\"y\" font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"10\" fill=\"#475569\">");
        for (double y = FloorToStep(minY, yStep); y <= maxY + yStep * 0.5; y += yStep)
        {
            if (y < minY - yStep * 0.01) continue;
            var point = map(minX, y);
            svg.Append("<line x1=\"").Append(F(yLeft)).Append("\" y1=\"").Append(F(point.Y))
                .Append("\" x2=\"").Append(F(yRight)).Append("\" y2=\"").Append(F(point.Y))
                .AppendLine("\" stroke=\"#e2e8f0\" stroke-width=\"0.8\"/>");
            svg.Append("<text data-tick-label=\"y\" x=\"").Append(F(yLeft - 7)).Append("\" y=\"")
                .Append(F(point.Y + 3)).Append("\" text-anchor=\"end\">")
                .Append(FormatTick(y, yStep)).AppendLine("</text>");
        }
        svg.AppendLine("</g>");
    }

    static (double Min, double Max) Range(double min, double max, double fallback)
    {
        if (max - min >= 1e-15) return (min, max);
        double half = Math.Max(fallback, Math.Abs(min) + Math.Abs(max) + fallback) / 2.0;
        return (-half, half);
    }

    static double NiceStep(double raw)
    {
        if (!double.IsFinite(raw) || raw <= 1e-15) return 1;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double normalized = raw / magnitude;
        double nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return nice * magnitude;
    }

    static double FloorToStep(double value, double step) => Math.Floor(value / step + 1e-10) * step;

    static string FormatTick(double value, double step)
    {
        if (Math.Abs(value) < step * 1e-8) value = 0;
        return value.ToString("G4", CultureInfo.InvariantCulture);
    }

    static string F(double value) => value.ToString("G8", CultureInfo.InvariantCulture);
    static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
}
