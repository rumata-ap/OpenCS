using System.Globalization;
using System.Net;
using System.Text;
using System.Windows;
using OpenCS.ViewModels;

namespace OpenCS.Services;

/// <summary>Строит автономную SVG-карту состояния сечения из данных SectionPlotVM.</summary>
public sealed class SectionStateSvgExporter
{
    const double Width = 900;
    const double Height = 650;
    const double Padding = 58;

    /// <summary>Экспортирует карту деформаций или напряжений в SVG.</summary>
    public string Render(SectionPlotVM plot, string title = "")
    {
        ArgumentNullException.ThrowIfNull(plot);
        var points = AllGeometryPoints(plot).ToList();
        if (points.Count == 0)
            points = [new Point(-1, -1), new Point(1, 1)];

        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        double spanX = Math.Max(maxX - minX, 1e-6);
        double spanY = Math.Max(maxY - minY, 1e-6);
        double scale = Math.Min((Width - 2 * Padding) / spanX, (Height - 2 * Padding) / spanY);
        double usedWidth = spanX * scale, usedHeight = spanY * scale;
        double offsetX = (Width - usedWidth) / 2.0;
        double offsetY = (Height - usedHeight) / 2.0;
        Point Map(Point p) => new(offsetX + (p.X - minX) * scale,
                                   Height - offsetY - (p.Y - minY) * scale);

        double maxAbs = AllValues(plot).Select(Math.Abs).DefaultIfEmpty(1.0).Max();
        if (!double.IsFinite(maxAbs) || maxAbs < 1e-15) maxAbs = 1.0;

        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ")
           .Append(F(Width)).Append(' ').Append(F(Height)).Append("\" role=\"img\">");
        svg.Append("<title>").Append(E(title)).AppendLine("</title>");
        svg.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");

        foreach (var fiber in plot.ConcreteFibers)
        {
            if (fiber.Vertices.Count < 3) continue;
            string pointsText = string.Join(" ", fiber.Vertices.Select(p => P(Map(p))));
            svg.Append("<polygon points=\"").Append(pointsText).Append("\" fill=\"")
               .Append(Color(fiber.Value, maxAbs)).Append("\" fill-opacity=\".88\" stroke=\"#ffffff\" stroke-width=\".7\">");
            svg.Append("<title>").Append(E(fiber.Tooltip)).AppendLine("</title></polygon>");
        }

        foreach (var area in plot.NoMeshAreas)
        {
            DrawPolyline(svg, area.Hull, Map, "#3b5268", 1.6);
            foreach (var hole in area.Holes)
                DrawPolyline(svg, hole, Map, "#6b7280", 1.2);
        }

        foreach (var rebar in plot.RebarFibers)
        {
            svg.Append("<circle cx=\"").Append(F(Map(rebar.Center).X)).Append("\" cy=\"")
               .Append(F(Map(rebar.Center).Y)).Append("\" r=\"").Append(F(rebar.RadiusMm * scale))
               .Append("\" fill=\"").Append(Color(rebar.Value, maxAbs))
               .Append("\" stroke=\"#5b1b1b\" stroke-width=\"1\">");
            svg.Append("<title>").Append(E(rebar.Tooltip)).AppendLine("</title></circle>");
        }

        if (plot.NeutralAxis is { Count: >= 2 } neutralAxis)
        {
            Point first = Map(neutralAxis[0]), last = Map(neutralAxis[^1]);
            svg.Append("<line x1=\"").Append(F(first.X)).Append("\" y1=\"").Append(F(first.Y))
               .Append("\" x2=\"").Append(F(last.X)).Append("\" y2=\"").Append(F(last.Y))
               .AppendLine("\" stroke=\"#334155\" stroke-width=\"2\" stroke-dasharray=\"8 5\"/>");
        }

        DrawAxes(svg, Map, minX, maxX, minY, maxY);
        DrawLegend(svg, plot, maxAbs, title);
        svg.AppendLine("</svg>");
        return svg.ToString();
    }

