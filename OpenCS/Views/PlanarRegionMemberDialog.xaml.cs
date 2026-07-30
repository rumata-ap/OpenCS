using CScore.Fem;
using CScore.Planar;
using OpenCS.ViewModels;
using OpenCS.Views.Helpers;

namespace OpenCS.Views;

public partial class PlanarRegionMemberDialog : System.Windows.Window
{
    readonly PlanarRegionMemberVM _vm;

    public FemMember? SavedMember { get; private set; }
    public FemMember? DeletedMember { get; private set; }

    public PlanarRegionMemberDialog(AppViewModel app, FemSchema schema, Frame3D frame,
        FemMember? existingMember = null, PlanarRegion? existingRegion = null)
    {
        InitializeComponent();
        _vm = new PlanarRegionMemberVM(app, schema, frame, existingMember, existingRegion);
        DataContext = _vm;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PlanarRegionMemberVM.PlotElements))
                UpdatePlot();
        };
        _vm.SaveCompleted += m => { SavedMember = m; DialogResult = true; Close(); };
        _vm.DeleteCompleted += m => { DeletedMember = m; DialogResult = true; Close(); };

        UpdatePlot();
    }

    void Window_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Owner != null)
        {
            Width = Owner.ActualWidth;
            Height = Owner.ActualHeight;
        }
    }

    void FitView_Click(object sender, System.Windows.RoutedEventArgs e) => preview.FitToView();

    void MoveGeometry_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new GeometryTransformDialog(GeometryTransformKind.Move, "PlanarRegionPanTool") { Owner = this };
        if (dlg.ShowDialog() == true) _vm.TranslateGeometry(dlg.Dx, dlg.Dy);
    }

    void ScaleGeometry_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new GeometryTransformDialog(GeometryTransformKind.Scale, "PlanarRegionZoomTool") { Owner = this };
        if (dlg.ShowDialog() == true) _vm.ScaleGeometry(dlg.Factor);
    }

    void RotateGeometry_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new GeometryTransformDialog(GeometryTransformKind.Rotate, "PlanarRegionRotateTool") { Owner = this };
        if (dlg.ShowDialog() == true) _vm.RotateGeometryDegrees(dlg.AngleDeg);
    }

    void MoveZoneGeometry_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new GeometryTransformDialog(GeometryTransformKind.Move, "PlanarRegionRebarZoneMoveTool") { Owner = this };
        if (dlg.ShowDialog() == true) _vm.TranslateZoneGeometry(dlg.Dx, dlg.Dy);
    }

    void ScaleZoneGeometry_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new GeometryTransformDialog(GeometryTransformKind.Scale, "PlanarRegionRebarZoneZoomTool") { Owner = this };
        if (dlg.ShowDialog() == true) _vm.ScaleZoneGeometry(dlg.Factor);
    }

    void RotateZoneGeometry_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new GeometryTransformDialog(GeometryTransformKind.Rotate, "PlanarRegionRebarZoneRotateTool") { Owner = this };
        if (dlg.ShowDialog() == true) _vm.RotateZoneGeometryDegrees(dlg.AngleDeg);
    }

    void UpdatePlot()
    {
        var elements = _vm.PlotElements;
        if (elements.Count == 0) { preview.Clear(); return; }

        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;
        var hull = _vm.Hull;
        if (hull != null)
            for (int i = 0; i < hull.X.Count; i++)
            {
                if (hull.X[i] < xMin) xMin = hull.X[i];
                if (hull.X[i] > xMax) xMax = hull.X[i];
                if (hull.Y[i] < yMin) yMin = hull.Y[i];
                if (hull.Y[i] > yMax) yMax = hull.Y[i];
            }

        if (xMin > xMax) { preview.Clear(); return; }
        if (xMax - xMin < 1e-9) { xMin -= 0.1; xMax += 0.1; }
        if (yMax - yMin < 1e-9) { yMin -= 0.1; yMax += 0.1; }

        preview.SetElements(elements, xMin, xMax, yMin, yMax);
    }
}
