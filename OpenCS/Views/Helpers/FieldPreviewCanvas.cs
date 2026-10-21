using System.Windows;
using System.Windows.Media;
using CScore.Fem.Fields;

namespace OpenCS.Views.Helpers;

/// <summary>
/// Предпросмотр поля на плоскости участка: пластины участка — заливка по значению (жёлтый → красный, серый — нет
/// значения), точки таблицы — кружки цветом по значению; для узловых целей — кружки узлов в плане и точки таблицы.
/// </summary>
public sealed class FieldPreviewCanvas : FrameworkElement
{
    public static readonly DependencyProperty EvaluationProperty = DependencyProperty.Register(nameof(Evaluation),
        typeof(FieldEvaluation), typeof(FieldPreviewCanvas), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PatchIndexProperty = DependencyProperty.Register(nameof(PatchIndex),
        typeof(int), typeof(FieldPreviewCanvas), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(nameof(Points),
        typeof(IReadOnlyList<FieldPoint>), typeof(FieldPreviewCanvas), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty NodeTargetsProperty = DependencyProperty.Register(nameof(NodeTargets),
        typeof(IReadOnlyList<FieldNode>), typeof(FieldPreviewCanvas), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PlaneToleranceProperty = DependencyProperty.Register(nameof(PlaneTolerance),
        typeof(double), typeof(FieldPreviewCanvas), new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PointsHaveZProperty = DependencyProperty.Register(nameof(PointsHaveZ),
        typeof(bool), typeof(FieldPreviewCanvas), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public FieldEvaluation? Evaluation { get => (FieldEvaluation?)GetValue(EvaluationProperty); set => SetValue(EvaluationProperty, value); }
    /// <summary>Номер показываемого участка в <see cref="FieldEvaluation.Patches"/>.</summary>
    public int PatchIndex { get => (int)GetValue(PatchIndexProperty); set => SetValue(PatchIndexProperty, value); }
    /// <summary>Точки таблицы после совмещения.</summary>
    public IReadOnlyList<FieldPoint>? Points { get => (IReadOnlyList<FieldPoint>?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    /// <summary>Узлы-цели (пружины); у пластин — null.</summary>
    public IReadOnlyList<FieldNode>? NodeTargets { get => (IReadOnlyList<FieldNode>?)GetValue(NodeTargetsProperty); set => SetValue(NodeTargetsProperty, value); }
    /// <summary>Допуск точки до плоскости участка, м (как в настройках поля).</summary>
    public double PlaneTolerance { get => (double)GetValue(PlaneToleranceProperty); set => SetValue(PlaneToleranceProperty, value); }
    /// <summary>Без Z точки показываются на любом пологом участке — проецируются вертикально.</summary>
    public bool PointsHaveZ { get => (bool)GetValue(PointsHaveZProperty); set => SetValue(PointsHaveZProperty, value); }

    // Цвет значения — одна из Levels ступеней с замороженной кистью: фигуры одной ступени рисуются одной геометрией.
    // Отдельная фигура с новой кистью на каждый КЭ (25 тыс. КЭ + 7 тыс. точек) перерисовывалась по 15 с.
    const int Levels = 64;
    static readonly Brush[] LevelBrushes = Enumerable.Range(0, Levels).Select(i =>
    {
        var b = new SolidColorBrush(ValueColor(i, 0, Levels - 1));
        b.Freeze();
        return (Brush)b;
    }).ToArray();
    static readonly Pen EdgePen = Frozen(new Pen(Brushes.DimGray, 0.3));
    static readonly Pen PointPen = Frozen(new Pen(Brushes.Black, 0.3));
    static readonly Pen NodePen = Frozen(new Pen(Brushes.Black, 0.5));
    static readonly Pen TablePointPen = Frozen(new Pen(Brushes.Blue, 0.6));

    static Pen Frozen(Pen pen)
    {
        pen.Freeze();
        return pen;
    }

    /// <summary>Фигуры, сгруппированные по ступени цвета; ступень −1 — нет значения (серый).</summary>
    sealed class Batches
    {
        readonly Dictionary<int, (StreamGeometry Geo, StreamGeometryContext Ctx)> _byLevel = [];

        public StreamGeometryContext this[int level]
        {
            get
            {
                if (!_byLevel.TryGetValue(level, out var b))
                {
                    var geo = new StreamGeometry();
                    _byLevel[level] = b = (geo, geo.Open());
                }
                return b.Ctx;
            }
        }

        public void Draw(DrawingContext dc, Brush? noValue, Pen pen)
        {
            foreach (var (level, (geo, ctx)) in _byLevel.OrderBy(x => x.Key))
            {
                ctx.Close();
                geo.Freeze();
                dc.DrawGeometry(level < 0 ? noValue : LevelBrushes[level], pen, geo);
            }
        }
    }

    static void Polygon(StreamGeometryContext ctx, IReadOnlyList<Point> p)
    {
        ctx.BeginFigure(p[0], true, true);
        ctx.PolyLineTo(p.Skip(1).ToList(), true, false);
    }

    static void Circle(StreamGeometryContext ctx, Point c, double r)
    {
        ctx.BeginFigure(new Point(c.X - r, c.Y), true, true);
        ctx.ArcTo(new Point(c.X + r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        ctx.ArcTo(new Point(c.X - r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
        var eval = Evaluation;
        if (eval == null || RenderSize.Width < 10 || RenderSize.Height < 10) return;
        var values = eval.Targets.Where(t => t.Values is { Length: > 0 }).Select(t => t.Values![0]).ToList();
        if (Points is { Count: > 0 } pts) values.AddRange(pts.Where(p => p.Values.Length > 0).Select(p => p.Values[0]));
        double vMin = values.Count > 0 ? values.Min() : 0, vMax = values.Count > 0 ? values.Max() : 1;
        int Level(double v) => (int)Math.Round(Math.Clamp(vMax > vMin ? (v - vMin) / (vMax - vMin) : 0.5, 0, 1) * (Levels - 1));
        var byTag = new Dictionary<string, FieldTargetValue>(StringComparer.Ordinal);
        foreach (var t in eval.Targets) byTag.TryAdd(t.Tag, t);
        int TargetLevel(string tag) => byTag.GetValueOrDefault(tag)?.Values is { Length: > 0 } v ? Level(v[0]) : -1;

        if (eval.Patches.Count > 0)
        {
            var patch = eval.Patches[Math.Clamp(PatchIndex, 0, eval.Patches.Count - 1)];
            var map = Map(patch.Shells.SelectMany(s => s.Nodes).Select(patch.Project).ToList());
            var shells = new Batches();
            foreach (var s in patch.Shells)
                Polygon(shells[TargetLevel(s.Tag)], s.Nodes.Select(n => map(patch.Project(n))).ToList());
            shells.Draw(dc, Brushes.LightGray, EdgePen);
            if (Points == null) return;
            var points = new Batches();
            foreach (var p in Points)
            {
                FieldVec3 q;
                if (PointsHaveZ)
                {
                    if (patch.DistanceToPlane(p.P) > PlaneTolerance) continue;
                    q = p.P;
                }
                else
                {
                    // Таблица без Z — точка вертикально на плоскость участка, как при переносе; на стенах не показывается.
                    if (patch.IsSteep) continue;
                    var n = patch.Normal;
                    q = p.P with { Z = patch.Origin.Z - (n.X * (p.P.X - patch.Origin.X) + n.Y * (p.P.Y - patch.Origin.Y)) / n.Z };
                }
                // Все столбцы «не использовать» — точки без значений, серым.
                Circle(points[p.Values.Length > 0 ? Level(p.Values[0]) : -1], map(patch.Project(q)), 2);
            }
            points.Draw(dc, Brushes.Gray, PointPen);
            return;
        }
        if (NodeTargets is { Count: > 0 } nodes)
        {
            var map = Map(nodes.Select(n => (n.P.X, n.P.Y)).Concat((Points ?? []).Select(p => (p.P.X, p.P.Y))).ToList());
            var targets = new Batches();
            foreach (var n in nodes) Circle(targets[TargetLevel(n.Tag)], map((n.P.X, n.P.Y)), 4);
            targets.Draw(dc, Brushes.LightGray, NodePen);
            if (Points == null) return;
            var table = new StreamGeometry();
            using (var ctx = table.Open())
                foreach (var p in Points) Circle(ctx, map((p.P.X, p.P.Y)), 2.5);
            table.Freeze();
            dc.DrawGeometry(null, TablePointPen, table);
        }
    }

    /// <summary>Вписывает координаты (u, v) в поле с отступом 10 px, ось v — вверх.</summary>
    Func<(double U, double V), Point> Map(IReadOnlyList<(double U, double V)> uv)
    {
        double u0 = uv.Min(p => p.U), u1 = uv.Max(p => p.U), v0 = uv.Min(p => p.V), v1 = uv.Max(p => p.V);
        double k = Math.Min((RenderSize.Width - 20) / Math.Max(u1 - u0, 1e-9), (RenderSize.Height - 20) / Math.Max(v1 - v0, 1e-9));
        return p => new Point(10 + (p.U - u0) * k, RenderSize.Height - 10 - (p.V - v0) * k);
    }

    static Color ValueColor(double v, double min, double max)
    {
        double t = max > min ? Math.Clamp((v - min) / (max - min), 0, 1) : 0.5;
        return Color.FromRgb(255, (byte)(230 * (1 - t)), (byte)(60 * (1 - t)));
    }
}
