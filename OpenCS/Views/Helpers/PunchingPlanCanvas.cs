using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CScore.Sp63Punching;

namespace OpenCS.Views.Helpers;

/// <summary>
/// План узла продавливания: грузовая площадь, края плиты, отверстия с касательными, варианты контура.
/// Определяющий (или выбранный) контур — сплошной и толще, прочие — штрих; цвет — тон вердикта пакетов.
/// Щелчок по контуру выбирает вариант (<see cref="SelectedVariantId"/>, двусторонняя привязка).
/// </summary>
public sealed class PunchingPlanCanvas : FrameworkElement
{
    /// <summary>Сцена.</summary>
    public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
        nameof(Scene), typeof(PunchingScene), typeof(PunchingPlanCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Выбранный вариант (id ядра); null — выделен определяющий.</summary>
    public static readonly DependencyProperty SelectedVariantIdProperty = DependencyProperty.Register(
        nameof(SelectedVariantId), typeof(string), typeof(PunchingPlanCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Сцена.</summary>
    public PunchingScene? Scene { get => (PunchingScene?)GetValue(SceneProperty); set => SetValue(SceneProperty, value); }

    /// <summary>Выбранный вариант.</summary>
    public string? SelectedVariantId { get => (string?)GetValue(SelectedVariantIdProperty); set => SetValue(SelectedVariantIdProperty, value); }

    /// <summary>Тон вердикта: норма.</summary>
    public static readonly Color OkColor = Color.FromRgb(0x2D, 0x7A, 0x3E);
    /// <summary>Тон вердикта: близко к пределу (Кисп ≥ 0,99).</summary>
    public static readonly Color NearColor = Color.FromRgb(0xB7, 0x79, 0x1F);
    /// <summary>Тон вердикта: не проходит.</summary>
    public static readonly Color FailColor = Color.FromRgb(0xC0, 0x39, 0x2B);

    /// <summary>Цвет по Кисп (пороги — как у пакетов).</summary>
    public static Color VerdictColor(double k) =>
        !double.IsFinite(k) || k > 1.0 ? FailColor : k >= ViewModels.BatchVerdictView.NearLimitUtilization ? NearColor : OkColor;

    readonly List<(string Id, List<Point> Pts)> _hit = [];
    readonly ToolTip _tip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };
    string? _hoverId;

    static readonly Typeface Font = new("Segoe UI");
    static readonly Brush OutsideBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xEC, 0xEF, 0xF3)));
    static readonly Pen EdgePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)), 3));
    static readonly Pen AxisPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)), 0.8) { DashStyle = new DashStyle([10, 3, 2, 3], 0) });
    static readonly Pen RayPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)), 0.9) { DashStyle = new DashStyle([4, 3], 0) });
    static readonly Pen ExcludedPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)), 1.4) { DashStyle = new DashStyle([1.5, 2.5], 0) });
    static readonly Pen HolePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)), 1.4));
    static readonly Pen AreaPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)), 1.6));
    static readonly Brush AreaBrush = Frozen(new DrawingBrush(new GeometryDrawing(null,
        new Pen(new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)), 0.8), Geometry.Parse("M0,8 L8,0")))
    {
        TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 8, 8), ViewportUnits = BrushMappingMode.Absolute
    });

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    /// <summary>Создаёт холст.</summary>
    public PunchingPlanCanvas()
    {
        ClipToBounds = true;
        _tip.PlacementTarget = this;
        ToolTip = _tip;
        ToolTipService.SetIsEnabled(this, false);
        ToolTipService.SetInitialShowDelay(this, 150);
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
        _hit.Clear();
        var scene = Scene;
        if (scene is null || ActualWidth < 20 || ActualHeight < 20) return;

        var (x0, x1, y0, y1) = scene.Bounds();
        double span = Math.Max(x1 - x0, y1 - y0);
        double margin = Math.Max(0.15 * span, Math.Max(0.5 * scene.H0, 0.05));
        foreach (var e in scene.Edges)
        {
            double d = (scene.AreaCentroid - e.Point).Dot(e.InwardNormal);
            if (Math.Abs(d) <= Math.Max(span, 6 * scene.H0))
            {
                var foot = scene.AreaCentroid - d * e.InwardNormal;
                x0 = Math.Min(x0, foot.X); x1 = Math.Max(x1, foot.X);
                y0 = Math.Min(y0, foot.Y); y1 = Math.Max(y1, foot.Y);
            }
        }
        x0 -= margin; x1 += margin; y0 -= margin; y1 += margin;
        const double pad = 12, footer = 18;
        double w = ActualWidth - 2 * pad, h = ActualHeight - 2 * pad - footer;
        double scale = Math.Min(w / Math.Max(x1 - x0, 1e-9), h / Math.Max(y1 - y0, 1e-9));
        double ox = pad + (w - (x1 - x0) * scale) / 2, oy = pad + (h - (y1 - y0) * scale) / 2;
        Point M(PlanPoint p) => new(ox + (p.X - x0) * scale, oy + (y1 - p.Y) * scale);

        var view = new Rect(new Point(ox, oy), new Size((x1 - x0) * scale, (y1 - y0) * scale));
        dc.PushClip(new RectangleGeometry(view));

        double big = 10 * (x1 - x0 + y1 - y0);
        foreach (var e in scene.Edges)
        {
            var n = e.InwardNormal;
            var t = new PlanPoint(-n.Y, n.X);
            var a = e.Point + big * t;
            var b = e.Point - big * t;
            dc.DrawGeometry(OutsideBrush, null, Poly([a, b, b - big * n, a - big * n], M, true));
            dc.DrawLine(EdgePen, M(a), M(b));
        }

        var c0 = scene.AreaCentroid;
        dc.DrawLine(AxisPen, M(new PlanPoint(x0, c0.Y)), M(new PlanPoint(x1, c0.Y)));
        dc.DrawLine(AxisPen, M(new PlanPoint(c0.X, y0)), M(new PlanPoint(c0.X, y1)));

        double rayLen = 2 * (x1 - x0 + y1 - y0);
        foreach (var r in scene.HoleRays ?? [])
        {
            dc.DrawLine(RayPen, M(c0), M(c0 + rayLen * r.Lo));
            dc.DrawLine(RayPen, M(c0), M(c0 + rayLen * r.Hi));
        }
        foreach (var hole in scene.Holes)
        {
            dc.DrawGeometry(Brushes.White, HolePen, Poly(hole, M, true));
            if (hole.Count >= 4)
            {
                double hx0 = hole.Min(p => p.X), hx1 = hole.Max(p => p.X), hy0 = hole.Min(p => p.Y), hy1 = hole.Max(p => p.Y);
                dc.DrawLine(RayPen, M(new PlanPoint(hx0, hy0)), M(new PlanPoint(hx1, hy1)));
                dc.DrawLine(RayPen, M(new PlanPoint(hx0, hy1)), M(new PlanPoint(hx1, hy0)));
            }
        }

        dc.DrawGeometry(AreaBrush, AreaPen, Poly(scene.AreaOutline(), M, true));
        var inner = scene.AreaInnerOutline();
        if (inner.Count > 0) dc.DrawGeometry(Brushes.White, AreaPen, Poly(inner, M, true));

        string? selected = SelectedVariantId ?? scene.Variants.FirstOrDefault(v => v.Governs)?.Id;
        foreach (var v in scene.Variants.Where(v => v.Pieces.Count > 0).OrderBy(v => v.Id == selected).ThenBy(v => v.Id == _hoverId))
        {
            bool strong = v.Id == selected;
            bool hover = v.Id == _hoverId;
            var brush = new SolidColorBrush(VerdictColor(v.Utilization));
            brush.Freeze();
            var pen = new Pen(brush, strong ? 3 : hover ? 2.4 : 1.6) { LineJoin = PenLineJoin.Round };
            if (!strong) pen.DashStyle = new DashStyle([5, 3], 0);
            pen.Freeze();
            foreach (var piece in v.Pieces)
            {
                var pts = piece.SelectMany(PunchingScene.Tessellate).Select(M).ToList();
                dc.DrawGeometry(null, pen, Poly(pts));
                _hit.Add((v.Id, pts));
            }
            if (strong)
            {
                foreach (var x in v.Excluded)
                    dc.DrawGeometry(null, ExcludedPen, Poly(PunchingScene.Tessellate(x).Select(M).ToList()));
                var c = M(v.Centroid);
                var cp = new Pen(brush, 2); cp.Freeze();
                dc.DrawLine(cp, new Point(c.X - 7, c.Y), new Point(c.X + 7, c.Y));
                dc.DrawLine(cp, new Point(c.X, c.Y - 7), new Point(c.X, c.Y + 7));
            }
        }
        dc.Pop();

        var gray = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)); gray.Freeze();
        var px = M(new PlanPoint(x1, c0.Y));
        dc.DrawText(Text("X", 12, gray), new Point(px.X - 12, px.Y - 18));
        var py = M(new PlanPoint(c0.X, y1));
        dc.DrawText(Text("Y", 12, gray), new Point(py.X + 5, py.Y + 2));
        dc.DrawText(Text(string.Format(CultureInfo.CurrentCulture, Utilites.Loc.S("PunchingCanvasLegend", "h0 = {0:0} мм; + — центр тяжести контура"),
            scene.H0 * 1000), 11, gray), new Point(pad, ActualHeight - footer));
    }

    FormattedText Text(string s, double size, Brush brush) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    static StreamGeometry Poly(IReadOnlyList<PlanPoint> pts, Func<PlanPoint, Point> map, bool closed)
        => Poly(pts.Select(map).ToList(), closed);

    static StreamGeometry Poly(IReadOnlyList<Point> pts, bool closed = false)
    {
        var g = new StreamGeometry();
        if (pts.Count > 1)
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(pts[0], closed, closed);
                ctx.PolyLineTo(pts.Skip(1).ToList(), true, true);
            }
        g.Freeze();
        return g;
    }

    string? HitTest(Point p)
    {
        string? best = null;
        double bestD = 8;
        foreach (var (id, pts) in _hit)
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                double d = SegmentDistance(p, pts[i], pts[i + 1]);
                if (d < bestD) { bestD = d; best = id; }
            }
        return best;
    }

    static double SegmentDistance(Point p, Point a, Point b)
    {
        var ab = b - a;
        double len2 = ab.LengthSquared;
        double t = len2 > 0 ? Math.Clamp(Vector.Multiply(p - a, ab) / len2, 0, 1) : 0;
        return (p - (a + t * ab)).Length;
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        string? id = HitTest(e.GetPosition(this));
        if (id == _hoverId) return;
        _hoverId = id;
        Cursor = id is null ? null : Cursors.Hand;
        var v = id is null ? null : Scene?.Variants.FirstOrDefault(x => x.Id == id);
        ToolTipService.SetIsEnabled(this, v is not null);
        _tip.IsOpen = false;
        if (v is not null)
        {
            _tip.Content = $"{Variants.Title(v.Kind, v.Zone, v.Id, Scene!.Edges.Count)}\n{Utilites.Loc.S("PunchingKisp", "Кисп")} = {Variants.K(v.Utilization)}";
            _tip.IsOpen = true;
        }
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _tip.IsOpen = false;
        if (_hoverId is null) return;
        _hoverId = null;
        Cursor = null;
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (HitTest(e.GetPosition(this)) is { } id) SelectedVariantId = id;
    }
}

