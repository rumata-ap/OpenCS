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

        // Габариты, мм.
        double dimY = S((0, minY)).Y + 18;
        dc.DrawLine(DimPen, new Point(S((minX, 0)).X, dimY), new Point(S((maxX, 0)).X, dimY));
        DrawCentered(dc, Mm(w), new Point(cx, dimY + 2));
        double dimX = S((minX, 0)).X - 18;
        dc.DrawLine(DimPen, new Point(dimX, S((0, minY)).Y), new Point(dimX, S((0, maxY)).Y));
        DrawText(dc, Mm(h), new Point(Math.Max(2, dimX - 44), cy - 8), 11, false);

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

    void DrawCentered(DrawingContext dc, string text, Point top)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(top.X - ft.Width / 2, top.Y));
    }

    static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
