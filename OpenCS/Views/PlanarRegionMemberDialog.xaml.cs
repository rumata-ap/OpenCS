using CScore.Fem;
using CScore.Planar;
using OpenCS.Utilites;
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
            if (e.PropertyName == nameof(PlanarRegionMemberVM.GeometryPlotElements))
                UpdateGeometryPlot();
            if (e.PropertyName == nameof(PlanarRegionMemberVM.RebarPlotElements))
                UpdateRebarPlot();
        };
        _vm.SaveCompleted += m => { SavedMember = m; DialogResult = true; Close(); };
        _vm.DeleteCompleted += m => { DeletedMember = m; DialogResult = true; Close(); };

        rebarPreview.ModelClicked += (x, y) => _vm.SelectZoneAtPoint(x, y);

        UpdateGeometryPlot();
        UpdateRebarPlot();
    }

    void Window_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Owner != null)
        {
            Width = Owner.ActualWidth;
            Height = Owner.ActualHeight;
        }
    }

    void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_vm.HasZones || !_vm.RebarLayoutDirty) return;

        var result = System.Windows.MessageBox.Show(
            Loc.S("PlanarRegionRebarUnsavedChangesPrompt"), Loc.S("Warning"),
            System.Windows.MessageBoxButton.YesNoCancel, System.Windows.MessageBoxImage.Warning);

        if (result == System.Windows.MessageBoxResult.Cancel) { e.Cancel = true; return; }
        if (result == System.Windows.MessageBoxResult.Yes)
        {
            if (!_vm.TrySave(out var member)) { e.Cancel = true; return; }
            SavedMember = member;
            DialogResult = true;
        }
        // "Не сохранять" — ничего не делаем, закрытие продолжается без сохранения.
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

    void ImportGeometryFromDxf_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new PlanarGeometryDxfImportDialog(_vm.FileDialogService) { Owner = this };
        if (dlg.ShowDialog() == true) _vm.ImportGeometryFromDxf(dlg.Hull!, dlg.Holes);
    }

    void FitRebarView_Click(object sender, System.Windows.RoutedEventArgs e) => rebarPreview.FitToView();

    void RebarFaceTab_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is System.Windows.Controls.TabItem { Tag: string tag })
            _vm.ActiveRebarFace = tag == "PlusN" ? CScore.PlateRebar.RebarFace.PlusN : CScore.PlateRebar.RebarFace.MinusN;
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

    void ImportRebarZonesFromDxf_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new RebarZoneDxfImportDialog(_vm.FileDialogService) { Owner = this };
        if (dlg.ShowDialog() == true) _vm.ImportDxfZones(dlg.AcceptedIncluded);
    }

    void ApplyZoneMaterial_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var zones = zonesList.SelectedItems.Cast<RebarZoneVM>().ToList();
        var material = zoneBatchMaterialCombo.SelectedItem as CScore.Material;
        _vm.ApplyMaterialToSelectedZones(zones, material);
    }

    void UpdateGeometryPlot()
    {
        var elements = _vm.GeometryPlotElements;
        var hull = _vm.Hull;
        UpdatePlot(preview, elements, hull);
    }

    void UpdateRebarPlot()
    {
        var elements = _vm.RebarPlotElements;
        var hull = _vm.Hull;
        UpdatePlot(rebarPreview, elements, hull);
    }

    static void UpdatePlot(PlanarRegionPreviewCanvas canvas, System.Collections.Generic.IReadOnlyList<PlotElement> elements, CScore.Contour? hull)
    {
        if (elements.Count == 0) { canvas.Clear(); return; }

        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;
        if (hull != null)
            for (int i = 0; i < hull.X.Count; i++)
            {
                if (hull.X[i] < xMin) xMin = hull.X[i];
                if (hull.X[i] > xMax) xMax = hull.X[i];
                if (hull.Y[i] < yMin) yMin = hull.Y[i];
                if (hull.Y[i] > yMax) yMax = hull.Y[i];
            }

        if (xMin > xMax) { canvas.Clear(); return; }
        if (xMax - xMin < 1e-9) { xMin -= 0.1; xMax += 0.1; }
        if (yMax - yMin < 1e-9) { yMin -= 0.1; yMax += 0.1; }

        canvas.SetElements(elements, xMin, xMax, yMin, yMax);
    }
}
