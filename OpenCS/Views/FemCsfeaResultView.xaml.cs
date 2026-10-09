using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>
/// Результат расчёта схемы CSfea: 3D (деформированная схема, мозаика пластин, эпюры стержней), графики «λ по шагам» и
/// «λ — перемещение контрольного узла», журнал шагов и отчёт; ползунок шага.
/// </summary>
public partial class FemCsfeaResultView : UserControl
{
    /// <summary>Тон ленты эпюры — как в результате OpenSees.</summary>
    static readonly Color ForceRibbonColor = Color.FromArgb(90, 0x4D, 0xB6, 0xAC);

    readonly FemCsfeaResultVM _vm;
    LinesVisual3D? _deformed;
    readonly ModelVisual3D _shells = new();
    readonly Dictionary<GeometryModel3D, FemCsfeaResultVM.ShellPatch> _patchByModel = new();
    MeshGeometryVisual3D? _forceRibbon;
    BillboardTextVisual3D? _forceMax, _forceMin;
    bool _syncingGrid;

    public FemCsfeaResultView(FemCsfeaResultVM vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        reportBox.Text = string.Join(Environment.NewLine, vm.ReportLines);
        BuildViewport();
        UpdateCharts();
        lambdaCanvas.StepClicked += i => _vm.SelectedStepIndex = i;
        controlCanvas.StepClicked += i => _vm.SelectedStepIndex = i;
        viewport.MouseMove += Viewport_MouseMove;
        viewport.MouseLeave += (_, _) => _vm.SetHover(null);
        _vm.PropertyChanged += OnVmPropertyChanged;
        SyncGrid();
    }

    void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FemCsfeaResultVM.SelectedStepIndex):
                UpdateCharts();
                SyncGrid();
                break;
            case nameof(FemCsfeaResultVM.DeformedLines) when _deformed != null:
                _deformed.Points = _vm.DeformedLines;
                break;
            case nameof(FemCsfeaResultVM.ShellPatches):
                UpdateShells();
                break;
            case nameof(FemCsfeaResultVM.ForceDiagramMesh) when _forceRibbon != null:
                _forceRibbon.MeshGeometry = _vm.ForceDiagramMesh;
                break;
            case nameof(FemCsfeaResultVM.ForceMaxLabelText):
                UpdateForceLabels();
                break;
        }
    }

    void BuildViewport()
    {
        if (!_vm.HasGeometry) return;
        viewport.Children.Add(new DefaultLights());
        viewport.Children.Add(new LinesVisual3D { Color = Colors.Gray, Thickness = 1, Points = _vm.OriginalLines });
        viewport.Children.Add(_shells);
        _deformed = new LinesVisual3D { Color = Colors.SteelBlue, Thickness = 1.5, Points = _vm.DeformedLines };
        viewport.Children.Add(_deformed);
        _forceRibbon = new MeshGeometryVisual3D { MeshGeometry = _vm.ForceDiagramMesh, Fill = new SolidColorBrush(ForceRibbonColor) };
        viewport.Children.Add(_forceRibbon);
        _forceMax = new BillboardTextVisual3D { Foreground = Brushes.Black, Background = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 12 };
        _forceMin = new BillboardTextVisual3D { Foreground = Brushes.Black, Background = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 12 };
        viewport.Children.Add(_forceMax);
        viewport.Children.Add(_forceMin);
        UpdateShells();
        UpdateForceLabels();
        viewport.ZoomExtents();
    }

    void UpdateShells()
    {
        var group = new Model3DGroup();
        _patchByModel.Clear();
        foreach (var patch in _vm.ShellPatches)
        {
            var brush = new SolidColorBrush(patch.Color);
            brush.Freeze();
            var material = new DiffuseMaterial(brush);
            var model = new GeometryModel3D(patch.Mesh, material) { BackMaterial = material };
            group.Children.Add(model);
            _patchByModel[model] = patch;
        }
        _shells.Content = group;
    }

    void UpdateForceLabels()
    {
        if (_forceMax == null || _forceMin == null) return;
        _forceMax.Text = _vm.ForceMaxLabelText ?? "";
        _forceMin.Text = _vm.ForceMinLabelText ?? "";
        if (_vm.ForceMaxLabelPosition is { } a) _forceMax.Position = a;
        if (_vm.ForceMinLabelPosition is { } b) _forceMin.Position = b;
    }

    void UpdateCharts()
    {
        lambdaCanvas.SetData(_vm.LambdaPoints, _vm.SelectedStepIndex);
        controlCanvas.SetData(_vm.ControlPoints, _vm.SelectedStepIndex);
    }

    void SyncGrid()
    {
        _syncingGrid = true;
        try
        {
            if (_vm.SelectedStepIndex < _vm.StepRows.Count)
            {
                stepsGrid.SelectedIndex = _vm.SelectedStepIndex;
                if (stepsGrid.SelectedItem != null) stepsGrid.ScrollIntoView(stepsGrid.SelectedItem);
            }
        }
        finally { _syncingGrid = false; }
    }

    void StepsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingGrid && stepsGrid.SelectedIndex >= 0) _vm.SelectedStepIndex = stepsGrid.SelectedIndex;
    }

    /// <summary>Значение мозаики под курсором — в легенду.</summary>
    void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_vm.IsActive) return;
        int? elem = null;
        var hit = VisualTreeHelper.HitTest(viewport.Viewport, e.GetPosition(viewport.Viewport)) as RayMeshGeometry3DHitTestResult;
        if (hit?.ModelHit is GeometryModel3D model && _patchByModel.TryGetValue(model, out var patch))
            elem = FemCsfeaResultVM.ElementOf(patch, hit.VertexIndex1);
        _vm.SetHover(elem);
    }
}
