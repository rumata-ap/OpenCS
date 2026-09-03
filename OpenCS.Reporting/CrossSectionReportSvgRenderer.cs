using System.Globalization;
using System.Net;
using System.Text;
using CScore;

namespace OpenCS.Reporting;

/// <summary>Строит независимый от WPF SVG-чертёж сечения для отчёта.</summary>
public sealed class CrossSectionReportSvgRenderer
{
    const double Width = 900;
    const double Height = 650;
    const double Padding = 70;

    /// <summary>Рисует внешние контуры, отверстия, части сечения и точечную арматуру.</summary>
    public string Render(CrossSection section, string title = "Геометрия сечения")
    {
        ArgumentNullException.ThrowIfNull(section);
        var areas = Areas(section).ToList();
        var points = GeometryPoints(areas).ToList();
        if (points.Count == 0)
            points = [(-0.1, -0.1), (0.1, 0.1)];

        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        double spanX = Math.Max(maxX - minX, 1e-9);
        double spanY = Math.Max(maxY - minY, 1e-9);
        double scale = Math.Min((Width - 2 * Padding) / spanX, (Height - 2 * Padding) / spanY);
        double usedWidth = spanX * scale;
        double usedHeight = spanY * scale;
        double offsetX = (Width - usedWidth) / 2.0;
        double offsetY = (Height - usedHeight) / 2.0;
        (double X, double Y) Map(double x, double y) =>
            (offsetX + (x - minX) * scale,
             Height - offsetY - (y - minY) * scale);

        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"900\" height=\"650\" viewBox=\"0 0 900 650\" role=\"img\">");
        svg.Append("<title>").Append(E(title)).AppendLine("</title>");
        svg.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
        DrawAxes(svg, Map, minX, maxX, minY, maxY);

        int areaIndex = 0;
        foreach (var area in areas)
        {
            string fill = Fill(area.Material?.Type ?? MatType.None);
            string stroke = Stroke(area.Material?.Type ?? MatType.None);
            string label = AreaLabel(area, ++areaIndex);

            if (area.Hull is { X.Count: > 2 } hull)
            {
                svg.Append("<polygon points=\"").Append(Points(hull.X, hull.Y, Map))
                    .Append("\" fill=\"").Append(fill)
                    .Append("\" fill-opacity=\".28\" stroke=\"").Append(stroke)
                    .AppendLine("\" stroke-width=\"2\"><title>" + E(label) + "</title></polygon>");

                var center = Center(hull);
                var mapped = Map(center.X, center.Y);
                svg.Append("<text x=\"").Append(F(mapped.X)).Append("\" y=\"")
                    .Append(F(mapped.Y)).AppendLine("\" text-anchor=\"middle\" font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"13\" fill=\"#1f2937\">" + E(area.Tag) + "</text>");
            }

            foreach (var hole in area.Holes)
            {
                if (hole.X.Count < 3) continue;
                svg.Append("<polygon points=\"").Append(Points(hole.X, hole.Y, Map))
                    .AppendLine("\" fill=\"white\" stroke=\"#475569\" stroke-width=\"1.5\" stroke-dasharray=\"5 3\"/>");
            }

            foreach (var fiber in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
            {
                var mapped = Map(fiber.X, fiber.Y);
                double radius = Math.Max(4.0, (fiber.Diameter > 0 ? fiber.Diameter / 2 : spanX * 0.008) * scale);
                svg.Append("<circle cx=\"").Append(F(mapped.X)).Append("\" cy=\"").Append(F(mapped.Y))
                    .Append("\" r=\"").Append(F(radius)).Append("\" fill=\"").Append(stroke)
                    .AppendLine("\" stroke=\"#5b1b1b\" stroke-width=\"1\"><title>" + E($"{label}; стержень №{fiber.Num}") + "</title></circle>");
            }
        }

        svg.Append("<g font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"12\" fill=\"#334155\">")
            .Append("<text x=\"18\" y=\"28\" font-size=\"16\" font-weight=\"600\">")
            .Append(E(title)).AppendLine("</text>")
            .Append("<text x=\"18\" y=\"48\">x, y — м; размеры стержней — мм</text>")
            .AppendLine("</g></svg>");
        return svg.ToString();
    }

    static IEnumerable<MaterialArea> Areas(CrossSection section)
    {
        if (section is TwoStageSection twoStage)
            return twoStage.Stage1.Areas.Concat(twoStage.Areas).Where(MaterialArea.IsCalcActive);
        return section.Areas.Where(MaterialArea.IsCalcActive);
    }

