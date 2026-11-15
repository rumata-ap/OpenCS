using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OpenCS.Tasks;
using OpenCS.ViewModels;

namespace OpenCS.Views.Helpers;

/// <summary>
/// План перекрытия проверки продавливания по МКЭ: плиты одной отметки (контур и отверстия), узлы — грузовые площади
/// цветом статуса с подписью Кисп; выбранный узел обведён. Щелчок по узлу — <see cref="NodeClicked"/>; колесо —
/// масштаб у курсора, правая кнопка — сдвиг, двойной щелчок правой — вписать.
/// </summary>
public sealed class PunchingFemPlanCanvas : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(nameof(Data), typeof(PunchingFemCheckData),
        typeof(PunchingFemPlanCanvas), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((PunchingFemPlanCanvas)d).ResetView()));
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(nameof(Level), typeof(double?),
        typeof(PunchingFemPlanCanvas), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((PunchingFemPlanCanvas)d).ResetView()));
    public static readonly DependencyProperty SelectedNodeProperty = DependencyProperty.Register(nameof(SelectedNode), typeof(string),
        typeof(PunchingFemPlanCanvas), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Результат проверки.</summary>
    public PunchingFemCheckData? Data { get => (PunchingFemCheckData?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    /// <summary>Отметка плит, м; null — все.</summary>
    public double? Level { get => (double?)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    /// <summary>Выбранный узел.</summary>
    public string? SelectedNode { get => (string?)GetValue(SelectedNodeProperty); set => SetValue(SelectedNodeProperty, value); }

    /// <summary>Щелчок по узлу (тег).</summary>
    public event Action<string>? NodeClicked;
    /// <summary>Двойной щелчок по узлу (тег) — открыть его расчёт.</summary>
    public event Action<string>? NodeDoubleClicked;

    /// <summary>Вписать план в окно.</summary>
    public void FitView() => ResetView();

    /// <summary>Допуск отметки при отборе по уровню, м.</summary>
    public const double LevelTol = 0.05;

    double _scale = 1, _panX, _panY;
    bool _fitted;
    Point? _drag;
    readonly List<(string Node, Point At, double R)> _hit = [];

    static readonly Typeface Font = new("Segoe UI");
    static readonly Brush SlabFill = Frozen(new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF6)));
    static readonly Pen SlabPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x44, 0x55, 0x66)), 1.2));
    static readonly Pen SelectPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x1F, 0x4E, 0xD8)), 2.5));
    static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)));

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public PunchingFemPlanCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        SizeChanged += (_, _) => { _fitted = false; InvalidateVisual(); };
    }

    void ResetView() { _fitted = false; InvalidateVisual(); }

    bool OnLevel(double z) => Level is not double l || Math.Abs(z - l) <= LevelTol;

    (double X0, double Y0, double X1, double Y1)? Bounds()
    {
        if (Data is not { } d) return null;
        var pts = d.Slabs.Where(s => OnLevel(s.Z)).SelectMany(s => s.Loops.SelectMany(l => l))
            .Concat(d.Joints.Where(j => j.Origin.Length == 3 && OnLevel(j.Origin[2])).Select(j => new[] { j.Origin[0], j.Origin[1] })).ToList();
        if (pts.Count == 0) return null;
        return (pts.Min(p => p[0]), pts.Min(p => p[1]), pts.Max(p => p[0]), pts.Max(p => p[1]));
    }

    void Fit()
    {
        _fitted = true;
        if (Bounds() is not var (x0, y0, x1, y1) || ActualWidth < 10 || ActualHeight < 10) return;
        double w = Math.Max(x1 - x0, 1), h = Math.Max(y1 - y0, 1), m = 24;
        _scale = Math.Min((ActualWidth - 2 * m) / w, (ActualHeight - 2 * m) / h);
        _panX = ActualWidth / 2 - _scale * (x0 + x1) / 2;
        _panY = ActualHeight / 2 + _scale * (y0 + y1) / 2;
    }

    Point P(double x, double y) => new(_panX + _scale * x, _panY - _scale * y);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));
        _hit.Clear();
        _labelHit.Clear();
        if (Data is not { } d) return;
        if (!_fitted) Fit();
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        foreach (var s in d.Slabs.Where(s => OnLevel(s.Z)))
        {
            var geo = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (var g = geo.Open())
                foreach (var loop in s.Loops)
                {
                    g.BeginFigure(P(loop[0][0], loop[0][1]), true, true);
                    g.PolyLineTo(loop.Skip(1).Select(p => P(p[0], p[1])).ToList(), true, false);
                }
            geo.Freeze();
            dc.DrawGeometry(SlabFill, SlabPen, geo);
        }

        var labels = new List<(PunchingFemJointResult J, Point At, double R)>();
        foreach (var j in d.Joints.Where(j => j.Origin.Length == 3 && OnLevel(j.Origin[2])))
        {
            var brush = PunchingFemStatus.Brush(j.Status, j.Passed);
            var at = P(j.Origin[0], j.Origin[1]);
            double r = 7;
            if (j.Area.Count >= 3)
            {
                var pts = j.Area.Select(p => P(p[0], p[1])).ToList();
                double size = Math.Max(pts.Max(p => p.X) - pts.Min(p => p.X), pts.Max(p => p.Y) - pts.Min(p => p.Y));
                if (size >= 10)
                {
                    var geo = new StreamGeometry();
                    using (var g = geo.Open())
                    {
                        g.BeginFigure(pts[0], true, true);
                        g.PolyLineTo(pts.Skip(1).ToList(), true, false);
                    }
                    geo.Freeze();
                    dc.DrawGeometry(brush, j.Node == SelectedNode ? SelectPen : null, geo);
                    r = size / 2;
                }
                else DrawMarker(dc, brush, at, r, j.Node == SelectedNode);
            }
            else DrawMarker(dc, brush, at, r, j.Node == SelectedNode);
            _hit.Add((j.Node, at, Math.Max(r, 8)));
            labels.Add((j, at, r));
        }

        // Подписи без наложений: выбранный узел, затем не прошедшие и с наибольшим Кисп; остальные — при увеличении.
        var placed = new List<Rect>(labels.Select(l => new Rect(l.At.X - l.R, l.At.Y - l.R, 2 * l.R, 2 * l.R)));
        var order = labels
            .OrderByDescending(l => l.J.Node == SelectedNode)
            .ThenBy(l => l.J.Status == "no_check")
            .ThenBy(l => l.J.Passed)
            .ThenByDescending(l => l.J.Ratio);
        foreach (var (j, at, r) in order)
        {
            string label = j.Status == "no_check" ? j.Node + " —" : j.Node + "  " + Variants.K(j.Ratio);
            var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, 11, TextBrush, dpi);
            var rect = new Rect(at.X + r + 3, at.Y - text.Height / 2, text.Width, text.Height);
            bool selected = j.Node == SelectedNode;
            // Свой маркер (первые labels.Count прямоугольников — маркеры) пересечением не считается.
            if (!selected && placed.Where((p, i) => i >= labels.Count || labels[i].J != j).Any(p => p.IntersectsWith(rect))) continue;
            if (selected) dc.DrawRectangle(LabelBack, null, rect);
            dc.DrawText(text, rect.TopLeft);
            placed.Add(rect);
            _labelHit.Add((j.Node, rect));
        }
    }

    readonly List<(string Node, Rect Rect)> _labelHit = [];
    static readonly Brush LabelBack = Frozen(new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)));

    static void DrawMarker(DrawingContext dc, Brush brush, Point at, double r, bool selected) =>
        dc.DrawEllipse(brush, selected ? SelectPen : null, at, r, r);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var p = e.GetPosition(this);
        var best = _hit.Where(h => (h.At - p).Length <= h.R + 6).OrderBy(h => (h.At - p).Length).FirstOrDefault();
        string? node = best.Node ?? _labelHit.FirstOrDefault(l => l.Rect.Contains(p)).Node;
        if (node != null)
        {
            NodeClicked?.Invoke(node);
            if (e.ClickCount == 2) NodeDoubleClicked?.Invoke(node);
            e.Handled = true;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var p = e.GetPosition(this);
        double k = e.Delta > 0 ? 1.2 : 1 / 1.2;
        _panX = p.X - k * (p.X - _panX);
        _panY = p.Y - k * (p.Y - _panY);
        _scale *= k;
        _fitted = true;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (e.ClickCount == 2) { ResetView(); return; }
        _drag = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag is not { } start || e.RightButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(this);
        _panX += p.X - start.X;
        _panY += p.Y - start.Y;
        _drag = p;
        InvalidateVisual();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        _drag = null;
        ReleaseMouseCapture();
    }
}
