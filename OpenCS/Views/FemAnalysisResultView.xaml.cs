using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using CScore;
using CScore.Fem;
using HelixToolkit.Wpf;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Результатная вкладка линейного OpenSees-расчёта FEM-схемы: 3D-деформация и таблицы.</summary>
public partial class FemAnalysisResultView : UserControl
{
    readonly FemAnalysisResultVM _vm;
    LinesVisual3D? _deformed;
    PointsVisual3D? _nodesVisual;
    MeshGeometryVisual3D? _forceRibbon;

    public FemAnalysisResultView(FemAnalysisResultVM vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;
        BuildViewport();
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FemAnalysisResultVM.DeformedLines) && _deformed is not null)
        {
            _deformed.Points = _vm.DeformedLines;
        }
        else if (e.PropertyName == nameof(FemAnalysisResultVM.DeformedNodes) && _nodesVisual is not null)
        {
            _nodesVisual.Points = showNodesCheck.IsChecked == true ? _vm.DeformedNodes : null;
        }
        else if (e.PropertyName == nameof(FemAnalysisResultVM.ForceDiagramMesh) && _forceRibbon is not null)
        {
            _forceRibbon.MeshGeometry = _vm.ForceDiagramMesh;
        }
    }

    void NodesToggle(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_nodesVisual != null && sender is CheckBox cb)
            _nodesVisual.Points = cb.IsChecked == true ? _vm.DeformedNodes : null;
    }

    void BuildViewport()
    {
        if (!_vm.HasGeometry) return;
        viewport.Children.Add(new DefaultLights());
        viewport.Children.Add(new LinesVisual3D { Color = Colors.Gray, Thickness = 1, Points = _vm.OriginalLines });
        _deformed = new LinesVisual3D { Color = Colors.SteelBlue, Thickness = 2, Points = _vm.DeformedLines };
        viewport.Children.Add(_deformed);
        _nodesVisual = new PointsVisual3D { Color = Colors.DarkSlateGray, Size = 5, Points = _vm.DeformedNodes };
        viewport.Children.Add(_nodesVisual);
        _forceRibbon = new MeshGeometryVisual3D
        {
            MeshGeometry = _vm.ForceDiagramMesh,
            Fill = new SolidColorBrush(Color.FromArgb(140, 0xE8, 0x7A, 0x00))
        };
        viewport.Children.Add(_forceRibbon);
        viewport.ZoomExtents();
    }

    string? _contextMenuTargetTag;
    string? _contextMenuTargetKind;

    HelixToolkit.Wpf.Ray3D? GetRay(System.Windows.Point pt)
    {
        if (viewport.Camera is not System.Windows.Media.Media3D.ProjectionCamera) return null;
        var m = HelixToolkit.Wpf.Viewport3DHelper.GetTotalTransform(viewport.Viewport);
        if (!m.HasInverse) return null;
        m.Invert();
        var w = viewport.ActualWidth;
        var h = viewport.ActualHeight;
        if (w == 0 || h == 0) return null;
        var p0 = new System.Windows.Media.Media3D.Point3D(pt.X / w * 2 - 1, -(pt.Y / h * 2 - 1), 0);
        var p1 = new System.Windows.Media.Media3D.Point3D(pt.X / w * 2 - 1, -(pt.Y / h * 2 - 1), 1);
        var p0w = m.Transform(p0);
        var p1w = m.Transform(p1);
        return new HelixToolkit.Wpf.Ray3D(p0w, p1w - p0w);
    }

    void Viewport_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var pt = e.GetPosition(viewport);
        var ray = GetRay(pt);
        if (ray == null) return;
        
        var hit = _vm.HitTest(ray.Origin, ray.Direction);
        if (hit == null) return;

        // Если попали по узлу или элементу, блокируем вращение камеры (которое HelixToolkit делает по правому клику)
        e.Handled = true;

        _contextMenuTargetKind = hit.Value.Kind;
        _contextMenuTargetTag = hit.Value.Tag;
        
        var menu = (ContextMenu)Resources[_contextMenuTargetKind == "Node" ? "ResultNodeContextMenu" : "ResultMemberContextMenu"];
        menu.PlacementTarget = viewport;
        menu.IsOpen = true;
        e.Handled = true;
    }

    void MemberShow2DCtx_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_contextMenuTargetTag != null) _vm.RequestShowMemberForce(_contextMenuTargetTag);
    }

    void MemberSectionCtx_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_contextMenuTargetTag != null) _vm.RequestGoToSection(_contextMenuTargetTag);
    }

    void NodeValuesCtx_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_contextMenuTargetTag != null) _vm.RequestShowNodeValues(_contextMenuTargetTag);
    }
}