/// <summary>Локализованные подписи вариантов продавливания для окон.</summary>
public static class Variants
{
    /// <summary>Название варианта контура.</summary>
    public static string Title(PunchingVariantKind kind, PunchingZone zone, string id, int edgeCount)
    {
        string name = Utilites.Loc.S("PunchingVariant_" + kind, kind.ToString());
        if (kind == PunchingVariantKind.OpenEdge && edgeCount > 1)
        {
            int i = id.LastIndexOf('_');
            if (i >= 0 && int.TryParse(id[(i + 1)..], out int n)) name += $" {n + 1}";
        }
        return zone == PunchingZone.BeyondReinforcement ? name + Utilites.Loc.S("PunchingVariantOuterSuffix", " — за зоной арматуры") : name;
    }

    /// <summary>Краткое название варианта — заголовок столбца таблицы строк пакета.</summary>
    public static string Short(PunchingVariantKind kind, PunchingZone zone, string id, int edgeCount)
    {
        string name = Utilites.Loc.S("PunchingVariantShort_" + kind, kind.ToString());
        int i = id.LastIndexOf('_');
        bool numbered = (kind == PunchingVariantKind.OpenEdge && edgeCount > 1) || kind == PunchingVariantKind.Custom;
        if (numbered && i >= 0 && int.TryParse(id[(i + 1)..], out int n)) name += $" {n + 1}";
        return zone == PunchingZone.BeyondReinforcement ? name + Utilites.Loc.S("PunchingVariantShortOuterSuffix", ", за арматурой") : name;
    }

