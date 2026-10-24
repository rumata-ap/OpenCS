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
    readonly LinesVisual3D _edges = new() { Thickness = 0.5, Color = Colors.DimGray };
    readonly List<(Point3D Pos, ScenePoint Point)> _pointHits = [];

    public LimitSurfaceResultView(CalcResult result, AppViewModel? app = null, CalcTask? task = null)
    {
        InitializeComponent();
        _app = app;
        _task = task;
        _vm = new LimitSurfaceResultVM(result);
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
        Viewport.Children.Add(_surface);
        Viewport.Children.Add(_edges);
        Viewport.Children.Add(_points);
        Viewport.Children.Add(_slicePlane);
        _vm.SceneChanged += (_, _) => Redraw();
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

        // Рёбра сетки.
        var edges = new Point3DCollection();
        if (_vm.ShowEdges)
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
        _slicePlane.Content = null;

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

    void RedrawSlice() { }
    void RefreshPointsGrid() { }
}
