using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Structural;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.Views.Helpers;

namespace OpenCS.Views;

/// <summary>Хост 2D-эпюр усилий одного конструктивного стержня с выбором компоненты,
/// синхронизацией шага нагружения и маркерами точек интегрирования.</summary>
public partial class FemMemberForceView : UserControl
{
    /// <summary>Дуговые координаты mesh-элемента стержня.</summary>
    readonly record struct ElemArc(int Tag, double Si, double Sj);

    readonly List<ElemArc> _elements = [];
    readonly string _memberTag;
    readonly FemAnalysisResultVM _vm;
    FemMemberGeometryContext? _geometryContext;
    FemSectionLocationRow? _menuTargetRow;

    /// <summary>Выбранная группа результата в плоском просмотре.</summary>
    public FemResultGroup SelectedGroup { get; private set; } = FemResultGroup.Forces;

    public FemMemberForceView(DatabaseService db, FemSchema schema, string memberTag, FemAnalysisResultVM vm)
    {
        InitializeComponent();
        _memberTag = memberTag;
        _vm = vm;
        DataContext = vm;

        BuildElements(db, schema, memberTag);

        componentBox.ItemsSource = System.Enum.GetValues<FemForceComponent>();
        componentBox.SelectedItem = FemForceComponent.Mz;
        groupBox.ItemsSource = System.Enum.GetValues<FemResultGroup>();
        groupBox.SelectedItem = SelectedGroup;
        nodalComponentBox.ItemsSource = System.Enum.GetValues<FemNodalComponent>();
        nodalComponentBox.SelectedItem = FemNodalComponent.Ux;
        lengthUnitBox.ItemsSource = System.Enum.GetValues<FemLengthUnit>();
        lengthUnitBox.SelectedItem = _vm.DisplacementLengthUnit;
        rotationScaleBox.ItemsSource = System.Enum.GetValues<FemRotationScale>();
        rotationScaleBox.SelectedItem = _vm.RotationDisplayScale;
        UpdateGroupControls();

        canvas.MarkerClicked += OnMarkerClicked;
        canvas.MarkerContextMenuRequested += OnMarkerContextMenuRequested;
        _vm.PropertyChanged += OnVmPropertyChanged;
        Unloaded += (_, _) => _vm.PropertyChanged -= OnVmPropertyChanged;

        RefreshMarkers();
        RefreshDiagram();
    }

