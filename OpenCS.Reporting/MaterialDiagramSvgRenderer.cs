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
            .Append("<text x=\"").Append(F(Width - 145)).Append("\" y=\"").Append(F(origin.Y - 8)).AppendLine("\">ε</text>")
            .Append("<text x=\"").Append(F(origin.X + 8)).Append("\" y=\"28\">σ, МПа</text>")
            .Append("<text x=\"").Append(F(PaddingLeft + 8)).Append("\" y=\"").Append(F(Height - 22)).AppendLine("\">ε — безразмерная</text>")
            .Append("<line x1=\"720\" y1=\"28\" x2=\"745\" y2=\"28\" stroke=\"#2563eb\" stroke-width=\"2.2\"/>")
            .Append("<text x=\"752\" y=\"32\">сжатие</text>")
            .Append("<line x1=\"720\" y1=\"48\" x2=\"745\" y2=\"48\" stroke=\"#dc2626\" stroke-width=\"2.2\"/>")
            .AppendLine("<text x=\"752\" y=\"52\">растяжение</text></g></svg>");
        return svg.ToString();
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

    static string F(double value) => value.ToString("G8", CultureInfo.InvariantCulture);
    static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
}
