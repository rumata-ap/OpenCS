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

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
        var eval = Evaluation;
        if (eval == null || RenderSize.Width < 10 || RenderSize.Height < 10) return;
        var values = eval.Targets.Where(t => t.Values is { Length: > 0 }).Select(t => t.Values![0]).ToList();
        if (Points is { Count: > 0 } pts) values.AddRange(pts.Where(p => p.Values.Length > 0).Select(p => p.Values[0]));
        double vMin = values.Count > 0 ? values.Min() : 0, vMax = values.Count > 0 ? values.Max() : 1;
        var byTag = new Dictionary<string, FieldTargetValue>(StringComparer.Ordinal);
        foreach (var t in eval.Targets) byTag.TryAdd(t.Tag, t);
        Brush Fill(string tag) => byTag.GetValueOrDefault(tag)?.Values is { Length: > 0 } v
            ? new SolidColorBrush(ValueColor(v[0], vMin, vMax)) : Brushes.LightGray;

        if (eval.Patches.Count > 0)
        {
            var patch = eval.Patches[Math.Clamp(PatchIndex, 0, eval.Patches.Count - 1)];
            var map = Map(patch.Shells.SelectMany(s => s.Nodes).Select(patch.Project).ToList());
            var edge = new Pen(Brushes.DimGray, 0.3);
            foreach (var s in patch.Shells)
            {
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    var p = s.Nodes.Select(n => map(patch.Project(n))).ToList();
                    ctx.BeginFigure(p[0], true, true);
                    ctx.PolyLineTo(p.Skip(1).ToList(), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(Fill(s.Tag), edge, geo);
            }
            if (Points == null) return;
            var outline = new Pen(Brushes.Black, 0.3);
            foreach (var p in Points)
            {
                if (p.Values.Length == 0) continue;
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
                var fill = p.Values.Length > 0 ? new SolidColorBrush(ValueColor(p.Values[0], vMin, vMax)) : Brushes.Gray;
                dc.DrawEllipse(fill, outline, map(patch.Project(q)), 2, 2);
            }
            return;
        }
        if (NodeTargets is { Count: > 0 } nodes)
        {
            var map = Map(nodes.Select(n => (n.P.X, n.P.Y)).Concat((Points ?? []).Select(p => (p.P.X, p.P.Y))).ToList());
            var outline = new Pen(Brushes.Black, 0.5);
            foreach (var n in nodes) dc.DrawEllipse(Fill(n.Tag), outline, map((n.P.X, n.P.Y)), 4, 4);
            if (Points == null) return;
            var point = new Pen(Brushes.Blue, 0.6);
            foreach (var p in Points) dc.DrawEllipse(null, point, map((p.P.X, p.P.Y)), 2.5, 2.5);
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
