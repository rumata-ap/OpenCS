using System.Globalization;
using System.Net;
using System.Text;
using CScore;
using CSmath;

namespace OpenCS.Reporting;

/// <summary>Строит SVG-график фактической диаграммы σ(ε) из расчётного объекта Diagramm.</summary>
public sealed class MaterialDiagramSvgRenderer
{
    const double Width = 900;
    const double Height = 520;
    const double PaddingLeft = 72;
    const double PaddingRight = 32;
    const double PaddingTop = 52;
    const double PaddingBottom = 60;

    /// <summary>Рисует сжатие и растяжение одной диаграммы с осями и единицами.</summary>
    public string Render(Diagramm diagram, string title)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        var compression = Sample(diagram.Ic);
        var tension = Sample(diagram.It);
        var all = compression.Concat(tension).Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)).ToList();
        if (all.Count == 0) all = [(0, 0), (-0.001, 0), (0.001, 0)];

        double minX = Math.Min(all.Min(p => p.X), 0);
        double maxX = Math.Max(all.Max(p => p.X), 0);
        double minY = Math.Min(all.Min(p => p.Y), 0);
        double maxY = Math.Max(all.Max(p => p.Y), 0);
        (minX, maxX) = Range(minX, maxX, 0.001);
        (minY, maxY) = Range(minY, maxY, 1.0);

        double plotWidth = Width - PaddingLeft - PaddingRight;
        double plotHeight = Height - PaddingTop - PaddingBottom;
        (double X, double Y) Map(double x, double y) =>
            (PaddingLeft + (x - minX) / (maxX - minX) * plotWidth,
             Height - PaddingBottom - (y - minY) / (maxY - minY) * plotHeight);

        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"900\" height=\"520\" viewBox=\"0 0 900 520\" role=\"img\">");
        svg.Append("<title>").Append(E(title)).AppendLine("</title><rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
        var origin = Map(0, 0);
        DrawTicks(svg, Map, minX, maxX, minY, maxY);
        svg.Append("<line x1=\"").Append(F(PaddingLeft)).Append("\" y1=\"").Append(F(origin.Y))
            .Append("\" x2=\"").Append(F(Width - PaddingRight)).Append("\" y2=\"").Append(F(origin.Y))
            .AppendLine("\" stroke=\"#94a3b8\" stroke-width=\"1\"/>");
        svg.Append("<line x1=\"").Append(F(origin.X)).Append("\" y1=\"").Append(F(PaddingTop))
            .Append("\" x2=\"").Append(F(origin.X)).Append("\" y2=\"").Append(F(Height - PaddingBottom))
            .AppendLine("\" stroke=\"#94a3b8\" stroke-width=\"1\"/>");
        svg.Append("<polyline fill=\"none\" stroke=\"#2563eb\" stroke-width=\"2.2\" points=\"")
            .Append(Path(compression, Map)).AppendLine("\"/>");
        svg.Append("<polyline fill=\"none\" stroke=\"#dc2626\" stroke-width=\"2.2\" points=\"")
            .Append(Path(tension, Map)).AppendLine("\"/>");
        svg.Append("<g font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"12\" fill=\"#334155\">")
            .Append("<text x=\"18\" y=\"26\" font-size=\"16\" font-weight=\"600\">")
            .Append(E(title)).AppendLine("</text>")
            .Append("<text data-axis-title=\"x\" x=\"").Append(F(PaddingLeft + plotWidth / 2))
            .Append("\" y=\"").Append(F(Height - 10)).AppendLine("\" text-anchor=\"middle\">ε, безразмерная</text>")
            .Append("<text data-axis-title=\"y\" x=\"18\" y=\"").Append(F(PaddingTop + plotHeight / 2))
            .Append("\" text-anchor=\"middle\" transform=\"rotate(-90 18 ").Append(F(PaddingTop + plotHeight / 2))
            .AppendLine(")\">σ, МПа</text>")
            .Append("<line x1=\"720\" y1=\"28\" x2=\"745\" y2=\"28\" stroke=\"#2563eb\" stroke-width=\"2.2\"/>")
            .Append("<text x=\"752\" y=\"32\">сжатие</text>")
            .Append("<line x1=\"720\" y1=\"48\" x2=\"745\" y2=\"48\" stroke=\"#dc2626\" stroke-width=\"2.2\"/>")
            .AppendLine("<text x=\"752\" y=\"52\">растяжение</text></g></svg>");
        return svg.ToString();
    }

    static void DrawTicks(StringBuilder svg, Func<double, double, (double X, double Y)> map,
        double minX, double maxX, double minY, double maxY)
    {
        double xStep = NiceStep((maxX - minX) / 5.0);
        double yStep = NiceStep((maxY - minY) / 5.0);
        double xBottom = map(0, minY).Y;
        double xTop = map(0, maxY).Y;
        double yLeft = map(minX, 0).X;
        double yRight = map(maxX, 0).X;

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

    static List<(double X, double Y)> Sample(ISpline spline)
    {
        if (spline?.X is not { Length: > 0 } xs)
            return [];
        var result = new List<(double X, double Y)>();
        for (int i = 0; i < xs.Length - 1; i++)
        {
            double a = xs[i], b = xs[i + 1];
            if (!double.IsFinite(a) || !double.IsFinite(b) || b <= a) continue;
            const int count = 10;
            for (int j = i == 0 ? 0 : 1; j <= count; j++)
            {
                double x = a + (b - a) * j / count;
                double y = spline.Interpolate(x) / 1000.0;
                if (double.IsFinite(y)) result.Add((x, y));
            }
        }
        if (result.Count == 0)
            result.AddRange(xs.Select(x => (x, spline.Interpolate(x) / 1000.0)));
        return result;
    }

    static string Path(IEnumerable<(double X, double Y)> values,
        Func<double, double, (double X, double Y)> map)
        => string.Join(" ", values.Select(p =>
        {
            var point = map(p.X, p.Y);
            return F(point.X) + "," + F(point.Y);
        }));

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
