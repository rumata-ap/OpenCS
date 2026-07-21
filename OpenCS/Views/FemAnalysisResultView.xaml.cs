using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CScore;
using CScore.Fem;
using HelixToolkit.Wpf;
using OpenCS.OpenSees.Structural;
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

    readonly Dictionary<Visual3D, (bool IsNode, int Tag)> _pickTargets = new();
    readonly Dictionary<int, SphereVisual3D> _nodeSpheresByTag = new();
    readonly Dictionary<int, PipeVisual3D> _elemPipesByTag = new();

    bool _highlightWholeMember = true;

    public FemAnalysisResultView(FemAnalysisResultVM vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;
        BuildViewport();
        BuildPickTargets();
        BuildLoadFactorCanvas();
        loadFactorCanvas.StepClicked += idx => _vm.SelectedStepIndex = idx;
        _vm.PropertyChanged += OnVmPropertyChanged;
        viewport.MouseLeftButtonDown += Viewport_MouseLeftButtonDown;
    }

    void BuildLoadFactorCanvas() =>
        loadFactorCanvas.SetData(_vm.LoadFactorPoints.Select(p => (p.Step, p.LoadFactor, p.Converged)).ToList(), _vm.SelectedStepIndex);

    void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FemAnalysisResultVM.SelectedStepIndex))
        {
            loadFactorCanvas.SetData(_vm.LoadFactorPoints.Select(p => (p.Step, p.LoadFactor, p.Converged)).ToList(), _vm.SelectedStepIndex);
        }
        else if (e.PropertyName == nameof(FemAnalysisResultVM.DeformedLines) && _deformed is not null)
        {
            _deformed.Points = _vm.DeformedLines;
        }
        else if (e.PropertyName == nameof(FemAnalysisResultVM.DeformedNodes) && _nodesVisual is not null)
        {
            _nodesVisual.Points = showNodesCheck.IsChecked == true ? _vm.DeformedNodes : null!;
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
        else if (e.PropertyName is nameof(FemAnalysisResultVM.SelectedNodeTag) or nameof(FemAnalysisResultVM.SelectedElemTag))
        {
            UpdateSelectionHighlight();
        }
        else if (e.PropertyName == nameof(FemAnalysisResultVM.SelectedDisplacementRow) && _vm.SelectedDisplacementRow is { } dispRow)
        {
            displacementsGrid.ScrollIntoView(dispRow);
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
        if (_nodesVisual != null && sender is ToggleButton tb)
            _nodesVisual.Points = tb.IsChecked == true ? _vm.DeformedNodes : null!;
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
            _pickTargets[sphere] = (true, tag);
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
            _pickTargets[pipe] = (false, tag);
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

    (bool IsNode, int Tag)? HitTestPick(System.Windows.Point position)
    {
        (bool IsNode, int Tag)? hit = null;
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
                hit = target;
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

        if (target.IsNode)
            _vm.SelectNode(_vm.SelectedNodeTag == target.Tag ? null : target.Tag);
        else
            _vm.SelectElement(_vm.SelectedElemTag == target.Tag ? null : target.Tag);
    }

    void Viewport_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var hit = HitTestPick(e.GetPosition(viewport));
        if (hit is not { } target) return;

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
        if (_contextMenuTargetTag != null) _vm.RequestGoToSection(_contextMenuTargetTag);
    }

    void NodeValuesCtx_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_contextMenuTargetTag != null) _vm.RequestShowNodeValues(_contextMenuTargetTag);
    }

    void DisplacementsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is FemNodeDisplacement row)
            _vm.SelectNode(row.NodeTag);
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
}