    /// <summary>
    /// Кисп для подписи — то же консервативное округление вверх, что в отчёте
    /// (<see cref="OpenCS.Reporting.ReportNumberFormatter.FormatUtilization"/>), разделитель — по культуре; «∞».
    /// </summary>
    public static string K(double k) => double.IsFinite(k)
        ? OpenCS.Reporting.ReportNumberFormatter.FormatUtilization(k).Replace(".", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator)
        : "∞";

    /// <summary>
    /// Локализованный текст предупреждения: шаблон <c>PunchingWarn_{code}</c> с аргументами
    /// (id контуров заменяются названиями, числа — по культуре); без шаблона или аргументов — русский текст ядра.
    /// </summary>
    public static string Warning(OpenCS.Reporting.PunchingReportWarning w, IReadOnlyList<OpenCS.Reporting.PunchingReportVariant> variants, int edgeCount)
    {
        // Шаблон с подстановками требует аргументов: у результатов до их появления — русский текст ядра.
        if (w.Code.Length > 0 && Utilites.Loc.TryGet("PunchingWarn_" + w.Code, out string template)
            && (w.Args.Count > 0 || !template.Contains("{0}", StringComparison.Ordinal)))
        {
            var args = w.Args.Select(a =>
            {
                var v = variants.FirstOrDefault(x => x.Id == a);
                if (v is not null) return Title(v.Kind, v.Zone, v.Id, edgeCount);
                return double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                    ? d.ToString("0.##", CultureInfo.CurrentCulture) : a;
            }).ToArray<object>();
            try { return string.Format(CultureInfo.CurrentCulture, template, args); }
            catch (FormatException) { }
        }
        string text = w.Text;
        foreach (var v in variants)
            text = text.Replace($"«{v.Id}»", $"«{Title(v.Kind, v.Zone, v.Id, edgeCount)}»", StringComparison.Ordinal);
        return text;
    }
}
