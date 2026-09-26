using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CScore;
using CScore.ParametricRc;
using OpenCS.ViewModels;
using OpenCS.Utilites;

namespace OpenCS.Views.Helpers;

/// <summary>Схематический живой preview параметрического железобетонного сечения.</summary>
public sealed class ParametricRcPreviewControl : FrameworkElement
{
    static readonly Brush ConcreteBrush = Freeze(new SolidColorBrush(Color.FromArgb(82, 154, 190, 207)));
    static readonly Brush RebarBrush = Freeze(new SolidColorBrush(Color.FromRgb(190, 28, 28)));
    static readonly Brush StirrupBrush = Freeze(new SolidColorBrush(Color.FromRgb(202, 124, 24)));
    static readonly Brush AxisBrush = Freeze(new SolidColorBrush(Color.FromArgb(100, 70, 90, 100)));
    static readonly Pen OutlinePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(78, 105, 116)), 1.8));
    static readonly Pen AxisPen = Freeze(new Pen(AxisBrush, 0.8) { DashStyle = DashStyles.Dash });

    public ParametricRcPreviewControl()
    {
        ClipToBounds = true;
        DataContextChanged += OnDataContextChanged;
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? 520 : availableSize.Width,
        double.IsInfinity(availableSize.Height) ? 520 : availableSize.Height);

    void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is INotifyPropertyChanged newVm)
            newVm.PropertyChanged += OnViewModelPropertyChanged;
        InvalidateVisual();
    }

    void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(SystemColors.WindowBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (DataContext is not ParametricRcSectionVM vm || ActualWidth < 20 || ActualHeight < 20)
            return;

        var outer = GetOuterPoints(vm);
        if (outer.Count < 3) return;

        var bounds = Bounds(outer);
        double modelWidth = Math.Max(bounds.Width, 1);
        double modelHeight = Math.Max(bounds.Height, 1);
        double padLeft = 82, padRight = 105, padTop = 62, padBottom = 70;
        double scale = Math.Min((ActualWidth - padLeft - padRight) / modelWidth,
            (ActualHeight - padTop - padBottom) / modelHeight);
        if (!double.IsFinite(scale) || scale <= 0) return;

        double cx = padLeft + (ActualWidth - padLeft - padRight) / 2;
        double cy = padTop + (ActualHeight - padTop - padBottom) / 2;
        Point ToScreen((double X, double Y) point) =>
            new(cx + (point.X - (bounds.Left + bounds.Width / 2)) * scale,
                cy - (point.Y - (bounds.Top + bounds.Height / 2)) * scale);

        var screenOuter = outer.Select(ToScreen).ToList();
        var outerGeometry = Polygon(screenOuter);
        var fillGeometry = outerGeometry;
        List<(double X, double Y)>? inner = null;
        if (vm.Shape == ParametricRcShape.Annulus)
        {
            inner = CirclePointsInMeters(vm.InnerDiameterMm, 40);
            var evenOdd = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (var context = evenOdd.Open())
            {
                AddPolygon(context, screenOuter);
                AddPolygon(context, inner.Select(ToScreen).ToList());
            }
            evenOdd.Freeze();
            fillGeometry = evenOdd;
        }

        dc.DrawGeometry(ConcreteBrush, OutlinePen, fillGeometry);
        if (inner is not null)
            dc.DrawGeometry(null, OutlinePen, Polygon(inner.Select(ToScreen).ToList()));

        var center = new Point(cx, cy);
        DrawAxes(dc, center, padLeft, padRight, padTop, padBottom);

        dc.PushClip(fillGeometry);
        DrawLongitudinal(dc, vm, outer, ToScreen, scale);
        dc.Pop();
        DrawStirrups(dc, vm, ToScreen, scale);
        DrawDimensions(dc, vm, ToScreen, bounds, padLeft, padRight, padTop, padBottom);
    }

    static List<(double X, double Y)> GetOuterPoints(ParametricRcSectionVM vm)
    {
        double width = vm.WidthMm / 1000.0;
        double height = vm.HeightMm / 1000.0;
        double web = vm.WebMm / 1000.0;
        double flange = vm.FlangeMm / 1000.0;
        return vm.Shape switch
        {
            ParametricRcShape.Rectangle => TemplatePoints.RectPoints(width, height),
            ParametricRcShape.Tee => TemplatePoints.TeePoints(width, height, web, flange),
            ParametricRcShape.IBeam => TemplatePoints.IBeamPoints(height, width, web, flange),
            ParametricRcShape.Circle or ParametricRcShape.Annulus => CirclePointsInMeters(width * 1000.0, 40),
            _ => []
        };
    }

    void DrawLongitudinal(DrawingContext dc, ParametricRcSectionVM vm,
        IReadOnlyList<(double X, double Y)> boundary,
        Func<(double X, double Y), Point> toScreen, double scale)
    {
        if (vm.Shape is ParametricRcShape.Circle or ParametricRcShape.Annulus)
        {
            if (vm.PolarRebarCount < 1) return;
            double radius = vm.PolarRebarRadiusMm / 1000.0;
            double diameter = vm.PolarRebarDiameterMm / 1000.0;
            for (int i = 0; i < vm.PolarRebarCount; i++)
            {
                double angle = 2 * Math.PI * i / vm.PolarRebarCount;
                DrawBar(dc, toScreen((radius * Math.Cos(angle), radius * Math.Sin(angle))), diameter * scale);
            }
            return;
        }

        var definition = vm.BuildDefinition();
        DrawLayer(dc, boundary, definition, definition.LowerRebar, vm.LowerRebarEnabled, vm.LowerRebarIdealized,
            vm.LowerRebarDiameterMm, vm.LowerRebarAreaMm2, vm.GetRebarCentroidCoordinateMm(true),
            vm.LowerRebarAxis, Math.Max(0, vm.StirrupCoverMm) / 1000.0, toScreen, scale);
        DrawLayer(dc, boundary, definition, definition.UpperRebar, vm.UpperRebarEnabled, vm.UpperRebarIdealized,
            vm.UpperRebarDiameterMm, vm.UpperRebarAreaMm2, vm.GetRebarCentroidCoordinateMm(false),
            vm.UpperRebarAxis, Math.Max(0, vm.StirrupCoverMm) / 1000.0, toScreen, scale);
    }

    void DrawLayer(DrawingContext dc, IReadOnlyList<(double X, double Y)> boundary,
        ParametricRcSectionDefinition definition, ParametricLongitudinalLayer? layer,
        bool enabled, bool idealized, double diameterMm, double areaMm2,
        double coordinateMm, IdealizedRebarAxis axis,
        double sideCoverM,
        Func<(double X, double Y), Point> toScreen, double scale)
    {
        if (!enabled) return;
        double coordinate = coordinateMm / 1000.0;
        double diameter = diameterMm / 1000.0;
        if (idealized)
        {
            var pen = new Pen(RebarBrush, Math.Max(3,
                Math.Sqrt(Math.Max(areaMm2, 1) / 1_000_000.0) * scale))
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var span = GetIdealizedLineSpan(boundary, coordinate, axis, sideCoverM);
            if (axis == IdealizedRebarAxis.Mx)
                dc.DrawLine(pen, toScreen((span.Min, coordinate)), toScreen((span.Max, coordinate)));
            else
                dc.DrawLine(pen, toScreen((coordinate, span.Min)), toScreen((coordinate, span.Max)));
            return;
        }

        // Те же абсциссы, что у генератора сечения, — схема совпадает с расчётным сечением.
        if (layer is null) return;
        foreach (double x in ParametricRcSectionGenerator.GetPhysicalBarPositionsX(definition, layer))
            DrawBar(dc, toScreen((x, coordinate)), diameter * scale);
    }

    /// <summary>Находит отрезок фактического контура, доступный для условного слоя.</summary>
    internal static (double Min, double Max) GetIdealizedLineSpan(
        IReadOnlyList<(double X, double Y)> polygon, double coordinate, IdealizedRebarAxis axis,
        double coverM = 0)
    {
        var intersections = new List<double>();
        for (int i = 0; i < polygon.Count; i++)
        {
            var first = polygon[i];
            var second = polygon[(i + 1) % polygon.Count];
            double firstNormal = axis == IdealizedRebarAxis.Mx ? first.Y : first.X;
            double secondNormal = axis == IdealizedRebarAxis.Mx ? second.Y : second.X;
            if (!((firstNormal <= coordinate && secondNormal > coordinate) ||
                  (secondNormal <= coordinate && firstNormal > coordinate)))
                continue;
            double ratio = (coordinate - firstNormal) / (secondNormal - firstNormal);
            double value = axis == IdealizedRebarAxis.Mx
                ? first.X + ratio * (second.X - first.X)
                : first.Y + ratio * (second.Y - first.Y);
            intersections.Add(value);
        }

        (double Min, double Max) span = intersections.Count < 2
            ? axis == IdealizedRebarAxis.Mx
                ? (polygon.Min(p => p.X), polygon.Max(p => p.X))
                : (polygon.Min(p => p.Y), polygon.Max(p => p.Y))
            : (intersections.Min(), intersections.Max());

        double cover = Math.Max(0, coverM);
        double min = span.Min + cover;
        double max = span.Max - cover;
        if (min > max)
        {
            double center = (span.Min + span.Max) / 2;
            return (center, center);
        }

        return (min, max);
    }

    void DrawStirrups(DrawingContext dc, ParametricRcSectionVM vm,
        Func<(double X, double Y), Point> toScreen, double scale)
    {
        if (!vm.IsStirrupFieldsEnabled || vm.StirrupCount <= 0) return;
        foreach (var segment in GetStirrupPreviewSegments(vm.BuildDefinition()))
        {
            var pen = new Pen(StirrupBrush, Math.Max(1.5, segment.DiameterM * scale / 2))
            { DashStyle = DashStyles.Dash };
            dc.DrawLine(pen, toScreen((segment.X0, segment.Y0)), toScreen((segment.X1, segment.Y1)));
        }
    }

    /// <summary>Возвращает линии срезов поперечной арматуры в координатах модели.</summary>
    internal static IReadOnlyList<(double X0, double Y0, double X1, double Y1, double DiameterM)>
        GetStirrupPreviewSegments(ParametricRcSectionDefinition definition)
    {
        var zones = ParametricRcSectionGenerator.GetStirrupZones(definition);

        var segments = new List<(double X0, double Y0, double X1, double Y1, double DiameterM)>();
        foreach (var set in definition.StirrupCuts.Where(c => c.Count > 0))
        {
            if (!zones.TryGetValue(set.Zone, out var zone)) continue;
            double x0 = zone.MinX + set.CoverM, x1 = zone.MaxX - set.CoverM;
            double y0 = zone.MinY + set.CoverM, y1 = zone.MaxY - set.CoverM;
            for (int i = 0; i < set.Count; i++)
            {
                double ratio = (i + 1.0) / (set.Count + 1.0);
                segments.Add(set.Direction == ParametricStirrupDirection.Vertical
                    ? (x0 + ratio * (x1 - x0), y0, x0 + ratio * (x1 - x0), y1, set.DiameterM)
                    : (x0, y0 + ratio * (y1 - y0), x1, y0 + ratio * (y1 - y0), set.DiameterM));
            }
        }
        return segments;
    }

    void DrawDimensions(DrawingContext dc, ParametricRcSectionVM vm,
        Func<(double X, double Y), Point> toScreen, Rect bounds,
        double padLeft, double padRight, double padTop, double padBottom)
    {
        double modelTop = bounds.Bottom;
        double modelBottom = bounds.Top;
        var topLeft = toScreen((bounds.Left, modelTop));
        var topRight = toScreen((bounds.Right, modelTop));
        var widthSymbol = vm.Shape is ParametricRcShape.Circle or ParametricRcShape.Annulus
            ? Loc.S("ParametricRcPreviewSymbolD") : Loc.S("ParametricRcPreviewSymbolB");
        DrawDimension(dc, topLeft, topRight, new Vector(0, -34),
            $"{widthSymbol} = {Format(vm.WidthMm)}");
        if (vm.IsHeightEnabled)
        {
            var bottomLeft = toScreen((bounds.Left, modelBottom));
            DrawDimension(dc, topLeft, bottomLeft, new Vector(-42, 0),
                $"{Loc.S("ParametricRcPreviewSymbolH")} = {Format(vm.HeightMm)}");
        }

        if (vm.Shape is ParametricRcShape.Tee or ParametricRcShape.IBeam)
        {
            var callout = new Point(topRight.X + 12, topRight.Y + 22);
            DrawText(dc, callout,
                $"{Loc.S("ParametricRcPreviewSymbolBw")} = {Format(vm.WebMm)}\n{Loc.S("ParametricRcPreviewSymbolHf")} = {Format(vm.FlangeMm)}");
        }
        else if (vm.Shape == ParametricRcShape.Annulus)
        {
            var innerDiameter = GetInnerDiameterEndpointsInMeters(vm.InnerDiameterMm);
            var innerLeft = toScreen(innerDiameter.Left);
            var innerRight = toScreen(innerDiameter.Right);
            double lowerDimensionY = toScreen((0, bounds.Top)).Y + 26;
            DrawDimension(dc, innerLeft, innerRight,
                new Vector(0, lowerDimensionY - innerLeft.Y),
                $"{Loc.S("ParametricRcPreviewSymbolDLower")} = {Format(vm.InnerDiameterMm)}");
        }

        if (vm.Shape is ParametricRcShape.Circle or ParametricRcShape.Annulus)
        {
            DrawText(dc, new Point(padLeft, ActualHeight - padBottom + 46),
                $"{Loc.S("ParametricRcPreviewSymbolN")}×{Loc.S("ParametricRcPreviewSymbolDiameter")} = {vm.PolarRebarCount}×{Format(vm.PolarRebarDiameterMm)}");
        }
        else
        {
            DrawLayerLabel(dc, vm.LowerRebarEnabled, true, new Point(padLeft, ActualHeight - padBottom + 10),
                vm.LowerRebarIdealized, vm.LowerRebarCount, vm.LowerRebarDiameterMm, vm.LowerRebarAreaMm2, vm.LowerRebarOffsetMm);
            DrawLayerLabel(dc, vm.UpperRebarEnabled, false, new Point(padLeft, padTop + 2),
                vm.UpperRebarIdealized, vm.UpperRebarCount, vm.UpperRebarDiameterMm, vm.UpperRebarAreaMm2, vm.UpperRebarOffsetMm);
        }

        if (vm.IsStirrupFieldsEnabled && vm.StirrupCount > 0)
            DrawText(dc, new Point(ActualWidth - padRight + 12, ActualHeight / 2 - 16),
                $"{Loc.S("ParametricRcPreviewSymbolN")} = {vm.StirrupCount}\n{Loc.S("ParametricRcPreviewSymbolStirrup")} = {Format(vm.StirrupDiameterMm)}\n{Loc.S("ParametricRcPreviewSymbolS")} = {Format(vm.StirrupStepMm)}\n{Loc.S("ParametricRcPreviewSymbolC")} = {Format(vm.StirrupCoverMm)}");
    }

    void DrawLayerLabel(DrawingContext dc, bool enabled, bool lower, Point point, bool idealized,
        int count, double diameterMm, double areaMm2, double coordinateMm)
    {
        if (!enabled) return;
        string main = idealized
            ? $"{Loc.S("ParametricRcPreviewSymbolArea")} = {Format(areaMm2)}"
            : $"{Loc.S("ParametricRcPreviewSymbolN")}×{Loc.S("ParametricRcPreviewSymbolDiameter")} = {count}×{Format(diameterMm)}";
        string offsetSymbol = lower
            ? Loc.S("ParametricRcPreviewSymbolAs")
            : Loc.S("ParametricRcPreviewSymbolAsPrime");
        DrawText(dc, point, $"{main}; {offsetSymbol} = {Format(coordinateMm)}");
    }

    void DrawAxes(DrawingContext dc, Point center,
        double padLeft, double padRight, double padTop, double padBottom)
    {
        var xEnd = new Point(ActualWidth - padRight + 18, center.Y);
        var xStart = new Point(padLeft - 18, center.Y);
        var yEnd = new Point(center.X, padTop - 16);
        var yStart = new Point(center.X, ActualHeight - padBottom + 16);
        dc.DrawLine(AxisPen, xStart, xEnd);
        dc.DrawLine(AxisPen, yStart, yEnd);
        DrawArrow(dc, xEnd, new Point(xEnd.X - 18, xEnd.Y), AxisPen);
        DrawArrow(dc, yEnd, new Point(yEnd.X, yEnd.Y + 18), AxisPen);
        DrawText(dc, new Point(xEnd.X + 4, xEnd.Y - 16), Loc.S("ParametricRcPreviewAxisX"));
        DrawText(dc, new Point(yEnd.X + 5, yEnd.Y - 2), Loc.S("ParametricRcPreviewAxisY"));
    }

    static void DrawDimension(DrawingContext dc, Point first, Point second, Vector offset, string text)
    {
        var a = first + offset;
        var b = second + offset;
        var pen = new Pen(AxisBrush, 0.8);
        dc.DrawLine(pen, first, a);
        dc.DrawLine(pen, second, b);
        dc.DrawLine(pen, a, b);
        DrawArrow(dc, a, b, pen);
        DrawArrow(dc, b, a, pen);
        var ft = Formatted(text, 11, Brushes.Black);
        dc.DrawText(ft, new Point((a.X + b.X - ft.Width) / 2, (a.Y + b.Y) / 2 - ft.Height - 1));
    }

    static void DrawArrow(DrawingContext dc, Point tip, Point back, Pen pen)
    {
        Vector direction = back - tip;
        direction.Normalize();
        Vector side = new(-direction.Y, direction.X);
        Point p1 = tip + direction * 7 + side * 3;
        Point p2 = tip + direction * 7 - side * 3;
        dc.DrawLine(pen, tip, p1);
        dc.DrawLine(pen, tip, p2);
    }

    static void DrawText(DrawingContext dc, Point point, string text) =>
        dc.DrawText(Formatted(text, 11, Brushes.DimGray), point);

    static FormattedText Formatted(string text, double size, Brush brush) => new(
        text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
        new Typeface("Segoe UI"), size, brush, 1.0);

    static void DrawBar(DrawingContext dc, Point center, double diameter)
    {
        dc.DrawEllipse(RebarBrush, OutlinePen, center,
            Math.Max(2, diameter / 2), Math.Max(2, diameter / 2));
    }

    static void AddPolygon(StreamGeometryContext context, IReadOnlyList<Point> points)
    {
        context.BeginFigure(points[0], true, true);
        for (int i = 1; i < points.Count; i++) context.LineTo(points[i], true, false);
    }

    static Geometry Polygon(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        AddPolygon(context, points);
        geometry.Freeze();
        return geometry;
    }

    static List<(double X, double Y)> Circle(double diameterMm, int count)
    {
        double radius = diameterMm / 2.0;
        return Enumerable.Range(0, count)
            .Select(i =>
            {
                double angle = 2 * Math.PI * i / count;
                return (radius * Math.Cos(angle), radius * Math.Sin(angle));
            }).ToList();
    }

    /// <summary>Строит окружность в метрах по диаметру, заданному в миллиметрах.</summary>
    internal static List<(double X, double Y)> CirclePointsInMeters(double diameterMm, int count) =>
        Circle(diameterMm, count)
            .Select(p => (p.X / 1000.0, p.Y / 1000.0))
            .ToList();

    /// <summary>Возвращает концы размерной линии внутреннего диаметра в метрах.</summary>
    internal static ((double X, double Y) Left, (double X, double Y) Right)
        GetInnerDiameterEndpointsInMeters(double diameterMm, double yM = 0) =>
        ((-diameterMm / 2000.0, yM), (diameterMm / 2000.0, yM));

    static Rect Bounds(IReadOnlyList<(double X, double Y)> points)
    {
        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    static string Format(double value) => value.ToString("G5", CultureInfo.CurrentCulture);

    static T Freeze<T>(T value) where T : Freezable
    {
        value.Freeze();
        return value;
    }
}
