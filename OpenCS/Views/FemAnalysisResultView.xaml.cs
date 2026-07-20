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
    MeshGeometryVisual3D? _forceRibbon;

    public FemAnalysisResultView(CalcResult result, AppViewModel app, FemSchema schema)
    {
        InitializeComponent();
        _vm = new FemAnalysisResultVM(result, app.db, schema);
        DataContext = _vm;
        BuildViewport();
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FemAnalysisResultVM.DeformedLines) && _deformed is not null)
            _deformed.Points = _vm.DeformedLines;
        else if (e.PropertyName == nameof(FemAnalysisResultVM.ForceDiagramMesh) && _forceRibbon is not null)
            _forceRibbon.MeshGeometry = _vm.ForceDiagramMesh;
    }

    void BuildViewport()
    {
        if (!_vm.HasGeometry) return;
        viewport.Children.Add(new DefaultLights());
        viewport.Children.Add(new LinesVisual3D { Color = Colors.Gray, Thickness = 1, Points = _vm.OriginalLines });
        _deformed = new LinesVisual3D { Color = Colors.SteelBlue, Thickness = 2, Points = _vm.DeformedLines };
        viewport.Children.Add(_deformed);
        _forceRibbon = new MeshGeometryVisual3D
        {
            MeshGeometry = _vm.ForceDiagramMesh,
            Fill = new SolidColorBrush(Color.FromArgb(140, 0xE8, 0x7A, 0x00))
        };
        viewport.Children.Add(_forceRibbon);
        viewport.ZoomExtents();
    }
}
