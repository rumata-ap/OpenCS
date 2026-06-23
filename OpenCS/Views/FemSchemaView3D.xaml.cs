using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemSchemaView3D : UserControl
{
    Fem3DVM? VM => DataContext as Fem3DVM;

    Fem3DVM?        _activeVm;
    PointsVisual3D? _nodesVisual;
    LinesVisual3D?  _shellEdgesVisual;

    public FemSchemaView3D()
    {
        InitializeComponent();
        Loaded             += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (VM is { } vm) await ActivateVmAsync(vm);
    }

    async void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded) return; // OnLoaded обработает при первом показе
        if (e.NewValue is Fem3DVM vm && vm != _activeVm)
            await ActivateVmAsync(vm);
    }

    async Task ActivateVmAsync(Fem3DVM vm)
    {
        if (_activeVm != null)
            _activeVm.PropertyChanged -= OnVMPropertyChanged;

        _activeVm         = vm;
        _nodesVisual      = null;
        _shellEdgesVisual = null;
        viewport.Children.Clear();

        vm.PropertyChanged += OnVMPropertyChanged;
        await vm.LoadAsync();
    }

    void OnVMPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Fem3DVM.IsLoading) && VM is { IsLoading: false })
            BuildVisuals();
    }

    void BuildVisuals()
    {
        viewport.Children.Clear();
        viewport.Children.Add(new DefaultLights());

        if (VM == null) return;

        foreach (var group in VM.BarGroups)
        {
            viewport.Children.Add(new LinesVisual3D
            {
                Points    = group.Points,
                Color     = group.Color,
                Thickness = group.Thickness,
            });
        }

        bool isHighlight = VM.HiShellMesh != null;

        if (VM.ShellMesh is { } bgMesh)
        {
            var color = isHighlight ? Fem3DVM.ShellBgColor : Fem3DVM.ShellColor;
            var mat   = new DiffuseMaterial(new SolidColorBrush(color));
            var model = new GeometryModel3D(bgMesh, mat) { BackMaterial = mat };
            viewport.Children.Add(new ModelVisual3D { Content = model });
        }

        if (VM.HiShellMesh is { } hiMesh)
        {
            var mat   = new DiffuseMaterial(new SolidColorBrush(Fem3DVM.ShellHiColor));
            var model = new GeometryModel3D(hiMesh, mat) { BackMaterial = mat };
            viewport.Children.Add(new ModelVisual3D { Content = model });
        }

        _shellEdgesVisual = VM.ShellEdgePoints is { Count: > 0 } edgePts
            ? new LinesVisual3D { Points = edgePts, Color = Colors.DimGray, Thickness = 0.5 }
            : null;

        if (showShellEdgesCheck.IsChecked == true && _shellEdgesVisual != null)
            viewport.Children.Add(_shellEdgesVisual);

        _nodesVisual = VM.NodePoints is { Count: > 0 } nodePts
            ? new PointsVisual3D { Points = nodePts, Color = Colors.DimGray, Size = 3 }
            : null;

        if (showNodesCheck.IsChecked == true && _nodesVisual != null)
            viewport.Children.Add(_nodesVisual);

        if (VM.BarGroups.Count > 0 || VM.ShellMesh != null)
            viewport.ZoomExtents(500);
    }

    void ShellEdgesToggle(object sender, RoutedEventArgs e)
    {
        if (_shellEdgesVisual == null) return;
        if (showShellEdgesCheck.IsChecked == true)
            viewport.Children.Add(_shellEdgesVisual);
        else
            viewport.Children.Remove(_shellEdgesVisual);
    }

    void NodesToggle(object sender, RoutedEventArgs e)
    {
        if (_nodesVisual == null) return;
        if (showNodesCheck.IsChecked == true)
            viewport.Children.Add(_nodesVisual);
        else
            viewport.Children.Remove(_nodesVisual);
    }

    void ZoomExtents_Click(object sender, RoutedEventArgs e)
        => viewport.ZoomExtents(500);
}
