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
            if (e.PropertyName == nameof(PlanarRegionMemberVM.ShowMesh))
                meshToggleButton.IsChecked = _vm.ShowMesh;
            if (e.PropertyName == nameof(PlanarRegionMemberVM.StripPlotElements))
                UpdateStripPlot();
            if (e.PropertyName == nameof(PlanarRegionMemberVM.IsDrawingZone))
                rebarPreview.IsPicking = _vm.IsDrawingZone;
            if (e.PropertyName == nameof(PlanarRegionMemberVM.IsPickingStripAxis))
                stripPreview.IsPicking = _vm.IsPickingStripAxis;
        };
        _vm.SaveCompleted += m => { SavedMember = m; DialogResult = true; Close(); };
        _vm.DeleteCompleted += m => { DeletedMember = m; DialogResult = true; Close(); };

        rebarPreview.ModelClicked += (x, y) => _vm.OnRebarCanvasClick(x, y, rebarPreview.ModelUnitsPerPixel);
        rebarPreview.ModelDoubleClicked += (_, _) => _vm.FinishZoneDrawing();
        rebarPreview.ModelHover += (x, y) =>
        {
            if (_vm.IsDrawingZone) _vm.HoverDrawing(x, y, rebarPreview.ModelUnitsPerPixel);
            else _vm.HoverMosaicAt(x, y);
        };
        stripPreview.ModelClicked += (x, y) => _vm.OnStripCanvasClick(x, y, stripPreview.ModelUnitsPerPixel);
        stripPreview.ModelHover += (x, y) => _vm.OnStripCanvasHover(x, y, stripPreview.ModelUnitsPerPixel);

        PreviewKeyDown += Window_PreviewKeyDown;

        UpdateGeometryPlot();
        UpdateRebarPlot();
    }

    void MainTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Событие всплывает из вложенных TabControl (грани армирования) — нужен только переход главных вкладок.
        if (_vm is null || !ReferenceEquals(e.OriginalSource, mainTabs)) return;
        if (mainTabs.SelectedItem is System.Windows.Controls.TabItem { Tag: "strip" })
        {
            _vm.EnsureStripInitialized();
            UpdateStripPlot();
        }
        else
        {
            _vm.CancelZoneDrawing();
            _vm.CancelStripAxisPicking();
            drawZoneToggle.IsChecked = false;
        }
    }

    void DrawZone_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        CommitFocusedTextBox();
        if (_vm.IsDrawingZone) _vm.FinishZoneDrawing();
        else _vm.BeginZoneDrawing();
        drawZoneToggle.IsChecked = _vm.IsDrawingZone;
    }

    void FitStripView_Click(object sender, System.Windows.RoutedEventArgs e) => stripPreview.FitToView();

    /// <summary>Enter — завершить контур зоны, Backspace — убрать вершину, Esc — отменить рисование/указание оси.
    /// Клавиши не перехватываются, пока фокус в поле ввода.</summary>
    void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;
        switch (e.Key)
        {
            case System.Windows.Input.Key.Enter when _vm.IsDrawingZone:
                _vm.FinishZoneDrawing();
                drawZoneToggle.IsChecked = _vm.IsDrawingZone;
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Back when _vm.IsDrawingZone:
                _vm.UndoDrawingPoint();
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Escape when _vm.IsDrawingZone:
                _vm.CancelZoneDrawing();
                drawZoneToggle.IsChecked = false;
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Escape when _vm.IsPickingStripAxis:
                _vm.CancelStripAxisPicking();
                e.Handled = true;
                break;
        }
    }

    void Window_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Owner != null)
        {
            Width = Owner.ActualWidth;
            Height = Owner.ActualHeight;
        }
        _vm.ShowSavedMesh();
    }

    /// <summary>Поля пишут значение при потере фокуса, а кнопки панели инструментов фокус не забирают — перед их
    /// действием значение поля в фокусе передаётся явно (иначе новый размер КЭ не доходит до построения).</summary>
    static void CommitFocusedTextBox()
    {
        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox box)
            box.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
    }

    void CommitOnEnter(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && sender is System.Windows.Controls.TextBox box)
            box.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
    }

    void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        CommitFocusedTextBox();
        bool rebarDirty = _vm.HasZones && _vm.RebarLayoutDirty;
        if (!rebarDirty && !_vm.MeshSizeDirty) return;

        string prompt = rebarDirty
            ? Loc.S("PlanarRegionRebarUnsavedChangesPrompt")
            : Loc.S("PlanarRegionUnsavedChangesPrompt");
        var result = System.Windows.MessageBox.Show(
            prompt, Loc.S("Warning"),
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

    async void ToggleMesh_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        meshToggleButton.IsChecked = _vm.ShowMesh; // не даём WPF самостоятельно переключить состояние — им управляет VM
        CommitFocusedTextBox();
        await _vm.ToggleMeshAsync();
    }

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

    void UpdateStripPlot() => UpdatePlot(stripPreview, _vm.StripPlotElements, _vm.Hull);

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
