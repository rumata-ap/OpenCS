using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CScore;
using CScore.Fem;
using HelixToolkit.Wpf;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Structural;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Результатная вкладка линейного OpenSees-расчёта FEM-схемы: 3D-деформация и таблицы.</summary>
public partial class FemAnalysisResultView : UserControl
{
    /// <summary>Порог, после которого picking по отдельным узлам/элементам отключается (как в
    /// FemSchemaView3D) — на больших моделях O(N) Visual3D вешают UI.</summary>
    const int PickTargetThreshold = 500;

    // Радиус/диаметр pick-целей считаются от средней длины mesh-сегмента (см. BuildPickTargets),
    // а не берутся фиксированными: на густой сетке (короткие сегменты) фиксированный размер делает
    // сферу узла крупнее самого сегмента — стержень физически невозможно кликнуть между узлами.
    const double NodePickRadiusFactor = 0.12;
    const double NodePickRadiusMin = 0.01;
    const double NodePickRadiusMax = 0.5;
    const double ElemPickDiameterFactor = 0.05;
    const double ElemPickDiameterMin = 0.005;
    const double ElemPickDiameterMax = 0.2;

    // Уточнение относительно спеки: там описана пара «видимая маленькая сфера + невидимая
    // крупнее» по образцу FemSchemaView3D. Здесь этого не нужно — узлы уже рисует отдельный
    // PointsVisual3D (_nodesVisual, см. BuildViewport), поэтому единственная сфера на pick
    // target достаточна: Transparent в обычном состоянии (только для picking), OrangeRed при
    // выборе (подсветка). Так же, как уже сделано для элементов (PipeVisual3D).

    readonly FemAnalysisResultVM _vm;
    LinesVisual3D? _deformed;
    PointsVisual3D? _nodesVisual;
    MeshGeometryVisual3D? _forceRibbon;
    BillboardTextVisual3D? _forceMaxLabel;
    BillboardTextVisual3D? _forceMinLabel;

    sealed record PickTarget(bool IsNode, int Tag, FemSectionLocationRow? SectionRow);

    readonly Dictionary<Visual3D, PickTarget> _pickTargets = new();
    readonly Dictionary<int, SphereVisual3D> _nodeSpheresByTag = new();
    readonly Dictionary<int, PipeVisual3D> _elemPipesByTag = new();
    PointsVisual3D? _ipAvailableVisual;
    PointsVisual3D? _ipUnavailableVisual;
    readonly Dictionary<FemSectionLocationRow, SphereVisual3D> _ipPickSpheresByRow = new();

    bool _highlightWholeMember = true;

    public FemAnalysisResultView(FemAnalysisResultVM vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;
        displacementModeBox.ItemsSource = System.Enum.GetValues<FemDisplacementDisplayMode>();
        displacementModeBox.SelectedItem = _vm.DisplacementDisplayMode;
        displacementLengthUnitBox.ItemsSource = System.Enum.GetValues<FemLengthUnit>();
        displacementLengthUnitBox.SelectedItem = _vm.DisplacementLengthUnit;
        rotationDisplayScaleBox.ItemsSource = System.Enum.GetValues<FemRotationScale>();
        rotationDisplayScaleBox.SelectedItem = _vm.RotationDisplayScale;
        UpdateDisplacementTableColumns();
        BuildViewport();
        BuildPickTargets();
        BuildLoadFactorCanvas();
        loadFactorCanvas.StepClicked += idx => _vm.SelectedStepIndex = idx;
        controlDispCanvas.StepClicked += idx => _vm.SelectedStepIndex = idx;
        _vm.PropertyChanged += OnVmPropertyChanged;
        BuildIpMarkers();
        viewport.MouseLeftButtonDown += Viewport_MouseLeftButtonDown;
    }

    void BuildLoadFactorCanvas()
    {
        loadFactorCanvas.SetData(_vm.LoadFactorPoints.Select(p => ((double)p.Step, p.LoadFactor, p.Converged, 0)).ToList(), _vm.SelectedStepIndex);
        controlDispCanvas.SetData(_vm.ControlDisplacementPoints, _vm.SelectedStepIndex);
    }

