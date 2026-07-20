using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemSchemaView3D : UserControl
{
    /// <summary>Редактор схемы: источник команд и режимов единого тулбара 3D-вида.</summary>
    public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(
        nameof(Editor), typeof(FemSchemaEditorVM), typeof(FemSchemaView3D));

    public FemSchemaEditorVM? Editor
    {
        get => (FemSchemaEditorVM?)GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    Fem3DVM? VM => DataContext as Fem3DVM;

    Fem3DVM?        _activeVm;
    PointsVisual3D? _nodesVisual;
    LinesVisual3D?  _shellEdgesVisual;
    LinesVisual3D?  _meshVisual;
    LinesVisual3D?  _meshNodeGlyphVisual;
    bool _meshRenderQueued;

    readonly Dictionary<Visual3D, (bool IsNode, string Tag)> _pickTargets = new();
    PointsVisual3D? _editNodesVisual;
    string? _contextMenuTargetTag;

    bool _createNodeMode;
    bool _createBarMode;
    string? _pendingBarFirstNode;
    ModelVisual3D? _groundPlaneVisual;
    LinesVisual3D? _rubberBandVisual;

    public event Action<Point3D>? NodeCreateRequested;
    public event Action<string, string>? BarCreateRequested;

    /// <summary>Плоскость клика/наведения нужна и для создания узла, и для резиновой линии стержня.</summary>
    bool NeedsGroundPlane => _createNodeMode || (_createBarMode && _pendingBarFirstNode != null);

    public void SetCreateNodeMode(bool value)
    {
        _createNodeMode = value;
        _pendingBarFirstNode = null;
        UpdateGroundPlane();
        ClearRubberBand();
        createNodePanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetCreateBarMode(bool value)
    {
        _createBarMode = value;
        _pendingBarFirstNode = null;
        UpdateGroundPlane();
        ClearRubberBand();
        BuildEditProxies();
        createBarPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateGroundPlane()
    {
        if (NeedsGroundPlane && _groundPlaneVisual == null)
        {
            const double half = 200;
            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection(
                [
                    new Point3D(-half, -half, 0), new Point3D(half, -half, 0),
                    new Point3D(half, half, 0),   new Point3D(-half, half, 0)
                ]),
                TriangleIndices = new Int32Collection([0, 1, 2, 0, 2, 3])
            };
            var mat = new DiffuseMaterial(new SolidColorBrush(Colors.Transparent));
            _groundPlaneVisual = new ModelVisual3D { Content = new GeometryModel3D(mesh, mat) { BackMaterial = mat } };
            viewport.Children.Add(_groundPlaneVisual);
        }
        else if (NeedsGroundPlane && _groundPlaneVisual != null && !viewport.Children.Contains(_groundPlaneVisual))
        {
            // BuildVisuals() успел очистить Children (например, после LoadFromSession) — вернуть плоскость.
            viewport.Children.Add(_groundPlaneVisual);
        }
        else if (!NeedsGroundPlane && _groundPlaneVisual != null)
        {
            viewport.Children.Remove(_groundPlaneVisual);
            _groundPlaneVisual = null;
        }
    }

    void ClearRubberBand()
    {
        if (_rubberBandVisual == null) return;
        viewport.Children.Remove(_rubberBandVisual);
        _rubberBandVisual = null;
    }

    /// <summary>Пересекает луч клика/наведения с плоскостью Z=0 (та же плоскость, что используется
    /// для создания узла). Возвращает false, если плоскость сейчас не показана или промах.</summary>
    bool TryHitGroundPlane(Point screenPosition, out Point3D worldPoint)
    {
        Point3D? hit = null;
        HitTestResultBehavior Callback(HitTestResult result)
        {
            if (result is RayMeshGeometry3DHitTestResult meshHit && meshHit.VisualHit == _groundPlaneVisual)
            {
                var mesh = meshHit.MeshHit;
                var p1 = mesh.Positions[meshHit.VertexIndex1];
                var p2 = mesh.Positions[meshHit.VertexIndex2];
                var p3 = mesh.Positions[meshHit.VertexIndex3];
                hit = new Point3D(
                    p1.X * meshHit.VertexWeight1 + p2.X * meshHit.VertexWeight2 + p3.X * meshHit.VertexWeight3,
                    p1.Y * meshHit.VertexWeight1 + p2.Y * meshHit.VertexWeight2 + p3.Y * meshHit.VertexWeight3,
                    p1.Z * meshHit.VertexWeight1 + p2.Z * meshHit.VertexWeight2 + p3.Z * meshHit.VertexWeight3);
                return HitTestResultBehavior.Stop;
            }
            return HitTestResultBehavior.Continue;
        }
        VisualTreeHelper.HitTest(viewport, null, Callback, new PointHitTestParameters(screenPosition));
        worldPoint = hit ?? default;
        return hit.HasValue;
    }

    public FemSchemaView3D()
    {
        InitializeComponent();
        Loaded             += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        viewport.MouseLeftButtonDown += Viewport_MouseLeftButtonDown;
        viewport.MouseMove           += Viewport_MouseMove;
        PreviewMouseRightButtonDown += FemSchemaView3D_PreviewMouseRightButtonDown;
        viewport.KeyDown              += Viewport_KeyDown;
        viewport.Focusable = true;
    }

    /// <summary>Перехватывает ПКМ до контроллера камеры Helix: только попадание в редактируемый объект
    /// открывает меню, а клик по пустому месту остаётся жестом вращения модели.</summary>
    void FemSchemaView3D_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_createBarMode && _pendingBarFirstNode != null)
        {
            _pendingBarFirstNode = null;
            UpdateGroundPlane();
            ClearRubberBand();
            BuildEditProxies();
            return;
        }

        ShowContextMenuAt(e);
    }

    void Viewport_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (_createBarMode && _pendingBarFirstNode != null)
        {
            _pendingBarFirstNode = null;
            UpdateGroundPlane();
            ClearRubberBand();
            BuildEditProxies();
            e.Handled = true;
        }
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (VM is not { } vm) return;
        if (vm == _activeVm)
        {
            // Повторный показ той же VM (например, вернулись со вкладки «Узлы») — TabControl
            // удаляет и заново подключает содержимое неактивной вкладки, вызывая Loaded заново.
            // Не дёргаем БД/сессию повторно — просто перерисовываем уже посчитанное состояние.
            BuildVisuals();
            return;
        }
        await ActivateVmAsync(vm);
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
        {
            _activeVm.PropertyChanged -= OnVMPropertyChanged;
            if (_activeVm.Selection != null) _activeVm.Selection.Changed -= OnSelectionChanged;
        }

        _activeVm         = vm;
        _nodesVisual      = null;
        _shellEdgesVisual = null;
        _meshVisual       = null;
        _meshNodeGlyphVisual = null;
        viewport.Children.Clear();

        vm.PropertyChanged += OnVMPropertyChanged;
        if (vm.Selection != null) vm.Selection.Changed += OnSelectionChanged;
        await vm.LoadAsync();
    }

    void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (VM is { IsLoading: false }) BuildEditProxies();
    }

    void OnVMPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if ((e.PropertyName == nameof(Fem3DVM.IsLoading) && VM is { IsLoading: false }) ||
            e.PropertyName == nameof(Fem3DVM.MeshLinePoints) ||
            e.PropertyName == nameof(Fem3DVM.MeshNodePoints) ||
            e.PropertyName == nameof(Fem3DVM.DiagramGlyphs))
            BuildVisuals();
    }

    void BuildVisuals()
    {
        viewport.Children.Clear();
        _meshVisual = null;
        _meshNodeGlyphVisual = null;
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

        _meshVisual = VM.MeshLinePoints is { Count: > 0 } meshPoints
            ? new LinesVisual3D { Points = meshPoints, Color = Colors.MediumTurquoise, Thickness = 2.0 }
            : null;
        if (VM.MeshNodePoints is { Count: > 0 } meshNodePoints)
        {
            _meshNodeGlyphVisual = new LinesVisual3D
            {
                Points = FemMeshNodeGlyphFactory.Create(meshNodePoints),
                Color = Colors.DeepSkyBlue,
                Thickness = 1.5
            };
        }
        _shellEdgesVisual = VM.ShellEdgePoints is { Count: > 0 } edgePts
            ? new LinesVisual3D { Points = edgePts, Color = Colors.DimGray, Thickness = 0.5 }
            : null;

        if (showShellEdgesCheck.IsChecked == true && _shellEdgesVisual != null)
            viewport.Children.Add(_shellEdgesVisual);

        // В режиме редактирования узлы рисуются как сферы в BuildEditProxies (заодно кликабельные);
        // плоский PointsVisual3D (квадратные спрайты) — только для режима просмотра.
        if (VM.EditMode)
        {
            _nodesVisual = null;
        }
        else
        {
            _nodesVisual = VM.NodePoints is { Count: > 0 } nodePts
                ? new PointsVisual3D { Points = nodePts, Color = Colors.DimGray, Size = 3 }
                : null;

            if (showNodesCheck.IsChecked == true && _nodesVisual != null)
                viewport.Children.Add(_nodesVisual);
        }

        if (VM.BarGroups.Count > 0 || VM.ShellMesh != null)
            viewport.ZoomExtents(500);

        BuildDiagramGlyphs();
        BuildEditProxies();
        AddMeshVisuals();
        UpdateGroundPlane();
    }

    /// <summary>Рисует условные знаки закреплений, сил и моментов отдельными 3D-линиями.</summary>
    void BuildDiagramGlyphs()
    {
        if (VM == null) return;
        foreach (var glyph in VM.DiagramGlyphs)
        {
            if (!VM.DiagramNodePositions.TryGetValue(glyph.NodeId, out var node)) continue;
            var axis = glyph.Axis;
            axis.Normalize();
            var side = Math.Abs(axis.Z) < 0.9
                ? Vector3D.CrossProduct(axis, new Vector3D(0, 0, 1))
                : Vector3D.CrossProduct(axis, new Vector3D(0, 1, 0));
            side.Normalize();
            var up = Vector3D.CrossProduct(axis, side);
            up.Normalize();

            switch (glyph.Kind)
            {
                case FemDiagramGlyphKind.TranslationSupport:
                    DrawTranslationSupport(node, axis, side, up);
                    break;
                case FemDiagramGlyphKind.RotationSupport:
                    DrawRotationSupport(node, axis, side, up);
                    break;
                case FemDiagramGlyphKind.Force:
                    DrawForce(node, axis * glyph.Sign, side, up);
                    break;
                case FemDiagramGlyphKind.Moment:
                    DrawMoment(node, axis * glyph.Sign, side, up);
                    break;
            }
        }
    }

    void DrawTranslationSupport(Point3D node, Vector3D axis, Vector3D side, Vector3D up)
    {
        var basePoint = node + axis * 0.28;
        AddGlyphLines(Colors.RoyalBlue, 2,
            [node, basePoint, basePoint - side * 0.16, basePoint + side * 0.16,
             basePoint - up * 0.16, basePoint + up * 0.16,
             basePoint - side * 0.12 - up * 0.12, basePoint + side * 0.12 + up * 0.12]);
    }

    void DrawRotationSupport(Point3D node, Vector3D axis, Vector3D side, Vector3D up)
    {
        var points = new Point3DCollection();
        for (int i = 0; i <= 12; i++)
        {
            double angle = Math.PI * 1.6 * i / 12 + Math.PI * 0.2;
            points.Add(node + side * (Math.Cos(angle) * 0.24) + up * (Math.Sin(angle) * 0.24));
        }
        AddGlyphLine(Colors.MediumBlue, 2, points);
        var tip = points[^1];
        AddGlyphLines(Colors.MediumBlue, 2, [tip, tip - side * 0.1 - up * 0.06, tip, tip + side * 0.04 - up * 0.1]);
    }

    void DrawForce(Point3D node, Vector3D direction, Vector3D side, Vector3D up)
    {
        var tip = node - direction * 0.16;
        var tail = node - direction * 0.72;
        AddGlyphLines(Colors.Crimson, 2.5,
            [tail, tip,
             tip, tip - direction * 0.18 + side * 0.11,
             tip, tip - direction * 0.18 - side * 0.11,
             tip, tip - direction * 0.18 + up * 0.11,
             tip, tip - direction * 0.18 - up * 0.11]);
    }

    void DrawMoment(Point3D node, Vector3D axis, Vector3D side, Vector3D up)
    {
        var points = new Point3DCollection();
        for (int i = 0; i <= 16; i++)
        {
            double angle = Math.PI * 1.65 * i / 16;
            points.Add(node + side * (Math.Cos(angle) * 0.32) + up * (Math.Sin(angle) * 0.32));
        }
        AddGlyphLine(Colors.DarkOrange, 2.5, points);
        var tip = points[^1];
        var tangent = Vector3D.CrossProduct(axis, tip - node);
        tangent.Normalize();
        AddGlyphLines(Colors.DarkOrange, 2.5,
            [tip, tip - tangent * 0.15 + axis * 0.08, tip, tip - tangent * 0.15 - axis * 0.08]);
    }

    void AddGlyphLines(Color color, double thickness, IEnumerable<Point3D> points)
        => AddGlyphLine(color, thickness, new Point3DCollection(points));

    void AddGlyphLine(Color color, double thickness, Point3DCollection points)
    {
        if (points.Count < 2) return;
        viewport.Children.Add(new LinesVisual3D { Points = points, Color = color, Thickness = thickness });
    }

    /// <summary>Порог, после которого вместо сфер (по одной на узел) используется PointsVisual3D.
    /// Сферы дают per-node клик, но O(N) Visual3D — на импортированных моделях (>500 узлов) вешают UI.</summary>
    const int SphereNodeThreshold = 500;

    void BuildEditProxies()
    {
        foreach (var visual in _pickTargets.Keys) viewport.Children.Remove(visual);
        _pickTargets.Clear();
        if (_editNodesVisual != null) { viewport.Children.Remove(_editNodesVisual); _editNodesVisual = null; }
        if (VM is not { EditMode: true } vm) return;

        if (showNodesCheck.IsChecked == true)
        {
            if (vm.NodeProxies.Count <= SphereNodeThreshold)
            {
                foreach (var (tag, pos) in vm.NodeProxies)
                {
                    bool isPendingBarFirst = _createBarMode && tag == _pendingBarFirstNode;
                    bool selected = vm.Selection?.SelectedNodeTags.Contains(tag) == true;
                    var color = isPendingBarFirst ? Colors.Gold : selected ? Colors.OrangeRed : Colors.DimGray;
                    var sphere = new SphereVisual3D { Center = pos, Radius = 0.05, Fill = new SolidColorBrush(color) };
                    _pickTargets[sphere] = (true, tag);
                    viewport.Children.Add(sphere);

                    var hitSphere = new SphereVisual3D
                    {
                        Center = pos, Radius = 0.15,
                        Fill = new SolidColorBrush(Colors.Transparent)
                    };
                    _pickTargets[hitSphere] = (true, tag);
                    viewport.Children.Add(hitSphere);
                }
            }
            else
            {
                _editNodesVisual = new PointsVisual3D
                {
                    Points = new Point3DCollection(vm.NodeProxies.Select(np => np.Position)),
                    Color = Colors.DimGray, Size = 3
                };
                viewport.Children.Add(_editNodesVisual);
            }
        }
        foreach (var (tag, p1, p2) in vm.BarProxies)
        {
            bool selected = vm.Selection?.SelectedElemTags.Contains(tag) == true;
            var color = selected ? Colors.OrangeRed : Colors.Transparent;
            var pipe = new PipeVisual3D { Point1 = p1, Point2 = p2, Diameter = 0.04, Fill = new SolidColorBrush(color) };
            _pickTargets[pipe] = (false, tag);
            viewport.Children.Add(pipe);
        }
    }

    void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (VM is not { EditMode: true } vm) return;
        var position = e.GetPosition(viewport);

        if (_createNodeMode)
        {
            if (TryHitGroundPlane(position, out var worldPoint))
            {
                createNodeXBox.Text = worldPoint.X.ToString("F3");
                createNodeYBox.Text = worldPoint.Y.ToString("F3");
                createNodeZBox.Text = worldPoint.Z.ToString("F3");
                NodeCreateRequested?.Invoke(worldPoint);
            }
            return;
        }

        if (vm.Selection is not { } selection) return;
        var hits = new List<(bool IsNode, string Tag)>();
        HitTestResultBehavior Callback(HitTestResult result)
        {
            if (result is RayMeshGeometry3DHitTestResult meshHit &&
                _pickTargets.TryGetValue(meshHit.VisualHit, out var target))
                hits.Add(target);
            return HitTestResultBehavior.Continue;
        }
        VisualTreeHelper.HitTest(viewport, null, Callback, new PointHitTestParameters(position));
        if (hits.Count == 0) return;

        var pick = hits.FirstOrDefault(h => h.IsNode);
        if (pick.Tag == null) pick = hits[0];

        if (_createBarMode)
        {
            if (!pick.IsNode) return;
            if (_pendingBarFirstNode == null)
            {
                _pendingBarFirstNode = pick.Tag;
                UpdateGroundPlane();
                BuildEditProxies();
            }
            else if (_pendingBarFirstNode != pick.Tag)
            {
                BarCreateRequested?.Invoke(_pendingBarFirstNode, pick.Tag);
                _pendingBarFirstNode = pick.Tag;
                UpdateGroundPlane();
                ClearRubberBand();
            }
            return;
        }

        bool additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (pick.IsNode) selection.ToggleNode(pick.Tag, additive);
        else selection.ToggleElement(pick.Tag, additive);
    }

    void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (VM is not { EditMode: true } vm || !_createBarMode || _pendingBarFirstNode == null)
        {
            ClearRubberBand();
            return;
        }

        var firstNode = vm.NodeProxies.FirstOrDefault(np => np.Tag == _pendingBarFirstNode);
        if (firstNode.Tag == null) return;

        if (!TryHitGroundPlane(e.GetPosition(viewport), out var endPoint))
        {
            ClearRubberBand();
            return;
        }

        if (_rubberBandVisual == null)
        {
            _rubberBandVisual = new LinesVisual3D { Color = Colors.Gold, Thickness = 2 };
            viewport.Children.Add(_rubberBandVisual);
        }
        else if (!viewport.Children.Contains(_rubberBandVisual))
        {
            viewport.Children.Add(_rubberBandVisual);
        }
        _rubberBandVisual.Points = new Point3DCollection([firstNode.Position, endPoint]);
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
        if (VM?.EditMode == true)
        {
            // Прокси редактирования добавляются заново; полная пересборка возвращает mesh-слой поверх них.
            BuildVisuals();
            return;
        }

        if (_nodesVisual == null) return;
        if (showNodesCheck.IsChecked == true)
            viewport.Children.Add(_nodesVisual);
        else
            viewport.Children.Remove(_nodesVisual);
    }

    void MeshToggle(object sender, RoutedEventArgs e)
    {
        if (_meshRenderQueued) return;
        _meshRenderQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            _meshRenderQueued = false;
            BuildVisuals();
        }));
    }

    /// <summary>Добавляет расчётную сетку последней, поверх прокси редактирования исходной схемы.</summary>
    void AddMeshVisuals()
    {
        if (showMeshCheck.IsChecked != true) return;
        if (_meshVisual != null) viewport.Children.Add(_meshVisual);
        if (_meshNodeGlyphVisual != null) viewport.Children.Add(_meshNodeGlyphVisual);
    }

    /// <summary>Включает показ сохранённой расчётной сетки.</summary>
    public void ShowMeshOverlay() => showMeshCheck.IsChecked = true;

    void ZoomExtents_Click(object sender, RoutedEventArgs e)
        => viewport.ZoomExtents(500);

    void CreateNodeFromPanel_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(createNodeXBox.Text, out var x)) return;
        if (!double.TryParse(createNodeYBox.Text, out var y)) return;
        if (!double.TryParse(createNodeZBox.Text, out var z)) return;
        NodeCreateRequested?.Invoke(new Point3D(x, y, z));
    }

    void CloseCreateNodePanel_Click(object sender, RoutedEventArgs e)
        => CreateNodeModeCloseRequested?.Invoke();

    public event Action? CreateNodeModeCloseRequested;

    void CloseCreateBarPanel_Click(object sender, RoutedEventArgs e)
        => CreateBarModeCloseRequested?.Invoke();

    public event Action? CreateBarModeCloseRequested;

    public void SetBarSectionItemsSource(System.Collections.IEnumerable? sections)
        => createBarSectionCombo.ItemsSource = sections;

    string? _pendingBarSectionTag;

    void CreateBarSectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (createBarSectionCombo.SelectedItem is CScore.CrossSection cs)
            _pendingBarSectionTag = cs.Tag;
        else
            _pendingBarSectionTag = null;
    }

    /// <summary>Тег сечения, выбранного в панели «Добавить элемент» (для применения при создании).</summary>
    public string? PendingBarSectionTag => _pendingBarSectionTag;

    void ShowContextMenuAt(MouseButtonEventArgs e)
    {
        if (VM is not { EditMode: true }) return;
        if (_createBarMode) return;

        var position = e.GetPosition(viewport);
        (bool IsNode, string Tag)? hit = null;
        HitTestResultBehavior Callback(HitTestResult result)
        {
            if (result is RayMeshGeometry3DHitTestResult meshHit &&
                _pickTargets.TryGetValue(meshHit.VisualHit, out var target))
            {
                hit = target;
                return HitTestResultBehavior.Stop;
            }
            return HitTestResultBehavior.Continue;
        }
        VisualTreeHelper.HitTest(viewport, null, Callback, new PointHitTestParameters(position));
        if (hit is not { } target) return;

        _contextMenuTargetTag = target.Tag;
        var menu = (ContextMenu)Resources[target.IsNode ? "NodeContextMenu" : "MemberContextMenu"];
        menu.PlacementTarget = viewport;
        menu.IsOpen = true;
        e.Handled = true;
    }

    void MemberDeleteCtx_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuTargetTag is not { } tag) return;
        MemberDeleteRequested?.Invoke(tag);
    }

    void MemberSplitCtx_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuTargetTag is not { } tag) return;
        MemberSplitRequested?.Invoke(tag);
    }

    void MemberSectionCtx_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuTargetTag is not { } tag) return;
        MemberSectionEditRequested?.Invoke(tag);
    }

    void MemberPropertiesCtx_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuTargetTag is not { } tag) return;
        MemberPropertiesRequested?.Invoke(tag);
    }

    void MemberForcesCtx_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuTargetTag is not { } tag) return;
        MemberForcesRequested?.Invoke(tag);
    }

    public event Action<string>? MemberDeleteRequested;
    public event Action<string>? MemberSplitRequested;
    public event Action<string>? MemberSectionEditRequested;
    public event Action<string>? MemberPropertiesRequested;
    public event Action<string>? MemberForcesRequested;

    void NodeMoveCtx_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuTargetTag is not { } tag) return;
        var dlg = new FemNodeOffsetDialog(isCopy: false,
            (dx, dy, dz) => NodeMoveRequested?.Invoke(tag, dx, dy, dz))
        { Owner = Window.GetWindow(this) };
        dlg.Show();
    }

    void NodeCopyCtx_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuTargetTag is not { } tag) return;
        var dlg = new FemNodeOffsetDialog(isCopy: true,
            (dx, dy, dz) => NodeCopyRequested?.Invoke(tag, dx, dy, dz))
        { Owner = Window.GetWindow(this) };
        dlg.Show();
    }

    void NodePropertiesCtx_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuTargetTag is not { } tag) return;
        NodePropertiesRequested?.Invoke(tag);
    }

    public event Action<string, double, double, double>? NodeMoveRequested;
    public event Action<string, double, double, double>? NodeCopyRequested;
    public event Action<string>? NodePropertiesRequested;
}
