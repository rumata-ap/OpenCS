using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CScore;
using CScore.LimitSurface;
using HelixToolkit.Wpf;
using OpenCS.LimitSurface;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using GeoMeshBuilder = HelixToolkit.Geometry.MeshBuilder;

namespace OpenCS.Views;

/// <summary>Окно поверхности предельных усилий: шапка, 3D (Helix), срезы (PlotCanvas), точки (DataGrid).</summary>
public partial class LimitSurfaceResultView : UserControl
{
    readonly LimitSurfaceResultVM _vm;
    readonly AppViewModel? _app;
    readonly CalcTask? _task;
    readonly ModelVisual3D _surface = new(), _points = new(), _slicePlane = new();
    readonly LinesVisual3D _edges = new() { Thickness = 1, Color = Colors.DimGray };
    readonly List<(Point3D Pos, ScenePoint Point)> _pointHits = [];
    readonly WpfPlotService _slicePlot;
    bool _syncingSlice;
    static readonly SliceKind[] SliceKinds = [SliceKind.AtN, SliceKind.AtMx, SliceKind.AtMy, SliceKind.NM];
    static readonly string[] FamilyColors = ["#7F7F7F", "#A5A5A5", "#BFBFBF"];

    public LimitSurfaceResultView(CalcResult result, AppViewModel? app = null, CalcTask? task = null)
    {
        InitializeComponent();
        _app = app;
        _task = task;
        _vm = new LimitSurfaceResultVM(result);
        _slicePlot = new WpfPlotService(SlicePlot);
        DataContext = _vm;
        if (_vm.Scene is null)
        {
            ErrorBlock.Visibility = Visibility.Visible;
            Header.Visibility = Warnings.Visibility = Tabs.Visibility = Visibility.Collapsed;
            return;
        }
        ModeAll.IsChecked = true;
        AxesRel.IsChecked = true;
        ColoringBox.ItemsSource = _vm.ColoringItems.Select(c => Loc.S($"LimitSurfaceColoring_{c}")).ToList();
        ColoringBox.SelectedIndex = 0;
        SliceKindBox.ItemsSource = SliceKinds.Select(k => Loc.S($"LimitSurfaceSlice_{k}")).ToList();
        SliceKindBox.SelectedIndex = 0;
        FamilyBox.ItemsSource = Enumerable.Range(1, 9).ToList();
        FamilyBox.SelectedIndex = 0;
        // Непрозрачное — раньше полупрозрачного: иначе буфер глубины прячет точки внутри полупрозрачной поверхности.
        Viewport.Children.Add(_points);
        Viewport.Children.Add(_edges);
        Viewport.Children.Add(_surface);
        Viewport.Children.Add(_slicePlane);
        _vm.SceneChanged += (_, _) => Redraw();
        _vm.SliceChanged += (_, _) => { RedrawSlice(); _slicePlane.Content = SlicePlaneModel(_vm.Scene); };
        Redraw();
    }

    void OnModeChecked(object sender, RoutedEventArgs e) => _vm.Mode =
        sender == ModeMoment ? UtilizationMode.Moment : sender == ModeAxial ? UtilizationMode.Axial : UtilizationMode.All;

    void OnAxesChecked(object sender, RoutedEventArgs e)
    {
        _vm.Axes = sender == AxesAbs ? AxisMode.Absolute : AxisMode.Relative;
        if (IsLoaded) Viewport.ZoomExtents();
    }

