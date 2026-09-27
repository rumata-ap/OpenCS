using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views.Helpers;

/// <summary>
/// Живой предпросмотр параметрического МК-сечения: сформированный контур и отверстия в фактическом
/// положении, центральные оси x/y сечения, габариты и описание профиля.
/// </summary>
public sealed class ParametricSteelPreviewControl : FrameworkElement
{
    static readonly Brush SteelBrush = Freeze(new SolidColorBrush(Color.FromArgb(90, 100, 130, 160)));
    static readonly Brush AxisBrush = Freeze(new SolidColorBrush(Color.FromArgb(150, 70, 90, 100)));
    static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(50, 64, 72)));
    static readonly Pen OutlinePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(52, 78, 102)), 1.6));
    static readonly Pen AxisPen = Freeze(new Pen(AxisBrush, 0.9) { DashStyle = DashStyles.DashDot });
    static readonly Pen DimPen = Freeze(new Pen(AxisBrush, 0.8));

    /// <summary>Создаёт элемент.</summary>
    public ParametricSteelPreviewControl()
    {
        ClipToBounds = true;
        DataContextChanged += OnDataContextChanged;
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? 480 : availableSize.Width,
        double.IsInfinity(availableSize.Height) ? 480 : availableSize.Height);

    void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldVm) oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is INotifyPropertyChanged newVm) newVm.PropertyChanged += OnViewModelPropertyChanged;
        InvalidateVisual();
    }

    void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ParametricSteelSectionVM.Preview)) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(SystemColors.WindowBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (DataContext is not ParametricSteelSectionVM vm || ActualWidth < 40 || ActualHeight < 40) return;
        var area = vm.Preview.Section.Areas.FirstOrDefault();
        if (area?.Hull is not { } hull || hull.Points.Count < 3) return;

        var outer = hull.Points.Select(p => (p.X, p.Y)).ToList();
        var holes = area.Holes.Select(h => h.Points.Select(p => (p.X, p.Y)).ToList()).ToList();
        double minX = outer.Min(p => p.X), maxX = outer.Max(p => p.X);
        double minY = outer.Min(p => p.Y), maxY = outer.Max(p => p.Y);
        double w = Math.Max(maxX - minX, 1e-6), h = Math.Max(maxY - minY, 1e-6);
        const double padLeft = 60, padRight = 60, padTop = 40, padBottom = 70;
        double scale = Math.Min((ActualWidth - padLeft - padRight) / w, (ActualHeight - padTop - padBottom) / h);
        if (!double.IsFinite(scale) || scale <= 0) return;
        double cx = padLeft + (ActualWidth - padLeft - padRight) / 2;
        double cy = padTop + (ActualHeight - padTop - padBottom) / 2;
        double mx = (minX + maxX) / 2, my = (minY + maxY) / 2;
        Point S((double X, double Y) p) => new(cx + (p.X - mx) * scale, cy - (p.Y - my) * scale);

        var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var ctx = geometry.Open())
        {
            AddRing(ctx, outer.Select(S).ToList());
            foreach (var hole in holes) AddRing(ctx, hole.Select(S).ToList());
        }
        geometry.Freeze();
        dc.DrawGeometry(SteelBrush, OutlinePen, geometry);

        // Центральные оси сечения (генератор переносит контур в центр тяжести).
        var origin = S((0, 0));
        double ext = 22;
        var left = S((minX, 0)); var right = S((maxX, 0));
        var bottom = S((0, minY)); var top = S((0, maxY));
        dc.DrawLine(AxisPen, new Point(left.X - ext, origin.Y), new Point(right.X + ext, origin.Y));
        dc.DrawLine(AxisPen, new Point(origin.X, bottom.Y + ext), new Point(origin.X, top.Y - ext));
        DrawText(dc, Loc.S("ParametricRcPreviewAxisX"), new Point(right.X + ext + 3, origin.Y - 9), 12, true);
        DrawText(dc, Loc.S("ParametricRcPreviewAxisY"), new Point(origin.X + 4, top.Y - ext - 16), 12, true);

        // Габариты, мм: выносные линии, размерная линия со стрелками, подпись над линией.
        var bottomLeft = S((minX, minY));
        var bottomRight = S((maxX, minY));
        var topLeft = S((minX, maxY));
        DrawDimension(dc, bottomLeft, bottomRight, new Vector(0, 28), Mm(w), vertical: false);
        DrawDimension(dc, bottomLeft, topLeft, new Vector(-30, 0), Mm(h), vertical: true);

        if (!string.IsNullOrEmpty(vm.ProfileText))
            DrawText(dc, vm.ProfileText, new Point(8, ActualHeight - 22), 12, false);
    }

    static string Mm(double m) => (m * 1000).ToString("0.#", CultureInfo.CurrentCulture);

    static void AddRing(StreamGeometryContext ctx, IReadOnlyList<Point> ring)
    {
        ctx.BeginFigure(ring[0], true, true);
        ctx.PolyLineTo(ring.Skip(1).ToList(), true, false);
    }

    void DrawText(DrawingContext dc, string text, Point at, double size, bool bold)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal,
                FontStretches.Normal), size, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, at);
    }

    void DrawDimension(DrawingContext dc, Point first, Point second, Vector offset, string text, bool vertical)
    {
        var a = first + offset;
        var b = second + offset;
        // Выносные линии с небольшим зазором от контура и выпуском за размерную линию.
        var dir = offset;
        dir.Normalize();
        dc.DrawLine(DimPen, first + dir * 3, a + dir * 4);
        dc.DrawLine(DimPen, second + dir * 3, b + dir * 4);
        dc.DrawLine(DimPen, a, b);
        DrawArrow(dc, a, b);
        DrawArrow(dc, b, a);

        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        if (!vertical)
        {
            dc.DrawText(ft, new Point(mid.X - ft.Width / 2, mid.Y - ft.Height - 1));
            return;
        }
        // Вертикальный размер: подпись вдоль линии, читается снизу вверх.
        dc.PushTransform(new RotateTransform(-90, mid.X, mid.Y));
        dc.DrawText(ft, new Point(mid.X - ft.Width / 2, mid.Y - ft.Height - 1));
        dc.Pop();
    }

    static void DrawArrow(DrawingContext dc, Point tip, Point back)
    {
        var direction = back - tip;
        if (direction.Length < 1e-9) return;
        direction.Normalize();
        var side = new Vector(-direction.Y, direction.X);
        var arrow = new StreamGeometry();
        using (var ctx = arrow.Open())
        {
            ctx.BeginFigure(tip, true, true);
            ctx.LineTo(tip + direction * 8 + side * 2.5, true, false);
            ctx.LineTo(tip + direction * 8 - side * 2.5, true, false);
        }
        arrow.Freeze();
        dc.DrawGeometry(AxisBrush, null, arrow);
    }

    static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