    static IEnumerable<Point> AllGeometryPoints(SectionPlotVM plot)
    {
        foreach (var fiber in plot.ConcreteFibers)
            foreach (var point in fiber.Vertices)
                yield return point;
        foreach (var area in plot.NoMeshAreas)
        {
            foreach (var point in area.Hull) yield return point;
            foreach (var hole in area.Holes)
                foreach (var point in hole) yield return point;
        }
        foreach (var rebar in plot.RebarFibers) yield return rebar.Center;
    }

    static IEnumerable<double> AllValues(SectionPlotVM plot)
    {
        foreach (var fiber in plot.ConcreteFibers) yield return fiber.Value;
        foreach (var rebar in plot.RebarFibers) yield return rebar.Value;
    }

    static void DrawPolyline(StringBuilder svg, IReadOnlyList<Point> points,
        Func<Point, Point> map, string stroke, double width)
    {
        if (points.Count < 2) return;
        svg.Append("<polyline points=\"")
           .Append(string.Join(" ", points.Select(point => P(map(point)))))
           .Append("\" fill=\"none\" stroke=\"").Append(stroke)
           .Append("\" stroke-width=\"").Append(F(width)).AppendLine("\"/>");
    }

    static void DrawAxes(StringBuilder svg, Func<Point, Point> map,
        double minX, double maxX, double minY, double maxY)
    {
        if (minY <= 0 && maxY >= 0)
        {
            Point left = map(new Point(minX, 0)), right = map(new Point(maxX, 0));
            svg.Append("<line x1=\"").Append(F(left.X)).Append("\" y1=\"").Append(F(left.Y))
               .Append("\" x2=\"").Append(F(right.X)).Append("\" y2=\"").Append(F(right.Y))
               .AppendLine("\" stroke=\"#94a3b8\" stroke-width=\"1\"/>");
        }
        if (minX <= 0 && maxX >= 0)
        {
            Point bottom = map(new Point(0, minY)), top = map(new Point(0, maxY));
            svg.Append("<line x1=\"").Append(F(bottom.X)).Append("\" y1=\"").Append(F(bottom.Y))
               .Append("\" x2=\"").Append(F(top.X)).Append("\" y2=\"").Append(F(top.Y))
               .AppendLine("\" stroke=\"#94a3b8\" stroke-width=\"1\"/>");
        }
    }

    static void DrawLegend(StringBuilder svg, SectionPlotVM plot, double maxAbs, string title)
    {
        const double x = Width - 190, y = 18;
        string unit = plot.Mode == SectionPlotMode.Stress ? "σ, MPa" : "ε";
        svg.Append("<g font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"12\" fill=\"#334155\">")
           .Append("<rect x=\"").Append(F(x - 10)).Append("\" y=\"").Append(F(y - 8))
           .Append("\" width=\"178\" height=\"58\" rx=\"6\" fill=\"#ffffff\" fill-opacity=\".92\" stroke=\"#d9e1ea\"/>")
           .Append("<text x=\"").Append(F(x)).Append("\" y=\"").Append(F(y + 10)).Append("\">")
           .Append(E(title)).AppendLine("</text>")
           .Append("<text x=\"").Append(F(x)).Append("\" y=\"").Append(F(y + 30)).Append("\">")
           .Append(E("−" + maxAbs.ToString("G5", CultureInfo.InvariantCulture) + " … 0 … +" + maxAbs.ToString("G5", CultureInfo.InvariantCulture) + " " + unit))
           .AppendLine("</text></g>");
    }

    static string Color(double value, double maxAbs)
    {
        if (!double.IsFinite(value)) return "#cbd5e1";
        double t = Math.Clamp(Math.Abs(value) / maxAbs, 0.0, 1.0);
        (int r, int g, int b) target = value < 0 ? (37, 99, 190) : (205, 57, 57);
        int r0 = (int)Math.Round(255 + (target.r - 255) * t);
        int g0 = (int)Math.Round(255 + (target.g - 255) * t);
        int b0 = (int)Math.Round(255 + (target.b - 255) * t);
        return $"#{r0:X2}{g0:X2}{b0:X2}";
    }

    static string P(Point p) => F(p.X) + "," + F(p.Y);
    static string F(double value) => value.ToString("G8", CultureInfo.InvariantCulture);
    static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
}
