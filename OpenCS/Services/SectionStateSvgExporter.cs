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

    // Асимметричные поля: слева/сверху/снизу — под оси и заголовок, справа — под
    // боковую панель (цветовая шкала + выноски экстремумов), см. DrawSidebar.
    const double PaddingLeft = 68;
    const double PaddingRight = 198;
    const double PaddingTop = 50;
    const double PaddingBottom = 62;

    const double SidebarX0 = Width - PaddingRight;

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

        double availW = Width - PaddingLeft - PaddingRight;
        double availH = Height - PaddingTop - PaddingBottom;
        double scale = Math.Min(availW / spanX, availH / spanY);
        double usedWidth = spanX * scale, usedHeight = spanY * scale;
        double leftMargin = PaddingLeft + (availW - usedWidth) / 2.0;
        double bottomMargin = PaddingBottom + (availH - usedHeight) / 2.0;
        Point Map(Point p) => new(leftMargin + (p.X - minX) * scale,
                                   Height - bottomMargin - (p.Y - minY) * scale);

        // Раздельные шкалы: диапазон напряжений/деформаций в арматуре обычно на порядок
        // шире, чем в бетоне/областях, — общая шкала гасила бы контраст карты бетона.
        double concreteMaxAbs = SymmetricScale(plot.ConcreteMin, plot.ConcreteMax);
        double rebarMaxAbs = SymmetricScale(plot.RebarMin, plot.RebarMax);

        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ")
           .Append(F(Width)).Append(' ').Append(F(Height)).Append("\" role=\"img\">");
        svg.Append("<title>").Append(E(title)).AppendLine("</title>");
        svg.AppendLine(BuildDefs(concreteMaxAbs, rebarMaxAbs));
        svg.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
        svg.Append("<rect x=\"").Append(F(SidebarX0)).Append("\" y=\"0\" width=\"")
           .Append(F(PaddingRight)).Append("\" height=\"").Append(F(Height))
           .AppendLine("\" fill=\"#f8fafc\"/>");
        DrawAxes(svg, Map, minX, maxX, minY, maxY);

        foreach (var fiber in plot.ConcreteFibers)
        {
            if (fiber.Vertices.Count < 3) continue;
            string pointsText = string.Join(" ", fiber.Vertices.Select(p => P(Map(p))));
            svg.Append("<polygon points=\"").Append(pointsText).Append("\" fill=\"")
               .Append(Color(fiber.Value, concreteMaxAbs, invert: true)).Append("\" fill-opacity=\".88\" stroke=\"#ffffff\" stroke-width=\".7\">");
            svg.Append("<title>").Append(E(fiber.Tooltip)).AppendLine("</title></polygon>");
        }

        foreach (var area in plot.NoMeshAreas)
        {
            DrawPolyline(svg, area.Hull, Map, "#3b5268", 1.6);
            foreach (var hole in area.Holes)
                DrawPolyline(svg, hole, Map, "#6b7280", 1.2);
        }

        int maxTensionIndex = -1;
        double maxTensionEps = double.MinValue;
        for (int i = 0; i < plot.RebarFibers.Count; i++)
            if (plot.RebarFibers[i].Eps > maxTensionEps)
            {
                maxTensionEps = plot.RebarFibers[i].Eps;
                maxTensionIndex = i;
            }

        for (int i = 0; i < plot.RebarFibers.Count; i++)
        {
            var rebar = plot.RebarFibers[i];
            var mapped = Map(rebar.Center);
            double radius = F2(rebar.RadiusMm * scale);
            svg.Append("<circle cx=\"").Append(F(mapped.X)).Append("\" cy=\"")
               .Append(F(mapped.Y)).Append("\" r=\"").Append(F(radius))
               .Append("\" fill=\"").Append(Color(rebar.Value, rebarMaxAbs))
               .Append("\" stroke=\"#5b1b1b\" stroke-width=\"1\">");
            svg.Append("<title>").Append(E(rebar.Tooltip)).AppendLine("</title></circle>");

            // Номер стержня — совпадает с колонкой «№» таблицы арматуры отчёта
            // (тот же порядок обхода section.EnumerateAreas в StrainStateReportProvider.RebarRows).
            svg.Append("<text x=\"").Append(F(mapped.X)).Append("\" y=\"")
               .Append(F(mapped.Y - radius - 4))
               .Append("\" text-anchor=\"middle\" font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"9\" font-weight=\"600\" fill=\"#0f172a\" paint-order=\"stroke\" stroke=\"white\" stroke-width=\"2.4\">")
               .Append(i + 1).AppendLine("</text>");

            if (i == maxTensionIndex)
                svg.Append("<circle cx=\"").Append(F(mapped.X)).Append("\" cy=\"").Append(F(mapped.Y))
                   .Append("\" r=\"").Append(F(radius + 4))
                   .AppendLine("\" fill=\"none\" stroke=\"#0f172a\" stroke-width=\"2\"/>");
        }

        if (plot.NeutralAxis is { Count: >= 2 } neutralAxis)
        {
            Point first = Map(neutralAxis[0]), last = Map(neutralAxis[^1]);
            svg.Append("<line x1=\"").Append(F(first.X)).Append("\" y1=\"").Append(F(first.Y))
               .Append("\" x2=\"").Append(F(last.X)).Append("\" y2=\"").Append(F(last.Y))
               .AppendLine("\" stroke=\"#334155\" stroke-width=\"2\" stroke-dasharray=\"8 5\"/>");
            var mid = new Point((first.X + last.X) / 2.0, (first.Y + last.Y) / 2.0);
            svg.Append("<text x=\"").Append(F(mid.X + 6)).Append("\" y=\"").Append(F(mid.Y - 6))
               .AppendLine("\" font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"11\" fill=\"#334155\" paint-order=\"stroke\" stroke=\"white\" stroke-width=\"3\">нейтральная ось</text>");
        }

        if (plot.MaxComprData is { } maxCompr)
            DrawExtremeMarker(svg, Map(maxCompr.Pt), "#0f172a");
        if (maxTensionIndex >= 0)
            DrawExtremeMarker(svg, Map(plot.RebarFibers[maxTensionIndex].Center), "#0f172a");

        DrawSidebar(svg, plot, concreteMaxAbs, rebarMaxAbs, maxTensionIndex);

        svg.Append("<g font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"16\" font-weight=\"600\" fill=\"#1f2937\">")
           .Append("<text x=\"18\" y=\"28\">").Append(E(title)).AppendLine("</text></g>");

        svg.AppendLine("</svg>");
        return svg.ToString();
    }

    static double SymmetricScale(double min, double max)
    {
        double m = Math.Max(Math.Abs(min), Math.Abs(max));
        return double.IsFinite(m) && m > 1e-15 ? m : 1.0;
    }

    static string BuildDefs(double concreteMaxAbs, double rebarMaxAbs)
    {
        var sb = new StringBuilder();
        sb.Append("<defs>");
        AppendGradient(sb, "ndsLegendGradientConcrete", concreteMaxAbs, invert: true);
        AppendGradient(sb, "ndsLegendGradientRebar", rebarMaxAbs, invert: false);
        sb.Append("</defs>");
        return sb.ToString();
    }

    static void AppendGradient(StringBuilder sb, string id, double maxAbs, bool invert)
    {
        const int steps = 10;
        sb.Append("<linearGradient id=\"").Append(id).Append("\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\">");
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            double value = maxAbs - t * 2 * maxAbs; // сверху (+maxAbs) вниз (−maxAbs)
            sb.Append("<stop offset=\"").Append(F(t * 100)).Append("%\" stop-color=\"")
              .Append(Color(value, maxAbs, invert)).Append("\"/>");
        }
        sb.Append("</linearGradient>");
    }

    /// <summary>Рисует контрастный маркер экстремума на самой карте (выноска ведёт к боковой панели).</summary>
    static void DrawExtremeMarker(StringBuilder svg, Point mapped, string stroke)
    {
        svg.Append("<circle cx=\"").Append(F(mapped.X)).Append("\" cy=\"").Append(F(mapped.Y))
           .Append("\" r=\"7\" fill=\"none\" stroke=\"").Append(stroke)
           .AppendLine("\" stroke-width=\"2.4\"/>");
        svg.Append("<line x1=\"").Append(F(mapped.X - 10)).Append("\" y1=\"").Append(F(mapped.Y))
           .Append("\" x2=\"").Append(F(mapped.X + 10)).Append("\" y2=\"").Append(F(mapped.Y))
           .Append("\" stroke=\"").Append(stroke).AppendLine("\" stroke-width=\"1.4\"/>");
        svg.Append("<line x1=\"").Append(F(mapped.X)).Append("\" y1=\"").Append(F(mapped.Y - 10))
           .Append("\" x2=\"").Append(F(mapped.X)).Append("\" y2=\"").Append(F(mapped.Y + 10))
           .Append("\" stroke=\"").Append(stroke).AppendLine("\" stroke-width=\"1.4\"/>");
    }

    /// <summary>Боковая панель: две независимые градиентные шкалы (области/бетон и арматура —
    /// у арматуры диапазон обычно на порядок шире) и текстовые выноски по экстремумам —
    /// вся числовая информация должна быть видна на самом изображении, т.к. в PDF/DOCX
    /// всплывающие подсказки &lt;title&gt; недоступны.</summary>
    static void DrawSidebar(StringBuilder svg, SectionPlotVM plot, double concreteMaxAbs, double rebarMaxAbs,
        int maxTensionIndex)
    {
        string unit = plot.Mode == SectionPlotMode.Stress ? "σ, МПа" : "ε";
        double colX = SidebarX0 + 18;

        double y = DrawLegendBlock(svg, colX, 36, "Основной материал, " + unit, "ndsLegendGradientConcrete", concreteMaxAbs);
        if (plot.RebarFibers.Count > 0)
            y = DrawLegendBlock(svg, colX, y + 24, "Арматура, " + unit, "ndsLegendGradientRebar", rebarMaxAbs);

        double calloutY = y + 30;
        if (plot.MaxComprData is { } maxCompr)
        {
            DrawCallout(svg, colX, calloutY, "max в осн. материале",
            [
                $"x = {G4(maxCompr.Pt.X)} мм",
                $"y = {G4(maxCompr.Pt.Y)} мм",
                $"ε = {SignedG4(maxCompr.Eps)}",
                $"σ = {SignedG4(maxCompr.SigMpa)} МПа"
            ]);
            calloutY += 108;
        }

        if (maxTensionIndex >= 0)
        {
            var rebar = plot.RebarFibers[maxTensionIndex];
            DrawCallout(svg, colX, calloutY, "max в арматуре",
            [
                $"стержень: №{maxTensionIndex + 1}",
                $"группа: {rebar.Group}",
                $"x = {G4(rebar.Center.X)} мм",
                $"y = {G4(rebar.Center.Y)} мм",
                $"ε = {SignedG4(rebar.Eps)}",
                $"σ = {SignedG4(rebar.Sigma)} МПа"
            ]);
        }
    }

    /// <summary>Рисует один блок «заголовок + градиентная полоса + 5 делений» и возвращает Y конца блока.</summary>
    static double DrawLegendBlock(StringBuilder svg, double colX, double titleY, string header,
        string gradientId, double maxAbs)
    {
        const double barWidth = 20, barHeight = 110;
        double barY = titleY + 18;

        svg.Append("<g font-family=\"Segoe UI,Arial,sans-serif\" fill=\"#334155\">");
        svg.Append("<text x=\"").Append(F(colX)).Append("\" y=\"").Append(F(titleY))
           .Append("\" font-size=\"11\" font-weight=\"600\">").Append(E(header)).AppendLine("</text>");

        svg.Append("<rect x=\"").Append(F(colX)).Append("\" y=\"").Append(F(barY))
           .Append("\" width=\"").Append(F(barWidth)).Append("\" height=\"").Append(F(barHeight))
           .Append("\" fill=\"url(#").Append(gradientId)
           .AppendLine(")\" stroke=\"#94a3b8\" stroke-width=\"1\"/>");

        for (int i = 0; i <= 4; i++)
        {
            double t = i / 4.0;
            double value = maxAbs - t * 2 * maxAbs;
            double tickY = barY + t * barHeight;
            svg.Append("<line x1=\"").Append(F(colX + barWidth)).Append("\" y1=\"").Append(F(tickY))
               .Append("\" x2=\"").Append(F(colX + barWidth + 5)).Append("\" y2=\"").Append(F(tickY))
               .AppendLine("\" stroke=\"#334155\" stroke-width=\"1\"/>");
            svg.Append("<text x=\"").Append(F(colX + barWidth + 8)).Append("\" y=\"").Append(F(tickY + 4))
               .Append("\" font-size=\"11.5\">").Append(SignedG4(value)).AppendLine("</text>");
        }
        svg.AppendLine("</g>");
        return barY + barHeight;
    }

    static void DrawCallout(StringBuilder svg, double x, double y, string header, string[] lines)
    {
        svg.Append("<g font-family=\"Segoe UI,Arial,sans-serif\" fill=\"#1f2937\">");
        svg.Append("<text x=\"").Append(F(x)).Append("\" y=\"").Append(F(y))
           .Append("\" font-size=\"12.5\" font-weight=\"600\">").Append(E(header)).AppendLine("</text>");
        for (int i = 0; i < lines.Length; i++)
            svg.Append("<text x=\"").Append(F(x)).Append("\" y=\"").Append(F(y + 18 + i * 16))
               .Append("\" font-size=\"12\">").Append(E(lines[i])).AppendLine("</text>");
        svg.AppendLine("</g>");
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
        double xStep = NiceStep((maxX - minX) / 5.0);
        double yStep = NiceStep((maxY - minY) / 5.0);
        Point bottomLeft = map(new Point(minX, minY));
        Point bottomRight = map(new Point(maxX, minY));
        Point topLeft = map(new Point(minX, maxY));

        svg.Append("<g data-axis=\"x\" font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"12\" fill=\"#475569\">");
        for (double x = FloorToStep(minX, xStep); x <= maxX + xStep * 0.5; x += xStep)
        {
            if (x < minX - xStep * 0.01) continue;
            Point point = map(new Point(x, minY));
            svg.Append("<line x1=\"").Append(F(point.X)).Append("\" y1=\"").Append(F(topLeft.Y))
                .Append("\" x2=\"").Append(F(point.X)).Append("\" y2=\"").Append(F(bottomLeft.Y))
                .AppendLine("\" stroke=\"#e2e8f0\" stroke-width=\"0.8\"/>");
            svg.Append("<text data-tick-label=\"x\" x=\"").Append(F(point.X)).Append("\" y=\"")
                .Append(F(bottomLeft.Y + 18)).Append("\" text-anchor=\"middle\">")
                .Append(FormatTick(x, xStep)).AppendLine("</text>");
        }
        svg.AppendLine("</g>");

        svg.Append("<g data-axis=\"y\" font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"12\" fill=\"#475569\">");
        for (double y = FloorToStep(minY, yStep); y <= maxY + yStep * 0.5; y += yStep)
        {
            if (y < minY - yStep * 0.01) continue;
            Point point = map(new Point(minX, y));
            svg.Append("<line x1=\"").Append(F(bottomLeft.X)).Append("\" y1=\"").Append(F(point.Y))
                .Append("\" x2=\"").Append(F(bottomRight.X)).Append("\" y2=\"").Append(F(point.Y))
                .AppendLine("\" stroke=\"#e2e8f0\" stroke-width=\"0.8\"/>");
            svg.Append("<text data-tick-label=\"y\" x=\"").Append(F(bottomLeft.X - 8)).Append("\" y=\"")
                .Append(F(point.Y + 4)).Append("\" text-anchor=\"end\">")
                .Append(FormatTick(y, yStep)).AppendLine("</text>");
        }
        svg.AppendLine("</g>");

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

        double centerY = (topLeft.Y + bottomLeft.Y) / 2.0;
        svg.Append("<g font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"14\" fill=\"#334155\">")
            .Append("<text data-axis-title=\"x\" x=\"").Append(F((bottomLeft.X + bottomRight.X) / 2.0))
            .Append("\" y=\"").Append(F(bottomLeft.Y + 38)).AppendLine("\" text-anchor=\"middle\">x, мм</text>")
            .Append("<text data-axis-title=\"y\" x=\"18\" y=\"").Append(F(centerY))
            .Append("\" text-anchor=\"middle\" transform=\"rotate(-90 18 ").Append(F(centerY))
            .AppendLine(")\">y, мм</text></g>");
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

    static string Color(double value, double maxAbs, bool invert = false)
    {
        if (!double.IsFinite(value)) return "#cbd5e1";
        double t = Math.Clamp(Math.Abs(value) / maxAbs, 0.0, 1.0);
        bool negative = value < 0 != invert;
        (int r, int g, int b) target = negative ? (37, 99, 190) : (205, 57, 57);
        int r0 = (int)Math.Round(255 + (target.r - 255) * t);
        int g0 = (int)Math.Round(255 + (target.g - 255) * t);
        int b0 = (int)Math.Round(255 + (target.b - 255) * t);
        return $"#{r0:X2}{g0:X2}{b0:X2}";
    }

    static string P(Point p) => F(p.X) + "," + F(p.Y);
    static string F(double value) => value.ToString("G8", CultureInfo.InvariantCulture);
    static double F2(double value) => Math.Max(4.0, value);
    static string G4(double value) => value.ToString("G4", CultureInfo.InvariantCulture);
    static string SignedG4(double value)
    {
        string formatted = G4(value);
        return formatted == "0" || value == 0 ? "0" : (value > 0 ? "+" : "") + formatted;
    }
    static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
}