    void OnColoringChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ColoringBox.SelectedIndex >= 0) _vm.Coloring = _vm.ColoringItems[ColoringBox.SelectedIndex];
    }

    void Redraw()
    {
        var scene = _vm.Scene!;
        var s = _vm.Summary;
        SummaryText.Text = _vm.Points.Count == 0 ? "" :
            string.Format(Loc.S("LimitSurfaceSummaryFormat"), FormatRatio(s.MaxRatio), s.Outside, _vm.Points.Count, s.Ambiguous);
        Redraw3D(scene);
        RedrawSlice();
        RefreshPointsGrid();
    }

    Point3D P(ForcePoint p) { var v = _vm.Scene!.ToScene(p, _vm.Axes); return new Point3D(v.X, v.Y, v.Z); }

    static Vector3 V(Point3D p) => new((float)p.X, (float)p.Y, (float)p.Z);

    void Redraw3D(LimitSurfaceScene scene)
    {
        // Поверхность: по модели на группу цвета.
        var group = new Model3DGroup();
        byte alpha = _vm.Translucent ? (byte)140 : (byte)255;
        var legend = new List<UIElement>();
        foreach (var g in scene.SurfaceGroups(_vm.Coloring))
        {
            var mesh = new MeshGeometry3D();
            foreach (int t in g.Triangles)
            {
                var (a, b, c) = scene.Mesh.Triangles[t];
                int i0 = mesh.Positions.Count;
                mesh.Positions.Add(P(scene.Mesh.Vertices[a]));
                mesh.Positions.Add(P(scene.Mesh.Vertices[b]));
                mesh.Positions.Add(P(scene.Mesh.Vertices[c]));
                mesh.TriangleIndices.Add(i0); mesh.TriangleIndices.Add(i0 + 1); mesh.TriangleIndices.Add(i0 + 2);
            }
            var color = (Color)ColorConverter.ConvertFromString(g.ColorHex);
            color.A = alpha;
            var mat = new DiffuseMaterial(new SolidColorBrush(color));
            group.Children.Add(new GeometryModel3D(mesh, mat) { BackMaterial = mat });
            legend.Add(LegendRow(color, g.LegendText ?? Loc.S(g.LegendKey)));
        }
        _surface.Content = group;
        Legend.ItemsSource = legend;

        // Меридианы и параллели; у старых результатов без углов — рёбра треугольников.
        var edges = new Point3DCollection();
        if (_vm.ShowEdges && scene.HasGridLines)
        {
            var grid = scene.GridLines();
            foreach (var line in grid.Meridians.Concat(grid.Parallels))
                for (int i = 0; i + 1 < line.Count; i++) { edges.Add(P(line[i])); edges.Add(P(line[i + 1])); }
        }
        else if (_vm.ShowEdges)
            foreach (var (a, b, c) in scene.Mesh.Triangles)
            {
                Point3D pa = P(scene.Mesh.Vertices[a]), pb = P(scene.Mesh.Vertices[b]), pc = P(scene.Mesh.Vertices[c]);
                edges.Add(pa); edges.Add(pb); edges.Add(pb); edges.Add(pc); edges.Add(pc); edges.Add(pa);
            }
        _edges.Points = edges;

        // Точки набора: сферы по положению, выбранная — крупнее. Радиус — 1 % диагонали габарита в осях сцены.
        var lo = P(scene.Min);
        var hi = P(scene.Max);
        double r = 0.01 * (hi - lo).Length;
        var pts = new Model3DGroup();
        _pointHits.Clear();
        foreach (var state in new[] { PointState.Inside, PointState.Outside, PointState.Ambiguous })
        {
            var mb = new GeoMeshBuilder();
            bool any = false;
            foreach (var sp in _vm.Points.Where(p => p.State == state))
            {
                var pos = P(new ForcePoint(sp.Source.N, sp.Source.Mx, sp.Source.My));
                mb.AddSphere(V(pos), (float)(sp == _vm.SelectedPoint ? 2 * r : r), 10, 6);
                _pointHits.Add((pos, sp));
                any = true;
            }
            if (!any) continue;
            var brush = new SolidColorBrush(state switch
            {
                PointState.Inside => Color.FromRgb(0x2E, 0x7D, 0x32),
                PointState.Outside => Color.FromRgb(0xC6, 0x28, 0x28),
                _ => Color.FromRgb(0x9E, 0x9E, 0x9E),
            });
            pts.Children.Add(new GeometryModel3D(mb.ToMesh().ToWndMeshGeometry3D(true), new DiffuseMaterial(brush)));
        }
        // Маркер начала координат в относительных осях.
        if (_vm.Axes == AxisMode.Relative)
        {
            var mb = new GeoMeshBuilder();
            float side = (float)(1.5 * r);
            mb.AddBox(V(P(new ForcePoint(0, 0, 0))), side, side, side);
            pts.Children.Add(new GeometryModel3D(mb.ToMesh().ToWndMeshGeometry3D(true), new DiffuseMaterial(Brushes.Black)));
        }
        _points.Content = pts;
        _slicePlane.Content = SlicePlaneModel(scene);

        AxesText.Text = string.Format(Loc.S(_vm.Axes == AxisMode.Relative ? "LimitSurfaceAxesRelativeInfo" : "LimitSurfaceAxesAbsoluteInfo"),
            scene.Min.Mx, scene.Max.Mx, scene.Min.My, scene.Max.My, scene.Min.N, scene.Max.N);
    }

    static UIElement LegendRow(Color color, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
        row.Children.Add(new Border { Width = 14, Height = 10, Background = new SolidColorBrush(color), Margin = new Thickness(0, 0, 4, 0) });
        row.Children.Add(new TextBlock { Text = text });
        return row;
    }

    void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        var hit = Viewport.Viewport.FindHits(e.GetPosition(Viewport.Viewport)).FirstOrDefault(h => h.Visual == _points);
        if (hit is null || _pointHits.Count == 0) { HoverText.Text = ""; return; }
        var (_, sp) = _pointHits.MinBy(p => (p.Pos - hit.Position).Length);
        HoverText.Text = string.Format(Loc.S("LimitSurfacePointTooltip"),
            string.IsNullOrEmpty(sp.Source.Label) ? sp.Source.Num.ToString() : sp.Source.Label,
            sp.Source.N, sp.Source.Mx, sp.Source.My, FormatRatio(sp.Ratio));
    }

    static string FormatRatio(double k) => double.IsFinite(k) ? k.ToString("F3") : "∞";

    /// <summary>Полупрозрачный прямоугольник плоскости среза в пределах габарита сетки.</summary>
    Model3D? SlicePlaneModel(LimitSurfaceScene? s)
    {
        if (s is null || !_vm.ShowSlicePlane) return null;
        double l = _vm.SliceLevel;
        ForcePoint[] c = _vm.SliceKind switch
        {
            SliceKind.AtN => [new(l, s.Min.Mx, s.Min.My), new(l, s.Max.Mx, s.Min.My), new(l, s.Max.Mx, s.Max.My), new(l, s.Min.Mx, s.Max.My)],
            SliceKind.AtMx => [new(s.Min.N, l, s.Min.My), new(s.Max.N, l, s.Min.My), new(s.Max.N, l, s.Max.My), new(s.Min.N, l, s.Max.My)],
            SliceKind.AtMy => [new(s.Min.N, s.Min.Mx, l), new(s.Max.N, s.Min.Mx, l), new(s.Max.N, s.Max.Mx, l), new(s.Min.N, s.Max.Mx, l)],
            _ => NmPlane(s, l * Math.PI / 180),
        };
        var mesh = new MeshGeometry3D();
        foreach (var p in c) mesh.Positions.Add(P(p));
        foreach (int i in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(i);
        var mat = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(70, 0xC0, 0x00, 0x00)));
        return new GeometryModel3D(mesh, mat) { BackMaterial = mat };
    }

    /// <summary>Плоскость N–M по направлению α: через ось N, полуразмах — наибольший |M| габарита.</summary>
    static ForcePoint[] NmPlane(LimitSurfaceScene s, double a)
    {
        double m = MaxAbsM(s);
        double cx = Math.Cos(a) * m, cy = Math.Sin(a) * m;
        return [new(s.Min.N, -cx, -cy), new(s.Min.N, cx, cy), new(s.Max.N, cx, cy), new(s.Max.N, -cx, -cy)];
    }

    static double MaxAbsM(LimitSurfaceScene s) =>
        Math.Max(Math.Max(Math.Abs(s.Min.Mx), Math.Abs(s.Max.Mx)), Math.Max(Math.Abs(s.Min.My), Math.Abs(s.Max.My)));

    void RedrawSlice()
    {
        if (_vm.Scene is not { } scene) return;
        _syncingSlice = true;
        var (lo, hi) = scene.LevelRange(_vm.SliceKind);
        SliceSlider.Minimum = lo;
        SliceSlider.Maximum = hi;
        SliceSlider.Value = Math.Clamp(_vm.SliceLevel, lo, hi);
        SliceLevelBox.Text = _vm.SliceLevel.ToString("G6");
        SliceLevelLabel.Text = Loc.S($"LimitSurfaceSliceLevel_{_vm.SliceKind}");
        _syncingSlice = false;

        _slicePlot.Clear();
        var (xKey, yKey) = LimitSurfaceScene.SliceAxisKeys(_vm.SliceKind);
        string rel = _vm.Axes == AxisMode.Relative ? " " + Loc.S("LimitSurfaceAxisRelativeSuffix") : "";
        _slicePlot.SetXLabel(Loc.S(xKey) + rel);
        _slicePlot.SetYLabel(Loc.S(yKey) + rel);
        if (_vm.SliceKind == SliceKind.AtN && _vm.Axes == AxisMode.Absolute) _slicePlot.EnableSquareAxes();
        _slicePlot.SetOriginReferenceAxesVisibility(showXAxis: false, showYAxis: false);

        // Семейство рисуется первым, текущий уровень — поверх.
        var curves = scene.Slice(_vm.SliceKind, _vm.SliceLevels);
        for (int c = curves.Count - 1; c >= 0; c--)
            foreach (var loop in curves[c].Loops.Where(l => l.Count > 1))
            {
                double[] xs = loop.Select(q => Ax(q.X, true)).Append(Ax(loop[0].X, true)).ToArray();
                double[] ys = loop.Select(q => Ax(q.Y, false)).Append(Ax(loop[0].Y, false)).ToArray();
                _slicePlot.AddScatter(xs, ys, lineWidth: c == 0 ? 2 : 1,
                    color: c == 0 ? "#2F5597" : FamilyColors[Math.Min(c - 1, FamilyColors.Length - 1)]);
            }

        // Точки: прочие — серые проекции, выбранная — красная поверх + луч Кисп, если он лежит в плоскости среза.
        foreach (var sp in _vm.Points.Where(p => p != _vm.SelectedPoint && !_vm.HideOtherPoints))
        {
            var (x, y) = scene.Project(_vm.SliceKind, _vm.SliceLevel, F(sp));
            _slicePlot.AddMarkers([Ax(x, true)], [Ax(y, false)], markerSize: 4, color: "#9E9E9E");
        }
        if (_vm.SelectedPoint is { } sel)
        {
            var f = F(sel);
            if (sel.Capacity is { } cap && RayInSlice(sel))
            {
                var (ox, oy) = scene.Project(_vm.SliceKind, _vm.SliceLevel, RayOrigin(f));
                var (cx, cy) = scene.Project(_vm.SliceKind, _vm.SliceLevel, cap);
                _slicePlot.AddScatter([Ax(ox, true), Ax(cx, true)], [Ax(oy, false), Ax(cy, false)], lineWidth: 1, color: "#C00000");
            }
            var (x, y) = scene.Project(_vm.SliceKind, _vm.SliceLevel, f);
            _slicePlot.AddMarkers([Ax(x, true)], [Ax(y, false)], markerSize: 8, color: "#C00000");
        }
        _slicePlot.Refresh();
    }

    static ForcePoint F(ScenePoint sp) => new(sp.Source.N, sp.Source.Mx, sp.Source.My);

    /// <summary>Начало луча Кисп в текущем режиме.</summary>
    ForcePoint RayOrigin(ForcePoint f) => _vm.Mode switch
    {
        UtilizationMode.Moment => new ForcePoint(f.N, 0, 0),
        UtilizationMode.Axial => new ForcePoint(0, f.Mx, f.My),
        _ => new ForcePoint(0, 0, 0),
    };

    /// <summary>
    /// Луч Кисп лежит в плоскости среза, если и точка, и начало луча на её уровне — тогда отрезок
    /// «начало → поверхность» на графике честен. NM: плоскость содержит ось N, поэтому нужно, чтобы
    /// моменты точки и начала луча лежали на прямой направления α.
    /// </summary>
    bool RayInSlice(ScenePoint sp)
    {
        var f = F(sp);
        var o = RayOrigin(f);
        if (_vm.SliceKind == SliceKind.NM)
        {
            double a = _vm.SliceLevel * Math.PI / 180, ca = Math.Cos(a), sa = Math.Sin(a);
            double tol = 1e-6 * Math.Max(1, MaxAbsM(_vm.Scene!));
            return Math.Abs(f.My * ca - f.Mx * sa) <= tol && Math.Abs(o.My * ca - o.Mx * sa) <= tol;
        }
        double lf = LimitSurfaceScene.LevelThrough(_vm.SliceKind, sp.Source);
        double lo = LimitSurfaceScene.LevelThrough(_vm.SliceKind, new LimitSurfacePointRecord(0, "", o.N, o.Mx, o.My));
        double t = 1e-6 * Math.Max(1, Math.Abs(lf));
        return Math.Abs(lf - lo) <= t && Math.Abs(lf - _vm.SliceLevel) <= t;
    }

    /// <summary>Значение по оси 2D-графика в выбранных осях (относительные — доля размаха сетки по этой оси).</summary>
    double Ax(double v, bool horizontal)
    {
        if (_vm.Axes == AxisMode.Absolute) return v;
        var s = _vm.Scene!;
        double m = MaxAbsM(s);
        (double lo, double hi) = (_vm.SliceKind, horizontal) switch
        {
            (SliceKind.AtN, true) or (SliceKind.AtMy, true) => (s.Min.Mx, s.Max.Mx),
            (SliceKind.AtN, false) or (SliceKind.AtMx, true) => (s.Min.My, s.Max.My),
            (SliceKind.NM, true) => (-m, m),
            _ => (s.Min.N, s.Max.N),
        };
        return hi > lo ? (v - lo) / (hi - lo) : 0.5;
    }

    void OnSliceKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SliceKindBox.SelectedIndex >= 0) _vm.SliceKind = SliceKinds[SliceKindBox.SelectedIndex];
    }

    void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_syncingSlice) _vm.SliceLevel = e.NewValue;
    }

    void OnSliceLevelEdited(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(SliceLevelBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v))
            _vm.SliceLevel = v;
        else
            SliceLevelBox.Text = _vm.SliceLevel.ToString("G6");
    }

    void OnSliceLevelKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnSliceLevelEdited(sender, e);
    }

    void OnSliceThroughPoint(object sender, RoutedEventArgs e) => _vm.SliceThroughSelected();

    void OnFamilyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FamilyBox.SelectedIndex >= 0) _vm.SliceFamilyCount = FamilyBox.SelectedIndex + 1;
    }
    /// <summary>Строка таблицы точек (WPF-обёртка ScenePoint с текстами).</summary>
    sealed record PointRow(ScenePoint Point, int Num, string Label, double N, double Mx, double My,
        string RatioText, double RatioSort, string StateText, string ActiveText);

    bool _syncingGrid;
    IReadOnlyList<ScenePoint>? _rowsFor;
    List<PointRow> _rows = [];

    void RefreshPointsGrid()
    {
        _syncingGrid = true;
        // Строки пересобираются только при смене списка точек (режим Кисп) — иначе сохраняются сортировка и прокрутка.
        if (!ReferenceEquals(_rowsFor, _vm.Points))
        {
            _rowsFor = _vm.Points;
            _rows = _vm.Points.Select(p => new PointRow(p, p.Source.Num, p.Source.Label, p.Source.N, p.Source.Mx, p.Source.My,
                FormatRatio(p.Ratio) + (p.State == PointState.Ambiguous ? " ?" : ""),
                double.IsFinite(p.Ratio) ? p.Ratio : double.MaxValue,
                Loc.S($"LimitSurfaceState_{p.State}"),
                p.Active == ActiveCategory.None ? "" : Loc.S($"LimitSurfaceLegend_{p.Active}") + (p.SingleSign ? " (8.53)" : ""))).ToList();
            PointsGrid.ItemsSource = _rows;
        }
        PointsGrid.SelectedItem = _rows.FirstOrDefault(r => r.Point == _vm.SelectedPoint);
        _syncingGrid = false;
        PointsSummary.Text = SummaryText.Text;
        ToSingleTask.Visibility = _app != null && _task != null && LimitSurfaceRowTaskFactory.Supports(_task.Kind)
            ? Visibility.Visible : Visibility.Collapsed;
        ToSingleTask.IsEnabled = _vm.SelectedPoint != null;
    }

    void OnPointsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingGrid || PointsGrid.SelectedItem is not PointRow row) return;
        _vm.SelectedPoint = row.Point;
        _vm.SliceThroughSelected();
    }

    void OnToSingleTask(object sender, RoutedEventArgs e)
    {
        if (_app is null || _task is null || _vm.SelectedPoint is null) return;
        BatchRowTask.CreateAndRun(_app, _task, _vm.SelectedPoint.Source, _vm.Mode);
    }
}
