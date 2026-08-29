using CScore;
using OpenCS.Converters;
using OpenCS.Views;
using System.Windows.Media;

namespace OpenCS.Views.Helpers;

/// <summary>Данные геометрии поперечного сечения для read-only графического preview.</summary>
public sealed record CrossSectionPlotData(
    IReadOnlyList<PlotElement> Elements,
    double XMin,
    double XMax,
    double YMin,
    double YMax);

/// <summary>Строит элементы и границы графического preview поперечного сечения.</summary>
public static class CrossSectionPlotBuilder
{
    /// <summary>Строит preview обычного или двухстадийного поперечного сечения.</summary>
    public static CrossSectionPlotData Build(CrossSection section)
    {
        IEnumerable<MaterialArea> areas = section is TwoStageSection twoStage
            ? twoStage.Stage1.Areas.Concat(twoStage.Areas)
            : section.Areas;

        var elements = new List<PlotElement>();
        foreach (var area in areas)
            AddAreaElements(elements, area);

        var bounds = FindBounds(elements);
        return new CrossSectionPlotData(
            elements,
            bounds.XMin,
            bounds.XMax,
            bounds.YMin,
            bounds.YMax);
    }

    static void AddAreaElements(List<PlotElement> elements, MaterialArea area)
    {
        var hull = area.Hull;
        var brush = MatTypeToBrushConverter.GetBrush(area.Material?.Type ?? MatType.None);
        var fill = new SolidColorBrush(Color.FromArgb(120, brush.Color.R, brush.Color.G, brush.Color.B));
        if (hull != null && hull.X.Count > 0)
            elements.Add(new PolygonElement
            {
                Xs = [.. hull.X],
                Ys = [.. hull.Y],
                Fill = fill,
                Stroke = brush,
                StrokeThickness = 1.5
            });

        var meshFibers = area.Fibers
            .Where(f => f.TypeFiber is FiberType.poly or FiberType.tri)
            .ToArray();
        if (meshFibers.Length > 0)
            elements.Add(new FiberMeshElement { Fibers = meshFibers, ShowCentroids = false, Fill = null });

        foreach (var hole in area.Holes)
            if (hole.X.Count > 0)
                elements.Add(new PolygonElement
                {
                    Xs = [.. hole.X],
                    Ys = [.. hole.Y],
                    Fill = Brushes.White,
                    Stroke = Brushes.Gray,
                    StrokeThickness = 1
                });

        AddRebarElements(elements, area);
        AddStirrupElements(elements, area);
    }

    /// <summary>Добавляет точечную арматуру с цветом материала и признаком преднапряжения.</summary>
    internal static void AddRebarElements(List<PlotElement> elements, MaterialArea area)
    {
        var materialBrush = MatTypeToBrushConverter.GetBrush(area.Material?.Type ?? MatType.None);
        bool hasPrestress = double.IsFinite(area.SigSp) && Math.Abs(area.SigSp) > 1e-12;

        foreach (var fiber in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
        {
            if (hasPrestress)
                elements.Add(new CircleElement
                {
                    X = fiber.X,
                    Y = fiber.Y,
                    Radius = fiber.Diameter / 2 * 1.25,
                    Fill = null,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                });

            elements.Add(new CircleElement
            {
                X = fiber.X,
                Y = fiber.Y,
                Radius = fiber.Diameter / 2,
                Fill = materialBrush,
                Stroke = Brushes.DarkRed,
                StrokeThickness = 0.5
            });
        }
    }

    /// <summary>Добавляет центровые линии хомутов и открытых срезов.</summary>
    internal static void AddStirrupElements(List<PlotElement> elements, MaterialArea area)
    {
        foreach (var group in area.Stirrups)
        {
            foreach (var stirrup in group.Elements)
            {
                var contour = stirrup.CenterlineContour;
                int count = Math.Min(contour.X.Count, contour.Y.Count);
                if (count < 2) continue;

                elements.Add(new ScatterElement
                {
                    Xs = contour.X.Take(count).ToArray(),
                    Ys = contour.Y.Take(count).ToArray(),
                    Stroke = Brushes.DarkRed,
                    StrokeThickness = 2
                });
            }
        }
    }

    static (double XMin, double XMax, double YMin, double YMax) FindBounds(
        IReadOnlyList<PlotElement> elements)
    {
        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;
        bool any = false;

        void Consider(double x, double y)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y)) return;
            any = true;
            xMin = Math.Min(xMin, x);
            xMax = Math.Max(xMax, x);
            yMin = Math.Min(yMin, y);
            yMax = Math.Max(yMax, y);
        }

        foreach (var element in elements)
        {
            switch (element)
            {
                case PolygonElement polygon:
                    for (int i = 0; i < Math.Min(polygon.Xs.Length, polygon.Ys.Length); i++)
                        Consider(polygon.Xs[i], polygon.Ys[i]);
                    break;

                case CircleElement circle:
                    Consider(circle.X - circle.Radius, circle.Y - circle.Radius);
                    Consider(circle.X + circle.Radius, circle.Y + circle.Radius);
                    break;

                case FiberMeshElement mesh:
                    foreach (var fiber in mesh.Fibers)
                    {
                        Consider(fiber.X, fiber.Y);
                        if (string.IsNullOrWhiteSpace(fiber.WKT)) continue;
                        try
                        {
                            WktHelper.ParseWKTPolygon(fiber.WKT, out var xs, out var ys,
                                out var holeXs, out var holeYs);
                            for (int i = 0; i < Math.Min(xs.Count, ys.Count); i++)
                                Consider(xs[i], ys[i]);
                            foreach (var hole in holeXs)
                                for (int i = 0; i < Math.Min(hole.Count, holeYs[holeXs.IndexOf(hole)].Count); i++)
                                    Consider(hole[i], holeYs[holeXs.IndexOf(hole)][i]);
                        }
                        catch (Exception) { }
                    }
                    break;

                case ScatterElement scatter:
                    for (int i = 0; i < Math.Min(scatter.Xs.Length, scatter.Ys.Length); i++)
                        Consider(scatter.Xs[i], scatter.Ys[i]);
                    break;

                case LineSegmentsElement segments:
                    for (int i = 0; i < Math.Min(segments.Xs.Length, segments.Ys.Length); i++)
                        Consider(segments.Xs[i], segments.Ys[i]);
                    break;

                case MarkerElement marker:
                    for (int i = 0; i < Math.Min(marker.Xs.Length, marker.Ys.Length); i++)
                        Consider(marker.Xs[i], marker.Ys[i]);
                    break;

                case TextElement text:
                    Consider(text.X, text.Y);
                    break;
            }
        }

        if (!any)
            return (-0.1, 0.1, -0.1, 0.1);
        if (xMax - xMin < 1e-9) { xMin -= 0.1; xMax += 0.1; }
        if (yMax - yMin < 1e-9) { yMin -= 0.1; yMax += 0.1; }
        return (xMin, xMax, yMin, yMax);
    }
}