    void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FemAnalysisResultVM.SelectedStepIndex))
        {
            BuildLoadFactorCanvas();
        }
        else if (e.PropertyName == nameof(FemAnalysisResultVM.DeformedLines) && _deformed is not null)
        {
            _deformed.Points = _vm.DeformedLines;
        }
        else if (e.PropertyName is nameof(FemAnalysisResultVM.ShowDeformedSchema)
            or nameof(FemAnalysisResultVM.ShowDeformedNodes))
        {
            UpdateDeformedVisibility();
        }
        else if (e.PropertyName == nameof(FemAnalysisResultVM.DeformedNodes) && _nodesVisual is not null)
        {
            _nodesVisual.Points = _vm.DeformedNodes;
        }
        else if (e.PropertyName == nameof(FemAnalysisResultVM.ForceDiagramMesh) && _forceRibbon is not null)
        {
            _forceRibbon.MeshGeometry = _vm.ForceDiagramMesh;
        }
        else if (e.PropertyName == nameof(FemAnalysisResultVM.ForceMaxLabelText))
        {
            UpdateForceLabels();
        }
        else if (e.PropertyName == nameof(FemAnalysisResultVM.DeformedElementSegments))
        {
            BuildPickTargets();
        }
        else if (e.PropertyName == nameof(FemAnalysisResultVM.SectionMarkers))
        {
            BuildIpMarkers();
        }
        else if (e.PropertyName == nameof(FemAnalysisResultVM.SelectedSectionLocation))
        {
            UpdateIpHighlight();
        }
        else if (e.PropertyName is nameof(FemAnalysisResultVM.SelectedNodeTag) or nameof(FemAnalysisResultVM.SelectedElemTag))
        {
            UpdateSelectionHighlight();
        }
        else if (e.PropertyName is nameof(FemAnalysisResultVM.SelectedDisplayedDisplacementRow)
            or nameof(FemAnalysisResultVM.SelectedDisplacementRow))
        {
            if (_vm.SelectedDisplayedDisplacementRow is { } dispRow)
                displacementsGrid.ScrollIntoView(dispRow);
        }
        else if (e.PropertyName is nameof(FemAnalysisResultVM.DisplayedDisplacements)
            or nameof(FemAnalysisResultVM.DisplacementDisplayMode)
            or nameof(FemAnalysisResultVM.DisplacementLengthUnit)
            or nameof(FemAnalysisResultVM.RotationDisplayScale))
        {
            UpdateDisplacementTableColumns();
        }
        else if (e.PropertyName == nameof(FemAnalysisResultVM.SelectedReactionRow) && _vm.SelectedReactionRow is { } reactRow)
        {
            reactionsGrid.ScrollIntoView(reactRow);
        }
        else if (e.PropertyName == nameof(FemAnalysisResultVM.SelectedForceRow) && _vm.SelectedForceRow is { } forceRow)
        {
            elementForcesGrid.ScrollIntoView(forceRow);
        }
    }

    void NodesToggle(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is ToggleButton tb && tb.IsChecked is bool isChecked)
            _vm.ShowDeformedNodes = isChecked;
        UpdateDeformedVisibility();
    }

    void DeformedVisibilityToggle(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is ToggleButton tb && tb.IsChecked is bool isChecked)
            _vm.ShowDeformedSchema = isChecked;
        UpdateDeformedVisibility();
    }

    void UpdateDeformedVisibility()
    {
        if (_deformed is not null)
        {
            bool attached = viewport.Children.Contains(_deformed);
            if (_vm.ShowDeformedSchema && !attached) viewport.Children.Add(_deformed);
            else if (!_vm.ShowDeformedSchema && attached) viewport.Children.Remove(_deformed);
        }
        if (_nodesVisual is not null)
        {
            bool attached = viewport.Children.Contains(_nodesVisual);
            if (_vm.ShowDeformedNodes && !attached) viewport.Children.Add(_nodesVisual);
            else if (!_vm.ShowDeformedNodes && attached) viewport.Children.Remove(_nodesVisual);
        }
    }

    void HighlightWholeMemberToggle(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not ToggleButton tb) return;
        _highlightWholeMember = tb.IsChecked == true;
        UpdateSelectionHighlight();
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
        UpdateDeformedVisibility();
        _forceRibbon = new MeshGeometryVisual3D
        {
            MeshGeometry = _vm.ForceDiagramMesh,
            Fill = new SolidColorBrush(ForceRibbonColor)
        };
        viewport.Children.Add(_forceRibbon);

        _forceMaxLabel = new BillboardTextVisual3D
        {
            Foreground = Brushes.Black, Background = Brushes.White,
            FontWeight = FontWeights.Bold, FontSize = 12
        };
        _forceMinLabel = new BillboardTextVisual3D
        {
            Foreground = Brushes.Black, Background = Brushes.White,
            FontWeight = FontWeights.Bold, FontSize = 12
        };
        viewport.Children.Add(_forceMaxLabel);
        viewport.Children.Add(_forceMinLabel);
        UpdateForceLabels();

        viewport.ZoomExtents();
    }

    /// <summary>Лёгкий полупрозрачный тон ленты эпюры — отличается от SteelBlue (деформация)
    /// и от OrangeRed (подсветка выбора), чтобы не сливаться на глаз.</summary>
    static readonly Color ForceRibbonColor = Color.FromArgb(90, 0x4D, 0xB6, 0xAC);

    void UpdateForceLabels()
    {
        if (_forceMaxLabel != null)
        {
            if (_vm.ForceMaxLabelPosition is { } maxPos && _vm.ForceMaxLabelText is { } maxText)
            {
                _forceMaxLabel.Position = maxPos;
                _forceMaxLabel.Text = maxText;
            }
            else _forceMaxLabel.Text = "";
        }
        if (_forceMinLabel != null)
        {
            if (_vm.ForceMinLabelPosition is { } minPos && _vm.ForceMinLabelText is { } minText)
            {
                _forceMinLabel.Position = minPos;
                _forceMinLabel.Text = minText;
            }
            else _forceMinLabel.Text = "";
        }
    }

    string? _contextMenuTargetTag;
    FemSectionLocationRow? _contextMenuSectionRow;

    void BuildPickTargets()
    {
        foreach (var v in _pickTargets.Keys) viewport.Children.Remove(v);
        _pickTargets.Clear();
        _nodeSpheresByTag.Clear();
        _elemPipesByTag.Clear();

        if (!_vm.HasGeometry) return;
        if (_vm.DeformedNodesByTag.Count > PickTargetThreshold) return;

        double avgSegmentLength = _vm.DeformedElementSegments.Count > 0
            ? _vm.DeformedElementSegments.Average(s => (s.P1 - s.P0).Length)
            : 1.0;
        double nodeRadius = System.Math.Clamp(avgSegmentLength * NodePickRadiusFactor, NodePickRadiusMin, NodePickRadiusMax);
        double elemDiameter = System.Math.Clamp(avgSegmentLength * ElemPickDiameterFactor, ElemPickDiameterMin, ElemPickDiameterMax);

        foreach (var (tag, pos) in _vm.DeformedNodesByTag)
        {
            var sphere = new SphereVisual3D
            {
                Center = pos, Radius = nodeRadius,
                Fill = new SolidColorBrush(IsNodeHighlighted(tag) ? Colors.OrangeRed : Colors.Transparent)
            };
            _pickTargets[sphere] = new PickTarget(true, tag, null);
            _nodeSpheresByTag[tag] = sphere;
            viewport.Children.Add(sphere);
        }

        foreach (var (tag, p0, p1) in _vm.DeformedElementSegments)
        {
            var pipe = new PipeVisual3D
            {
                Point1 = p0, Point2 = p1, Diameter = elemDiameter,
                Fill = new SolidColorBrush(IsElemHighlighted(tag) ? Colors.OrangeRed : Colors.Transparent)
            };
            _pickTargets[pipe] = new PickTarget(false, tag, null);
            _elemPipesByTag[tag] = pipe;
            viewport.Children.Add(pipe);
        }
    }

    bool IsNodeHighlighted(int tag) => _vm.SelectedNodeTag == tag;

    /// <summary>В режиме «по конструктивному стержню» подсвечивает все mesh-сегменты одного стержня;
    /// в mesh-режиме — только сам выбранный сегмент. Идентичность выбора (SelectedElemTag) режим не меняет.</summary>
    bool IsElemHighlighted(int tag)
    {
        if (_vm.SelectedElemTag is not int selected) return false;
        if (!_highlightWholeMember) return selected == tag;
        return _vm.ResolveMemberTag(tag) == _vm.ResolveMemberTag(selected);
    }

    void UpdateSelectionHighlight()
    {
        foreach (var (tag, sphere) in _nodeSpheresByTag)
            sphere.Fill = new SolidColorBrush(IsNodeHighlighted(tag) ? Colors.OrangeRed : Colors.Transparent);
        foreach (var (tag, pipe) in _elemPipesByTag)
            pipe.Fill = new SolidColorBrush(IsElemHighlighted(tag) ? Colors.OrangeRed : Colors.Transparent);
    }

    (bool IsNode, int Tag, FemSectionLocationRow? SectionRow)? HitTestPick(System.Windows.Point position)
    {
        (bool IsNode, int Tag, FemSectionLocationRow? SectionRow)? hit = null;
        double bestDistance = double.MaxValue;
        HitTestResultBehavior Callback(HitTestResult result)
        {
            // Перебираем все попадания и берём ближайшее к камере по лучу — иначе при плотной
            // сетке порядок узел/элемент в дереве визуалов (не расстояние) решал бы, что выбрано.
            if (result is RayHitTestResult rayHit &&
                _pickTargets.TryGetValue(rayHit.VisualHit, out var target) &&
                rayHit.DistanceToRayOrigin < bestDistance)
            {
                bestDistance = rayHit.DistanceToRayOrigin;
                hit = (target.IsNode, target.Tag, target.SectionRow);
            }
            return HitTestResultBehavior.Continue;
        }
        VisualTreeHelper.HitTest(viewport, null, Callback, new PointHitTestParameters(position));
        return hit;
    }

    void Viewport_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var hit = HitTestPick(e.GetPosition(viewport));
        if (hit is not { } target) { _vm.SelectNode(null); return; }

        if (target.SectionRow is { } row)
        {
            _vm.SelectSectionLocation(row);
            return;
        }

        if (target.IsNode)
            _vm.SelectNode(_vm.SelectedNodeTag == target.Tag ? null : target.Tag);
        else
            _vm.SelectElement(_vm.SelectedElemTag == target.Tag ? null : target.Tag);
    }

    void Viewport_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var hit = HitTestPick(e.GetPosition(viewport));
        if (hit is not { } target) return;

        if (target.SectionRow is { } row)
        {
            // Попадание по маркеру ТИ — блокируем вращение камеры и открываем контекстное меню.
            e.Handled = true;
            _vm.SelectSectionLocation(row);
            _contextMenuSectionRow = row;
            var sectionMenu = (ContextMenu)Resources["ResultSectionContextMenu"];
            ((MenuItem)sectionMenu.Items[0]).IsEnabled = row.IsStateAvailable;
            sectionMenu.PlacementTarget = viewport;
            sectionMenu.IsOpen = true;
            return;
        }

        // Попадание по узлу/элементу — блокируем вращение камеры (стандартный жест HelixToolkit по ПКМ).
        e.Handled = true;

        if (target.IsNode)
        {
            _vm.SelectNode(target.Tag);
            _contextMenuTargetTag = target.Tag.ToString();
        }
        else
        {
            _vm.SelectElement(target.Tag);
            _contextMenuTargetTag = _vm.ResolveMemberTag(target.Tag);
        }

        var menu = (ContextMenu)Resources[target.IsNode ? "ResultNodeContextMenu" : "ResultMemberContextMenu"];
        menu.PlacementTarget = viewport;
        menu.IsOpen = true;
    }

    void MemberShow2DCtx_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_contextMenuTargetTag != null) _vm.RequestShowMemberForce(_contextMenuTargetTag);
    }

    void MemberSectionCtx_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_contextMenuTargetTag != null) _vm.RequestShowMemberSection(_contextMenuTargetTag);
    }

    void NodeValuesCtx_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_contextMenuTargetTag != null) _vm.RequestShowNodeValues(_contextMenuTargetTag);
    }

    void SectionStateCtx_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_contextMenuSectionRow != null) _vm.RequestSectionState(_contextMenuSectionRow);
    }

    void DisplacementsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is FemNodeDisplacementRow row)
            _vm.SelectNode(row.NodeTag);
    }

    void DisplacementModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (displacementModeBox.SelectedItem is FemDisplacementDisplayMode mode)
            _vm.DisplacementDisplayMode = mode;
    }

    void DisplacementLengthUnitBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (displacementLengthUnitBox.SelectedItem is FemLengthUnit unit)
            _vm.DisplacementLengthUnit = unit;
    }

    void RotationDisplayScaleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (rotationDisplayScaleBox.SelectedItem is FemRotationScale scale)
            _vm.RotationDisplayScale = scale;
    }

    void UpdateDisplacementTableColumns()
    {
        if (displacementsGrid.Columns.Count < 9) return;
        bool extremes = _vm.DisplacementDisplayMode == FemDisplacementDisplayMode.ExtremesOnly;
        displacementsGrid.Columns[1].Visibility = extremes ? Visibility.Visible : Visibility.Collapsed;
        displacementsGrid.Columns[8].Visibility = extremes ? Visibility.Visible : Visibility.Collapsed;

        string lengthUnit = Loc.S($"FemLength{_vm.DisplacementLengthUnit}");
        string rotationUnit = string.Format(Loc.S("FemRotationDisplayUnit"),
            Loc.S("FemUnitRad"), (int)_vm.RotationDisplayScale);
        displacementsGrid.Columns[2].Header = string.Format(Loc.S("FemResultColumnHeader"), Loc.S("FemResultNodalUx"), lengthUnit);
        displacementsGrid.Columns[3].Header = string.Format(Loc.S("FemResultColumnHeader"), Loc.S("FemResultNodalUy"), lengthUnit);
        displacementsGrid.Columns[4].Header = string.Format(Loc.S("FemResultColumnHeader"), Loc.S("FemResultNodalUz"), lengthUnit);
        displacementsGrid.Columns[5].Header = string.Format(Loc.S("FemResultColumnHeader"), Loc.S("FemResultNodalRx"), rotationUnit);
        displacementsGrid.Columns[6].Header = string.Format(Loc.S("FemResultColumnHeader"), Loc.S("FemResultNodalRy"), rotationUnit);
        displacementsGrid.Columns[7].Header = string.Format(Loc.S("FemResultColumnHeader"), Loc.S("FemResultNodalRz"), rotationUnit);
    }

    void ReactionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is FemNodeReaction row)
            _vm.SelectNode(row.NodeTag);
    }

    void ElementForcesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is FemElementEndForces row)
            _vm.SelectElement(row.ElemTag);
    }

    void IpMarkersToggle(object sender, System.Windows.RoutedEventArgs e)
    {
        BuildIpMarkers();
    }

    void BuildIpMarkers()
    {
        // Тумблер с IsChecked="True" срабатывает ещё во время InitializeComponent
        // (до присваивания _vm в конструкторе) — в этот момент маркеры строить нечего.
        if (_vm == null) return;
        if (_ipAvailableVisual != null) { viewport.Children.Remove(_ipAvailableVisual); _ipAvailableVisual = null; }
        if (_ipUnavailableVisual != null) { viewport.Children.Remove(_ipUnavailableVisual); _ipUnavailableVisual = null; }
        foreach (var sphere in _ipPickSpheresByRow.Values) viewport.Children.Remove(sphere);
        _ipPickSpheresByRow.Clear();
        foreach (var key in _pickTargets.Keys.Where(k => _pickTargets[k].SectionRow != null).ToList())
            _pickTargets.Remove(key);

        if (showIpMarkersCheck.IsChecked != true) return;

        var available = new Point3DCollection();
        var unavailable = new Point3DCollection();
        foreach (var m in _vm.SectionMarkers)
            (m.Location.IsStateAvailable ? available : unavailable).Add(m.Point);
        if (available.Count > 0)
        {
            _ipAvailableVisual = new PointsVisual3D { Color = Colors.SeaGreen, Size = 7, Points = available };
            viewport.Children.Add(_ipAvailableVisual);
        }
        if (unavailable.Count > 0)
        {
            _ipUnavailableVisual = new PointsVisual3D { Color = Colors.Gray, Size = 7, Points = unavailable };
            viewport.Children.Add(_ipUnavailableVisual);
        }

        if (_vm.SectionMarkers.Count > PickTargetThreshold) return;
        double avgSegmentLength = _vm.DeformedElementSegments.Count > 0
            ? _vm.DeformedElementSegments.Average(s => (s.P1 - s.P0).Length)
            : 1.0;
        double ipRadius = System.Math.Clamp(avgSegmentLength * NodePickRadiusFactor, NodePickRadiusMin, NodePickRadiusMax);
        foreach (var m in _vm.SectionMarkers)
        {
            var sphere = new SphereVisual3D
            {
                Center = m.Point,
                Radius = ipRadius,
                Fill = new SolidColorBrush(IsSectionHighlighted(m.Location) ? Colors.OrangeRed : Colors.Transparent)
            };
            _pickTargets[sphere] = new PickTarget(false, 0, m.Location);
            _ipPickSpheresByRow[m.Location] = sphere;
            viewport.Children.Add(sphere);
        }
    }

    bool IsSectionHighlighted(FemSectionLocationRow row) => Equals(_vm.SelectedSectionLocation, row);

    void UpdateIpHighlight()
    {
        foreach (var (row, sphere) in _ipPickSpheresByRow)
            sphere.Fill = new SolidColorBrush(IsSectionHighlighted(row) ? Colors.OrangeRed : Colors.Transparent);
    }

    void OpenSectionState_Click(object sender, System.Windows.RoutedEventArgs e)
        => _vm.RequestSectionState(_vm.SelectedSectionLocation);

    void SectionGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => _vm.RequestSectionState(_vm.SelectedSectionLocation);
}
