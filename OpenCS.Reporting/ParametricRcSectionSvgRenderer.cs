using System.Globalization;
using System.Text;
using CScore.ParametricRc;

namespace OpenCS.Reporting;

/// <summary>Portable SVG-рендерер схемы параметрического ЖБ-сечения.</summary>
public sealed class ParametricRcSectionSvgRenderer
{
    const double CanvasWidth = 600;
    const double CanvasHeight = 500;
    const double OriginX = 300;
    const double OriginY = 270;
    const double MaxDrawingWidth = 330;
    const double MaxDrawingHeight = 330;

    /// <summary>Строит схему без зависимости от WPF.</summary>
    public string Render(ParametricRcSectionDefinition definition,
        ReportSectionDiagramOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        options ??= new ReportSectionDiagramOptions();
        double width = Math.Max(0.001, definition.WidthM);
        double height = Math.Max(0.001, definition.HeightM);
        double scale = Math.Min(MaxDrawingWidth / width, MaxDrawingHeight / height);
        CurrentScale = scale;
        var svg = new StringBuilder();
        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(CanvasWidth)}\" height=\"{N(CanvasHeight)}\" viewBox=\"0 0 {N(CanvasWidth)} {N(CanvasHeight)}\">");
        svg.Append("<style>.outline{fill:#eaf2f9;stroke:#17324d;stroke-width:2}.inner-contour{fill:white;stroke:#17324d;stroke-width:2}.rebar{fill:#b42318;stroke:#7f1d1d;stroke-width:1}.axis{stroke:#64748b;stroke-width:1;stroke-dasharray:5 4}.dimension{fill:#334155;font:12px Segoe UI,Arial}.annotation{fill:#075985;font:12px Segoe UI,Arial}.stirrup-cut{stroke:#c2410c;stroke-width:2}</style>");
        svg.Append("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
        AppendContour(svg, definition, scale);
        AppendAxes(svg, options, scale);
        AppendRebar(svg, definition, scale);
        if (options.ShowStirrupCuts)
            AppendStirrupCuts(svg, definition, scale);
        AppendDimensions(svg, definition, options, scale);
        svg.Append("</svg>");
        return svg.ToString();
    }

    void AppendContour(StringBuilder svg, ParametricRcSectionDefinition definition, double scale)
    {
        switch (definition.Shape)
        {
            case ParametricRcShape.Circle:
                svg.Append($"<circle class=\"outline\" cx=\"{N(X(0))}\" cy=\"{N(Y(0))}\" r=\"{N(definition.WidthM * scale / 2)}\"/>");
                break;
            case ParametricRcShape.Annulus:
                svg.Append($"<circle class=\"outline\" cx=\"{N(X(0))}\" cy=\"{N(Y(0))}\" r=\"{N(definition.WidthM * scale / 2)}\"/>");
                svg.Append($"<circle class=\"inner-contour\" cx=\"{N(X(0))}\" cy=\"{N(Y(0))}\" r=\"{N(definition.InnerDiameterM * scale / 2)}\"/>");
                break;
            case ParametricRcShape.Tee:
                svg.Append($"<path class=\"outline\" d=\"{TeePath(definition, scale)}\"/>");
                break;
            case ParametricRcShape.IBeam:
                svg.Append($"<path class=\"outline\" d=\"{IBeamPath(definition, scale)}\"/>");
                break;
            default:
                svg.Append($"<rect class=\"outline\" x=\"{N(X(-definition.WidthM / 2))}\" y=\"{N(Y(definition.HeightM / 2))}\" width=\"{N(definition.WidthM * scale)}\" height=\"{N(definition.HeightM * scale)}\"/>");
                break;
        }
    }

    void AppendAxes(StringBuilder svg, ReportSectionDiagramOptions options, double scale)
    {
        svg.Append($"<line class=\"axis\" x1=\"{N(X(-0.65 * 0.5))}\" y1=\"{N(Y(0))}\" x2=\"{N(X(0.65 * 0.5))}\" y2=\"{N(Y(0))}\"/>");
        svg.Append($"<line class=\"axis\" x1=\"{N(X(0))}\" y1=\"{N(Y(-0.65 * 0.5))}\" x2=\"{N(X(0))}\" y2=\"{N(Y(0.65 * 0.5))}\"/>");
        svg.Append($"<text class=\"dimension\" x=\"{N(X(0.33))}\" y=\"{N(Y(0) - 5)}\">+X</text>");
        svg.Append($"<text class=\"dimension\" x=\"{N(X(0) + 5)}\" y=\"{N(Y(0.33))}\">+Y</text>");
        string side = options.TensionSide switch
        {
            ReportTensionSide.Positive => options.Axis == "My" ? "+X" : "+Y",
            ReportTensionSide.Negative => options.Axis == "My" ? "−X" : "−Y",
            ReportTensionSide.NotApplicable => "не применяется",
            _ => "не определена"
        };
        svg.Append($"<text class=\"annotation\" x=\"20\" y=\"25\">сторона растяжения: {Escape(side)}</text>");
        if (!string.IsNullOrWhiteSpace(options.Axis))
            svg.Append($"<text class=\"annotation\" x=\"20\" y=\"43\">плоскость: {Escape(options.Axis)}</text>");
        _ = scale;
    }

    void AppendRebar(StringBuilder svg, ParametricRcSectionDefinition definition, double scale)
    {
        AppendLayer(svg, definition, definition.LowerRebar, scale);
        AppendLayer(svg, definition, definition.UpperRebar, scale);
        if (definition.PolarRebar is not { } polar || polar.Count <= 0)
            return;
        for (int i = 0; i < polar.Count; i++)
        {
            double angle = 2.0 * Math.PI * i / polar.Count;
            double cx = polar.RadiusM * Math.Cos(angle);
            double cy = polar.RadiusM * Math.Sin(angle);
            svg.Append($"<circle class=\"rebar\" cx=\"{N(X(cx))}\" cy=\"{N(Y(cy))}\" r=\"{N(Math.Max(2, polar.DiameterM * scale / 2))}\"/>");
        }
    }

    void AppendLayer(StringBuilder svg, ParametricRcSectionDefinition definition,
        ParametricLongitudinalLayer? layer, double scale)
    {
        if (layer is not { Enabled: true }) return;
        string r = N(Math.Max(2, layer.DiameterM * scale / 2));
        if (layer.IsIdealized)
        {
            // Расчётный слой — одна точка на оси симметрии; для оси My координата — абсцисса.
            bool normalToX = layer.Axis == CScore.IdealizedRebarAxis.My;
            double cx = normalToX ? layer.CoordinateM : 0.0;
            double cy = normalToX ? 0.0 : layer.CoordinateM;
            svg.Append($"<circle class=\"rebar\" cx=\"{N(X(cx))}\" cy=\"{N(Y(cy))}\" r=\"{r}\"/>");
            return;
        }
        // Те же абсциссы, что у генератора сечения, — схема отчёта совпадает с расчётным сечением.
        foreach (double cx in ParametricRcSectionGenerator.GetPhysicalBarPositionsX(definition, layer))
            svg.Append($"<circle class=\"rebar\" cx=\"{N(X(cx))}\" cy=\"{N(Y(layer.CoordinateM))}\" r=\"{r}\"/>");
    }

    void AppendStirrupCuts(StringBuilder svg, ParametricRcSectionDefinition definition, double scale)
    {
        foreach (var cut in definition.StirrupCuts)
        {
            int count = Math.Max(1, cut.Count);
            for (int i = 0; i < count; i++)
            {
                double offset = count == 1 ? 0.0 : (double)i / (count - 1) - 0.5;
                if (cut.Direction == ParametricStirrupDirection.Vertical)
                {
                    double x = offset * Math.Max(0.001, definition.WebThicknessMOrWidth());
                    svg.Append($"<line class=\"stirrup-cut\" x1=\"{N(X(x))}\" y1=\"{N(Y(-definition.HeightM / 2))}\" x2=\"{N(X(x))}\" y2=\"{N(Y(definition.HeightM / 2))}\"/>");
                }
                else
                {
                    double y = offset * Math.Max(0.001, definition.HeightM * 0.8);
                    svg.Append($"<line class=\"stirrup-cut\" x1=\"{N(X(-definition.WidthM / 2))}\" y1=\"{N(Y(y))}\" x2=\"{N(X(definition.WidthM / 2))}\" y2=\"{N(Y(y))}\"/>");
                }
            }
        }
    }

    void AppendDimensions(StringBuilder svg, ParametricRcSectionDefinition definition,
        ReportSectionDiagramOptions options, double scale)
    {
        svg.Append($"<text class=\"dimension\" x=\"20\" y=\"465\">b = {Mm(definition.WidthM)} мм</text>");
        svg.Append($"<text class=\"dimension\" x=\"20\" y=\"482\">h = {Mm(definition.HeightM)} мм</text>");
        if (definition.InnerDiameterM > 0)
            svg.Append($"<text class=\"dimension\" x=\"180\" y=\"465\">dᵢ = {Mm(definition.InnerDiameterM)} мм</text>");
        if (options.A is double a)
            svg.Append($"<text class=\"annotation\" x=\"400\" y=\"420\">a = {Mm(a)}</text>");
        if (options.APrime is double aPrime)
            svg.Append($"<text class=\"annotation\" x=\"400\" y=\"438\">a′ = {Mm(aPrime)}</text>");
        if (options.H0 is double h0)
            svg.Append($"<text class=\"annotation\" x=\"400\" y=\"456\">h₀ = {Mm(h0)}</text>");
        _ = scale;
    }

    string TeePath(ParametricRcSectionDefinition definition, double scale)
    {
        double w = definition.WidthM / 2, web = definition.WebThicknessM / 2;
        double y0 = -definition.HeightM / 2, y1 = definition.HeightM / 2;
        double yf = y1 - definition.FlangeThicknessM;
        return Path([(-w, y1), (w, y1), (w, yf), (web, yf), (web, y0), (-web, y0), (-web, yf), (-w, yf)], scale);
    }

    string IBeamPath(ParametricRcSectionDefinition definition, double scale)
    {
        double w = definition.WidthM / 2, web = definition.WebThicknessM / 2;
        double y0 = -definition.HeightM / 2, y1 = definition.HeightM / 2;
        double yf0 = y0 + definition.FlangeThicknessM, yf1 = y1 - definition.FlangeThicknessM;
        return Path([(-w, y1), (w, y1), (w, yf1), (web, yf1), (web, yf0), (w, yf0), (w, y0), (-w, y0), (-w, yf0), (-web, yf0), (-web, yf1), (-w, yf1)], scale);
    }

    string Path(IReadOnlyList<(double X, double Y)> points, double scale)
    {
        var result = new StringBuilder();
        for (int i = 0; i < points.Count; i++)
            result.Append(i == 0 ? "M" : "L").Append(N(X(points[i].X))).Append(' ').Append(N(Y(points[i].Y))).Append(' ');
        return result.Append("Z").ToString();
    }

    double X(double x) => OriginX + x * CurrentScale;
    double Y(double y) => OriginY - y * CurrentScale;
    double CurrentScale { get; set; }

    static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    string Mm(double value) => ReportNumberFormatter.Format(value * 1000.0,
        ReportUnit.Millimeter);

    static string Escape(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

static class ParametricDefinitionExtensions
{
    public static double WebThicknessMOrWidth(this ParametricRcSectionDefinition definition)
        => definition.WebThicknessM > 0 ? definition.WebThicknessM : definition.WidthM;
}