    static IEnumerable<(double X, double Y)> GeometryPoints(IEnumerable<MaterialArea> areas)
    {
        foreach (var area in areas)
        {
            if (area.Hull != null)
                for (int i = 0; i < Math.Min(area.Hull.X.Count, area.Hull.Y.Count); i++)
                    yield return (area.Hull.X[i], area.Hull.Y[i]);
            foreach (var hole in area.Holes)
                for (int i = 0; i < Math.Min(hole.X.Count, hole.Y.Count); i++)
                    yield return (hole.X[i], hole.Y[i]);
            foreach (var fiber in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
                yield return (fiber.X, fiber.Y);
        }
    }

    static string AreaLabel(MaterialArea area, int index)
        => $"Часть {index}: {area.Tag}; материал: {area.Material?.Tag ?? "не задан"}; категория: {area.Category}; контуров: {area.Contours.Count}";

    static string Points(IList<double> xs, IList<double> ys, Func<double, double, (double X, double Y)> map)
    {
        int count = Math.Min(xs.Count, ys.Count);
        return string.Join(" ", Enumerable.Range(0, count).Select(i =>
        {
            var p = map(xs[i], ys[i]);
            return F(p.X) + "," + F(p.Y);
        }));
    }

    static (double X, double Y) Center(Contour contour)
    {
        if (contour.X.Count == 0) return (0, 0);
        int count = contour.X.Count;
        return (contour.X.Take(count).Average(), contour.Y.Take(count).Average());
    }

    static void DrawAxes(StringBuilder svg, Func<double, double, (double X, double Y)> map,
        double minX, double maxX, double minY, double maxY)
    {
        double xStep = NiceStep((maxX - minX) / 5.0);
        double yStep = NiceStep((maxY - minY) / 5.0);
        var bottomLeft = map(minX, minY);
        var bottomRight = map(maxX, minY);
        var topLeft = map(minX, maxY);

        svg.Append("<g data-axis=\"x\" font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"10\" fill=\"#475569\">");
        for (double x = FloorToStep(minX, xStep); x <= maxX + xStep * 0.5; x += xStep)
        {
            if (x < minX - xStep * 0.01) continue;
            var point = map(x, minY);
            svg.Append("<line x1=\"").Append(F(point.X)).Append("\" y1=\"").Append(F(topLeft.Y))
                .Append("\" x2=\"").Append(F(point.X)).Append("\" y2=\"").Append(F(bottomLeft.Y))
                .AppendLine("\" stroke=\"#e2e8f0\" stroke-width=\"0.8\"/>");
            svg.Append("<text data-tick-label=\"x\" x=\"").Append(F(point.X)).Append("\" y=\"")
                .Append(F(bottomLeft.Y + 16)).Append("\" text-anchor=\"middle\">")
                .Append(FormatTick(x, xStep)).AppendLine("</text>");
        }
        svg.AppendLine("</g>");

        svg.Append("<g data-axis=\"y\" font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"10\" fill=\"#475569\">");
        for (double y = FloorToStep(minY, yStep); y <= maxY + yStep * 0.5; y += yStep)
        {
            if (y < minY - yStep * 0.01) continue;
            var point = map(minX, y);
            svg.Append("<line x1=\"").Append(F(bottomLeft.X)).Append("\" y1=\"").Append(F(point.Y))
                .Append("\" x2=\"").Append(F(bottomRight.X)).Append("\" y2=\"").Append(F(point.Y))
                .AppendLine("\" stroke=\"#e2e8f0\" stroke-width=\"0.8\"/>");
            svg.Append("<text data-tick-label=\"y\" x=\"").Append(F(bottomLeft.X - 7)).Append("\" y=\"")
                .Append(F(point.Y + 3)).Append("\" text-anchor=\"end\">")
                .Append(FormatTick(y, yStep)).AppendLine("</text>");
        }
        svg.AppendLine("</g>");

        if (minY <= 0 && maxY >= 0)
        {
            var a = map(minX, 0); var b = map(maxX, 0);
            svg.Append("<line x1=\"").Append(F(a.X)).Append("\" y1=\"").Append(F(a.Y))
                .Append("\" x2=\"").Append(F(b.X)).Append("\" y2=\"").Append(F(b.Y))
                .AppendLine("\" stroke=\"#94a3b8\" stroke-width=\"1\"/>");
        }
        if (minX <= 0 && maxX >= 0)
        {
            var a = map(0, minY); var b = map(0, maxY);
            svg.Append("<line x1=\"").Append(F(a.X)).Append("\" y1=\"").Append(F(a.Y))
                .Append("\" x2=\"").Append(F(b.X)).Append("\" y2=\"").Append(F(b.Y))
                .AppendLine("\" stroke=\"#94a3b8\" stroke-width=\"1\"/>");
        }

        double centerY = (topLeft.Y + bottomLeft.Y) / 2.0;
        svg.Append("<g font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"12\" fill=\"#334155\">")
            .Append("<text data-axis-title=\"x\" x=\"").Append(F((bottomLeft.X + bottomRight.X) / 2.0))
            .Append("\" y=\"").Append(F(bottomLeft.Y + 35)).AppendLine("\" text-anchor=\"middle\">x, м</text>")
            .Append("<text data-axis-title=\"y\" x=\"18\" y=\"").Append(F(centerY))
            .Append("\" text-anchor=\"middle\" transform=\"rotate(-90 18 ").Append(F(centerY))
            .AppendLine(")\">y, м</text></g>");
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

    static string Fill(MatType type) => type switch
    {
        MatType.Concrete => "#3b82f6",
        MatType.ReSteelF or MatType.ReSteelU => "#f97316",
        MatType.Steel => "#22c55e",
        _ => "#94a3b8"
    };

    static string Stroke(MatType type) => type switch
    {
        MatType.Concrete => "#1d4ed8",
        MatType.ReSteelF or MatType.ReSteelU => "#c2410c",
        MatType.Steel => "#15803d",
        _ => "#475569"
    };

    static string F(double value) => value.ToString("G8", CultureInfo.InvariantCulture);
    static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
}