    void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FemAnalysisResultVM.SelectedStepIndex)
            or nameof(FemAnalysisResultVM.DisplacementLengthUnit)
            or nameof(FemAnalysisResultVM.RotationDisplayScale))
            RefreshDiagram();
    }

    void BuildElements(DatabaseService db, FemSchema schema, string memberTag)
    {
        var meshPos = new Dictionary<int, Point3D>();
        foreach (var n in db.GetFemMeshNodes(schema.Id))
            if (int.TryParse(n.NodeTag, out int t)) meshPos[t] = new Point3D(n.X, n.Y, n.Z);

        var (start, dir) = MemberAxis(db, schema, memberTag, meshPos);

        var geometryElements = new List<FemMemberMeshElement>();
        foreach (var e in db.GetFemMeshElements(schema.Id))
        {
            if (e.SourceMemberTag != memberTag) continue;
            if (!int.TryParse(e.ElemTag, out int etag)) continue;
            var ends = JsonSerializer.Deserialize<int[]>(e.NodeIdsJson) ?? [];
            if (ends.Length != 2 || !meshPos.TryGetValue(ends[0], out var pa) || !meshPos.TryGetValue(ends[1], out var pb))
                continue;
            double si = Vector3D.DotProduct(pa - start, dir);
            double sj = Vector3D.DotProduct(pb - start, dir);
            _elements.Add(new ElemArc(etag, si, sj));
            geometryElements.Add(new FemMemberMeshElement(etag, ends[0], ends[1], pa, pb));
        }
        _elements.Sort((a, b) => System.Math.Min(a.Si, a.Sj).CompareTo(System.Math.Min(b.Si, b.Sj)));
        _geometryContext = new FemMemberGeometryContext(memberTag, start, dir, geometryElements);
    }

    static (Point3D Start, Vector3D Dir) MemberAxis(
        DatabaseService db, FemSchema schema, string memberTag, Dictionary<int, Point3D> meshPos)
    {
        var member = db.GetFemMembers(schema.Id).FirstOrDefault(m => m.ElemTag == memberTag);
        var nodesByTag = new Dictionary<string, Point3D>();
        foreach (var n in db.GetFemNodes(schema.Id)) nodesByTag[n.NodeTag] = new Point3D(n.X, n.Y, n.Z);

        if (member?.Node1 is { } n1 && member.Node2 is { } n2 &&
            nodesByTag.TryGetValue(n1.ToString(), out var p1) && nodesByTag.TryGetValue(n2.ToString(), out var p2))
        {
            var d = p2 - p1;
            if (d.Length > 1e-9) { d.Normalize(); return (p1, d); }
        }
        if (meshPos.Count > 0)
        {
            var origin = meshPos.Values.First();
            var far = meshPos.Values.OrderByDescending(p => (p - origin).Length).First();
            var d = far - origin;
            if (d.Length > 1e-9) { d.Normalize(); return (origin, d); }
        }
        return (new Point3D(), new Vector3D(1, 0, 0));
    }

    void GroupBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (groupBox.SelectedItem is not FemResultGroup group) return;
        SelectedGroup = group;
        UpdateGroupControls();
        RefreshDiagram();
    }

    void ComponentBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshDiagram();

    void NodalComponentBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshDiagram();

    void LengthUnitBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lengthUnitBox.SelectedItem is FemLengthUnit unit)
            _vm.DisplacementLengthUnit = unit;
    }

    void RotationScaleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (rotationScaleBox.SelectedItem is FemRotationScale scale)
            _vm.RotationDisplayScale = scale;
    }

    void UpdateGroupControls()
    {
        bool forces = SelectedGroup == FemResultGroup.Forces;
        forceComponentPanel.Visibility = forces ? Visibility.Visible : Visibility.Collapsed;
        nodalComponentPanel.Visibility = forces ? Visibility.Collapsed : Visibility.Visible;
        lengthUnitPanel.Visibility = SelectedGroup == FemResultGroup.Displacements
            ? Visibility.Visible : Visibility.Collapsed;
        rotationScalePanel.Visibility = SelectedGroup == FemResultGroup.Rotations
            ? Visibility.Visible : Visibility.Collapsed;
    }

    void RefreshDiagram()
    {
        if (_geometryContext is null) return;

        var forcesByElem = _vm.ElementForces.ToDictionary(f => f.ElemTag);
        var displacementsByNode = _vm.Displacements.ToDictionary(d => d.NodeTag);
        FemDiagramSeries series = SelectedGroup switch
        {
            FemResultGroup.Forces when componentBox.SelectedItem is FemForceComponent forceComponent =>
                FemMemberResultSeriesBuilder.BuildForces(_geometryContext, forcesByElem, forceComponent),
            FemResultGroup.Displacements when nodalComponentBox.SelectedItem is FemNodalComponent displacementComponent =>
                FemMemberResultSeriesBuilder.BuildNodal(_geometryContext, displacementsByNode, displacementComponent),
            FemResultGroup.Rotations when nodalComponentBox.SelectedItem is FemNodalComponent rotationComponent =>
                FemMemberResultSeriesBuilder.BuildNodal(_geometryContext, displacementsByNode, rotationComponent),
            _ => FemDiagramSeries.Empty
        };
        FemDiagramSeries displaySeries = FemDiagramValueScaler.Scale(
            series, SelectedGroup, _vm.DisplacementLengthUnit, _vm.RotationDisplayScale);
        var segments = displaySeries.Segments
            .Select(segment => new FemMemberForceCanvas.Segment(
                segment.S0, segment.S1, segment.Value0, segment.Value1))
            .ToList();
        canvas.SetData(segments, BuildTitle());
    }

    string BuildTitle()
    {
        if (SelectedGroup == FemResultGroup.Forces)
        {
            var component = (FemForceComponent)componentBox.SelectedItem!;
            bool isForce = component is FemForceComponent.N or FemForceComponent.Qy or FemForceComponent.Qz;
            string unit = isForce ? Loc.S("UnitKN") : Loc.S("UnitKNm");
            return string.Format(Loc.S("FemMemberForceTitleWithUnit"), _memberTag, component, unit);
        }

        var nodalComponent = (FemNodalComponent)nodalComponentBox.SelectedItem!;
        string group = SelectedGroup == FemResultGroup.Displacements
            ? Loc.S("FemResultGroupDisplacements")
            : Loc.S("FemResultGroupRotations");
        string componentText = Loc.S($"FemResultNodal{nodalComponent}");
        string unitText = SelectedGroup == FemResultGroup.Displacements
            ? Loc.S($"FemLength{_vm.DisplacementLengthUnit}")
            : string.Format(Loc.S("FemRotationDisplayUnit"),
                Loc.S("FemUnitRad"), (int)_vm.RotationDisplayScale);
        return string.Format(Loc.S("FemMemberResultDiagramTitle"),
            _memberTag, group, componentText, unitText);
    }

    void RefreshMarkers()
    {
        var markers = new List<FemMemberForceCanvas.Marker>();
        foreach (var row in _vm.SectionLocations.Where(r => r.SourceMemberTag == _memberTag))
        {
            var el = _elements.FirstOrDefault(e => e.Tag == row.MeshElementTag);
            if (el.Tag != row.MeshElementTag) continue;
            double s = el.Si + (el.Sj - el.Si) * row.ElementLocalNormalized;
            markers.Add(new FemMemberForceCanvas.Marker(
                s, row.IsStateAvailable, row,
                string.Format(Loc.S("FemResultSectionIpMarker"), row.IntegrationPoint)));
        }
        canvas.SetMarkers(markers);
    }

    void OnMarkerClicked(object key)
    {
        if (key is FemSectionLocationRow row) canvas.SelectMarker(row);
    }

    void OnMarkerContextMenuRequested(object key)
    {
        if (key is not FemSectionLocationRow row) return;
        canvas.SelectMarker(row);
        _menuTargetRow = row;
        var item = new MenuItem
        {
            Header = Loc.S("FemMemberForceSectionState"),
            IsEnabled = row.IsStateAvailable
        };
        item.Click += OnShowSectionStateClick;
        var menu = new ContextMenu();
        menu.Items.Add(item);
        menu.PlacementTarget = canvas;
        menu.IsOpen = true;
    }

    void OnShowSectionStateClick(object sender, RoutedEventArgs e)
    {
        if (_menuTargetRow != null) _vm.RequestSectionState(_menuTargetRow);
    }

}
