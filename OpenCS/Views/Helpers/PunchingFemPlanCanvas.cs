using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OpenCS.Tasks;
using OpenCS.ViewModels;

namespace OpenCS.Views.Helpers;

/// <summary>
/// План перекрытия проверки продавливания по МКЭ: плиты одной отметки (контур и отверстия), стены — полосой по всей
/// длине, узлы — грузовые площади колонн, у стен значки (торец — квадрат, угол — ромб) цветом статуса с подписью Кисп;
/// выбранный узел обведён. Щелчок по узлу — <see cref="NodeClicked"/>; колесо —
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
    static readonly Brush CapitalFill = Frozen(new SolidColorBrush(Color.FromRgb(0xF6, 0xEF, 0xDF)));
    static readonly Pen CapitalPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)), 1) { DashStyle = new DashStyle([5, 3], 0) });
    /// <summary>Контур продавливания ЛИРЫ: фиолетовый пунктир.</summary>
    public static readonly Pen LiraPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x8E, 0x24, 0xAA)), 1.4) { DashStyle = new DashStyle([4, 3], 0) });
    static readonly Pen SelectPen =Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x1F, 0x4E, 0xD8)), 2.5));
    static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)));
    static readonly Brush WallBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB1)));

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

        // Стены — полосой толщины стены (не тоньше 3 px) по всей длине примыкания.
        foreach (var w in d.Walls.Where(w => OnLevel(w.Z) && w.A.Length == 2 && w.B.Length == 2))
        {
            var pen = new Pen(WallBrush, Math.Max(3, w.T * _scale)) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
            pen.Freeze();
            dc.DrawLine(pen, P(w.A[0], w.A[1]), P(w.B[0], w.B[1]));
        }

        // Капители — светлой заливкой со штрихом под значками узлов.
        foreach (var j in d.Joints.Where(j => j.Capital.Count >= 3 && j.Origin.Length == 3 && OnLevel(j.Origin[2])))
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(P(j.Capital[0][0], j.Capital[0][1]), true, true);
                g.PolyLineTo(j.Capital.Skip(1).Select(p => P(p[0], p[1])).ToList(), true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(CapitalFill, CapitalPen, geo);
        }

        // Контуры продавливания ЛИРЫ (приложенный *.asp) — пунктиром по отрезкам, как их построила ЛИРА (у края — разомкнутые).
        foreach (var j in d.Joints.Where(j => j.Lira is { Contour.Count: > 0 } && j.Origin.Length == 3 && OnLevel(j.Origin[2])))
            foreach (var s in j.Lira!.Contour.Where(s => s.Length == 6))
                dc.DrawLine(LiraPen, P(s[0], s[1]), P(s[3], s[4]));

        // Box — экранная рамка значка узла: у стен кусок стены не центрирован на узле (торец, наружный угол), подпись — справа от рамки.
        var labels = new List<(PunchingFemJointResult J, Rect Box)>();
        foreach (var j in d.Joints.Where(j => j.Origin.Length == 3 && OnLevel(j.Origin[2])))
        {
            var brush = PunchingFemStatus.Brush(j.Status, j.Passed);
            var at = P(j.Origin[0], j.Origin[1]);
            double r = 7;
            var box = new Rect(at.X - r, at.Y - r, 2 * r, 2 * r);
            if (j.Kind is "wall_end" or "wall_corner")
            {
                // Узел стены — значок: торец у середины торцевой грани, угол — в пересечении осей стен (середина между
                // наружным и внутренним углом куска стены).
                if (j.Kind == "wall_corner" && j.Area.Count == 6)
                    at = P((j.Area[0][0] + j.Area[3][0]) / 2, (j.Area[0][1] + j.Area[3][1]) / 2);
                box = new Rect(at.X - r, at.Y - r, 2 * r, 2 * r);
                DrawWallMarker(dc, brush, at, r, j.Kind == "wall_corner", j.Node == SelectedNode);
            }
            else if (j.Area.Count >= 3)
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
                    box = new Rect(new Point(pts.Min(p => p.X), pts.Min(p => p.Y)), new Point(pts.Max(p => p.X), pts.Max(p => p.Y)));
                    at = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
                    r = size / 2;
                }
                else DrawMarker(dc, brush, at, r, j.Node == SelectedNode);
            }
            else DrawMarker(dc, brush, at, r, j.Node == SelectedNode);
            _hit.Add((j.Node, at, Math.Max(r, 8)));
            // Пунктир ЛИРЫ шире значка: подпись — справа от него, а не впритык к контуру.
            if (j.Lira is { Contour.Count: > 0 } lira)
                foreach (var s in lira.Contour.Where(s => s.Length == 6))
                {
                    box.Union(P(s[0], s[1]));
                    box.Union(P(s[3], s[4]));
                }
            labels.Add((j, box));
        }

        // Подписи без наложений: выбранный узел, затем не прошедшие и с наибольшим Кисп; остальные — при увеличении.
        var placed = new List<Rect>(labels.Select(l => l.Box));
        var order = labels
            .OrderByDescending(l => l.J.Node == SelectedNode)
            .ThenBy(l => l.J.Status == "no_check")
            .ThenBy(l => l.J.Passed)
            .ThenByDescending(l => l.J.Ratio);
        foreach (var (j, box) in order)
        {
            string label = j.Status == "no_check" ? j.Node + " —" : j.Node + "  " + Variants.K(j.Ratio);
            var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, 11, TextBrush, dpi);
            var rect = new Rect(box.Right + 3, box.Y + box.Height / 2 - text.Height / 2, text.Width, text.Height);
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

    static readonly Pen MarkerPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)), 1));

    /// <summary>Значок узла стены: торец — квадрат, угол — ромб; обводка тонкая, у выбранного — как у выбора.</summary>
    static void DrawWallMarker(DrawingContext dc, Brush brush, Point at, double r, bool corner, bool selected)
    {
        var pen = selected ? SelectPen : MarkerPen;
        if (!corner)
        {
            dc.DrawRectangle(brush, pen, new Rect(at.X - r, at.Y - r, 2 * r, 2 * r));
            return;
        }
        double k = r * 1.3;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(at.X, at.Y - k), true, true);
            g.PolyLineTo([new Point(at.X + k, at.Y), new Point(at.X, at.Y + k), new Point(at.X - k, at.Y)], true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(brush, pen, geo);
    }

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
