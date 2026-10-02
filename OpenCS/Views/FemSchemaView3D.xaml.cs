using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CScore.Fem;
using HelixToolkit.Wpf;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.Views.Helpers;

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
    LinesVisual3D?  _planarMeshEdgesVisual;
    LinesVisual3D?  _planarMeshNodeGlyphVisual;

    readonly Dictionary<Visual3D, (bool IsNode, string Tag)> _pickTargets = new();
    readonly Dictionary<Visual3D, string> _planarRegionPickTargets = new();
    readonly Dictionary<Visual3D, (bool IsNodeLoad, string Tag)> _loadPickTargets = new();
    readonly Dictionary<Visual3D, (string NodeTag, int Dof)> _kinematicPickTargets = new();
    PointsVisual3D? _editNodesVisual;
    string? _contextMenuTargetTag;
    (bool IsNodeLoad, string Tag)? _contextMenuLoadTarget;
    (string NodeTag, int Dof)? _contextMenuKinematicTarget;

    bool _createNodeMode;
    bool _createBarMode, _createPlateMode, _createWallMode, _createSpatialPlateMode;
    string? _pendingBarFirstNode;
    readonly List<string> _pendingFrameNodes = [];
    ModelVisual3D? _groundPlaneVisual;
    LinesVisual3D? _rubberBandVisual;

    public event Action<Point3D>? NodeCreateRequested;
    public event Action<string, string>? BarCreateRequested;
    public event Action<string>? PlateFrameRequested;
    public event Action<string, string>? WallFrameRequested;
    public event Action<string, string, string>? SpatialPlateFrameRequested;
    public event Action<string>? PlanarRegionEditRequested;
    public event Action<string>? PlanarRegionDeleteRequested;

    int RequiredFrameNodeCount =>
        _createPlateMode ? 1 : _createWallMode ? 2 : _createSpatialPlateMode ? 3 : 0;

    public void SetCreatePlateMode(bool value) { _createPlateMode = value; _pendingFrameNodes.Clear(); UpdateGroundPlane(); }
    public void SetCreateWallMode(bool value) { _createWallMode = value; _pendingFrameNodes.Clear(); UpdateGroundPlane(); }
    public void SetCreateSpatialPlateMode(bool value) { _createSpatialPlateMode = value; _pendingFrameNodes.Clear(); UpdateGroundPlane(); }

    /// <summary>Плоскость клика/наведения нужна и для создания узла, и для резиновой линии стержня.</summary>
    bool NeedsGroundPlane => _createNodeMode || (_createBarMode && _pendingBarFirstNode != null)
        || _createPlateMode || _createWallMode || _createSpatialPlateMode;

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
        viewport.MouseLeave          += Viewport_MouseLeave;
        PreviewMouseRightButtonDown += FemSchemaView3D_PreviewMouseRightButtonDown;
        viewport.KeyDown              += Viewport_KeyDown;
        viewport.Focusable = true;
        InitCamera();
    }

    // ── Камера: проекция и угол обзора общие для всех 3D-видов сеанса ───────────────────────

    static bool s_orthographic;
    static double s_fieldOfView = 45;

    void InitCamera()
    {
        viewport.Orthographic = s_orthographic;
        orthographicCheck.IsChecked = s_orthographic;
        showGridCheck.IsChecked = s_showGrid;
        fieldOfViewSlider.Value = s_fieldOfView;
        if (viewport.Camera is PerspectiveCamera camera) camera.FieldOfView = s_fieldOfView;
        // Обработчики — после начальных значений: иначе они сработали бы ещё до настройки вида.
        orthographicCheck.Click += (_, _) =>
        {
            s_orthographic = orthographicCheck.IsChecked == true;
            viewport.Orthographic = s_orthographic;
        };
        fieldOfViewSlider.ValueChanged += (_, e) => SetFieldOfView(e.NewValue);
        fieldOfViewSlider.ToolTip = FieldOfViewTip();
    }

    /// <summary>
    /// Меняет угол обзора перспективы, сохраняя видимый размер схемы у точки, на которую смотрит
    /// камера: камера отъезжает или приближается вдоль направления взгляда (иначе при уменьшении угла
    /// схема бы «наезжала»).
    /// </summary>
    void SetFieldOfView(double degrees)
    {
        s_fieldOfView = degrees;
        fieldOfViewSlider.ToolTip = FieldOfViewTip();
        if (viewport.Camera is not PerspectiveCamera camera || camera.FieldOfView == degrees) return;
        double scale = Math.Tan(camera.FieldOfView * Math.PI / 360) / Math.Tan(degrees * Math.PI / 360);
        var target = camera.Position + camera.LookDirection;
        var look = camera.LookDirection * scale;
        camera.FieldOfView = degrees;
        camera.Position = target - look;
        camera.LookDirection = look;
    }

    string FieldOfViewTip() =>
        string.Format(CultureInfo.CurrentCulture, (string)FindResource("Fem3DFieldOfViewValue"), s_fieldOfView);

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
        if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None &&
            Editor?.Selection?.SelectedNodeTags is { Count: > 0 } selectedNodes)
        {
            NodeDeleteRequested?.Invoke(selectedNodes.ToArray());
            e.Handled = true;
            return;
        }

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
        _planarMeshEdgesVisual = null;
        _planarMeshNodeGlyphVisual = null;
        viewport.Children.Clear();

        vm.PropertyChanged += OnVMPropertyChanged;
        if (vm.Selection != null) vm.Selection.Changed += OnSelectionChanged;
        vm.ShowShellEdges = s_showGrid;
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
            e.PropertyName == nameof(Fem3DVM.DiagramGlyphs) ||
            e.PropertyName == nameof(Fem3DVM.MemberLoadGlyphs) ||
            e.PropertyName == nameof(Fem3DVM.ShowSectionGlyphs) ||
            e.PropertyName == nameof(Fem3DVM.ShowLoadValues))
            BuildVisuals();
        else if (e.PropertyName == nameof(Fem3DVM.MosaicShellMeshes) && VM is { IsLoading: false })
            ReplaceMosaicVisuals();
    }

    List<ModelVisual3D> _shellVisuals = [];
    List<LinesVisual3D> _barVisuals = [];

    /// <summary>Линии стержней: по сечениям либо по цветам мозаики.</summary>
    List<LinesVisual3D> CreateBarVisuals() => VM == null ? [] : VM.BarGroups
        .Select(group => new LinesVisual3D { Points = group.Points, Color = group.Color, Thickness = group.Thickness })
        .ToList();

    /// <summary>Заливка пластин: мозаика (по цветам) и остальные КЭ — обычным цветом
    /// (при включённой мозаике — полупрозрачной подложкой).</summary>
    List<ModelVisual3D> CreateShellVisuals(bool isHighlight)
    {
        var result = new List<ModelVisual3D>();
        if (VM == null) return result;
        static ModelVisual3D Visual(MeshGeometry3D mesh, Color color)
        {
            var mat = new DiffuseMaterial(new SolidColorBrush(color));
            return new ModelVisual3D { Content = new GeometryModel3D(mesh, mat) { BackMaterial = mat } };
        }
        // Непрозрачные — раньше полупрозрачной подложки, иначе она перекроет их в z-буфере.
        foreach (var m in VM.MosaicShellMeshes)
            result.Add(Visual(m.Mesh, m.Color));
        if (VM.ShellMesh is { } bgMesh)
            result.Add(Visual(bgMesh, isHighlight || VM.MosaicShellMeshes.Count > 0 ? Fem3DVM.ShellBgColor : Fem3DVM.ShellColor));
        if (VM.HiShellMesh is { } hiMesh)
            result.Add(Visual(hiMesh, Fem3DVM.ShellHiColor));
        return result;
    }

    /// <summary>Заменить только заливку пластин и линии стержней (смена мозаики) — без перестройки сцены
    /// и сброса камеры.</summary>
    void ReplaceMosaicVisuals()
    {
        if (VM == null) return;
        int barIndex = _barVisuals.Count > 0 ? viewport.Children.IndexOf(_barVisuals[0]) : -1;
        foreach (var v in _barVisuals) viewport.Children.Remove(v);
        _barVisuals = CreateBarVisuals();
        if (barIndex < 0 || barIndex > viewport.Children.Count) barIndex = Math.Min(1, viewport.Children.Count);
        for (int i = 0; i < _barVisuals.Count; i++)
            viewport.Children.Insert(barIndex + i, _barVisuals[i]);

        int index = _shellVisuals.Count > 0 ? viewport.Children.IndexOf(_shellVisuals[0]) : -1;
        foreach (var v in _shellVisuals) viewport.Children.Remove(v);
        _shellVisuals = CreateShellVisuals(VM.HiShellMesh != null);
        if (index < 0 || index > viewport.Children.Count) index = Math.Min(1 + _barVisuals.Count, viewport.Children.Count);
        for (int i = 0; i < _shellVisuals.Count; i++)
            viewport.Children.Insert(index + i, _shellVisuals[i]);
    }

    void BuildVisuals()
    {
        viewport.Children.Clear();
        _meshVisual = null;
        _meshNodeGlyphVisual = null;
        _planarMeshEdgesVisual = null;
        _planarMeshNodeGlyphVisual = null;
        _loadPickTargets.Clear();
        _kinematicPickTargets.Clear();
        viewport.Children.Add(new DefaultLights());

        if (VM == null) return;

        _barVisuals = CreateBarVisuals();
        foreach (var v in _barVisuals) viewport.Children.Add(v);

        bool isHighlight = VM.HiShellMesh != null;

        _shellVisuals = CreateShellVisuals(isHighlight);
        foreach (var v in _shellVisuals) viewport.Children.Add(v);

        foreach (var pv in VM.PlanarRegionVisuals)
        {
            if (pv.IsMeshLocked)
            {
                // Плита/стена из кБ ЛИРЫ: под ней уже залита сетка ЛИРЫ — только утолщённый контур
                // (своя заливка ложится в ту же плоскость и накладывается некрасиво).
                viewport.Children.Add(new LinesVisual3D { Points = pv.EdgePoints, Color = Fem3DVM.LockedMemberColor, Thickness = 3.5 });
                continue;
            }
            var mat   = new DiffuseMaterial(new SolidColorBrush(Fem3DVM.PlanarRegionMeshColor));
            var model = new GeometryModel3D(pv.Mesh, mat) { BackMaterial = mat };
            viewport.Children.Add(new ModelVisual3D { Content = model });

            viewport.Children.Add(new LinesVisual3D { Points = pv.EdgePoints, Color = Colors.SteelBlue, Thickness = 1.2 });
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
        _shellEdgesVisual = CreateShellEdgesVisual();
        _planarMeshEdgesVisual = VM.PlanarRegionMeshEdgePoints is { Count: > 0 } prMeshEdges
            ? new LinesVisual3D { Points = prMeshEdges, Color = Colors.DimGray, Thickness = 0.5 }
            : null;
        _planarMeshNodeGlyphVisual = VM.PlanarRegionMeshNodePoints is { Count: > 0 } prMeshNodes
            ? new LinesVisual3D { Points = FemMeshNodeGlyphFactory.Create(prMeshNodes), Color = Colors.DeepSkyBlue, Thickness = 1.5 }
            : null;

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

        // BuildEditProxies — раньше глифов нагрузок: их прозрачные (но пишущие в z-buffer)
        // сферы-прокси для клика иначе перекрывают ещё не нарисованные видимые сферы узлов/труб
        // стержней, отрисованные позже в том же кадре (WPF 3D не отключает запись глубины для
        // прозрачных материалов) — узлы с нагрузкой визуально пропадали.
        BuildEditProxies();
        BuildDiagramGlyphs();
        BuildMemberLoadGlyphs();
        BuildSectionGlyphs();
        ApplyGridVisuals();
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
                {
                    var mid = DrawForce(node, axis * glyph.Sign, side, up,
                        VM.ShowLoadValues ? FormatComponentValue(glyph.Component, glyph.Value, moment: false) : null,
                        Colors.Crimson);
                    AddNodeLoadPickTarget(mid, glyph.NodeId, Colors.Crimson);
                    break;
                }
                case FemDiagramGlyphKind.Moment:
                {
                    var mid = DrawMoment(node, axis * glyph.Sign, side, up,
                        VM.ShowLoadValues ? FormatComponentValue(glyph.Component, glyph.Value, moment: true) : null,
                        Colors.DarkOrange);
                    AddNodeLoadPickTarget(mid, glyph.NodeId, Colors.DarkOrange);
                    break;
                }
                case FemDiagramGlyphKind.KinematicDisplacement:
                {
                    var mid = DrawForce(node, axis * glyph.Sign, side, up,
                        VM.ShowLoadValues ? FormatKinematicValue(glyph.Component, glyph.Value, rotation: false) : null,
                        KinematicColor);
                    AddKinematicPickTarget(mid, glyph.NodeId, glyph.Component);
                    break;
                }
                case FemDiagramGlyphKind.KinematicRotation:
                {
                    var mid = DrawMoment(node, axis * glyph.Sign, side, up,
                        VM.ShowLoadValues ? FormatKinematicValue(glyph.Component, glyph.Value, rotation: true) : null,
                        KinematicColor);
                    AddKinematicPickTarget(mid, glyph.NodeId, glyph.Component);
                    break;
                }
            }
        }
    }

    /// <summary>Цвет глифов кинематических воздействий (заданных перемещений/поворотов) — тот же,
    /// что и у иконки KinematicLoadTool на тулбаре, чтобы визуально связать инструмент и результат.</summary>
    static readonly Color KinematicColor = Color.FromRgb(0x8E, 0x44, 0xAD);

    static string FormatKinematicValue(string component, double value, bool rotation)
    {
        string unit = Loc.S(rotation ? "FemUnitRad" : "FemUnitM");
        return $"{component} = {value:0.####} {unit}";
    }

    /// <summary>Видимый маркер-«ручка» для выбора узловой нагрузки правым кликом (только в режиме
    /// редактирования). Ставится в середине самой стрелки/петли глифа, а не в узле — не совпадает
    /// по положению ни с узлом, ни с соседними элементами, поэтому можно рисовать непрозрачным,
    /// не рискуя перекрыть что-то по глубине (WPF 3D пишет z-buffer и для прозрачного материала).
    /// Один и тот же узел может дать несколько маркеров (по числу компонент) — все ведут к одному
    /// и тому же узлу, что корректно для удаления/изменения нагрузки целиком.</summary>
    void AddNodeLoadPickTarget(Point3D glyphMidpoint, int nodeId, Color color)
    {
        if (VM is not { EditMode: true }) return;
        string? nodeTag = Editor?.Session.Nodes.FirstOrDefault(n => n.Id == nodeId)?.NodeTag;
        if (nodeTag == null) return;
        var handle = new SphereVisual3D { Center = glyphMidpoint, Radius = 0.05, Fill = new SolidColorBrush(color) };
        _loadPickTargets[handle] = (true, nodeTag);
        viewport.Children.Add(handle);
    }

    /// <summary>Видимый маркер-«ручка» для выбора кинематического воздействия (заданного перемещения
    /// или поворота) правым кликом. В отличие от узловой силовой нагрузки удаляется не целиком,
    /// а по конкретной степени свободы — так же, как задаётся: в диалоге каждый DOF независим.</summary>
    void AddKinematicPickTarget(Point3D glyphMidpoint, int nodeId, string component)
    {
        if (VM is not { EditMode: true }) return;
        string? nodeTag = Editor?.Session.Nodes.FirstOrDefault(n => n.Id == nodeId)?.NodeTag;
        int? dof = KinematicDof(component);
        if (nodeTag == null || dof == null) return;
        var handle = new SphereVisual3D { Center = glyphMidpoint, Radius = 0.05, Fill = new SolidColorBrush(KinematicColor) };
        _kinematicPickTargets[handle] = (nodeTag, dof.Value);
        viewport.Children.Add(handle);
    }

    static int? KinematicDof(string component) => component switch
    {
        "Ux" => 1, "Uy" => 2, "Uz" => 3, "Rx" => 4, "Ry" => 5, "Rz" => 6, _ => null
    };

    static string FormatComponentValue(string component, double valueNewtons, bool moment)
    {
        double kilo = moment
            ? FemUnitConverter.NewtonMetersToKiloNewtonMeters(valueNewtons)
            : FemUnitConverter.NewtonsToKiloNewtons(valueNewtons);
        string unit = Loc.S(moment ? "FemUnitKNm" : "FemUnitKN");
        return $"{component} = {kilo:0.##} {unit}";
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

    /// <summary>Рисует стрелку силы/кинематического перемещения; возвращает середину стрелки
    /// (не узел и не её кончик) — устойчивая точка для прокси выбора правым кликом.</summary>
    Point3D DrawForce(Point3D node, Vector3D direction, Vector3D side, Vector3D up, string? valueText, Color color)
    {
        var tip = node - direction * 0.16;
        var tail = node - direction * 0.72;
        AddGlyphLines(color, 2.5,
            [tail, tip,
             tip, tip - direction * 0.18 + side * 0.11,
             tip, tip - direction * 0.18 - side * 0.11,
             tip, tip - direction * 0.18 + up * 0.11,
             tip, tip - direction * 0.18 - up * 0.11]);
        if (valueText != null) AddValueLabel(tail, valueText, color);
        return tip + (tail - tip) * 0.5;
    }

    /// <summary>Рисует петлю момента/кинематического поворота; возвращает точку на самой петле
    /// (не узел) — устойчивая точка для прокси выбора правым кликом.</summary>
    Point3D DrawMoment(Point3D node, Vector3D axis, Vector3D side, Vector3D up, string? valueText, Color color)
    {
        var points = new Point3DCollection();
        for (int i = 0; i <= 16; i++)
        {
            double angle = Math.PI * 1.65 * i / 16;
            points.Add(node + side * (Math.Cos(angle) * 0.32) + up * (Math.Sin(angle) * 0.32));
        }
        AddGlyphLine(color, 2.5, points);
        var loopMidpoint = node + side * 0.32 + up * 0.32;
        if (valueText != null) AddValueLabel(loopMidpoint, valueText, color);
        var tip = points[^1];
        var tangent = Vector3D.CrossProduct(axis, tip - node);
        tangent.Normalize();
        AddGlyphLines(color, 2.5,
            [tip, tip - tangent * 0.15 + axis * 0.08, tip, tip - tangent * 0.15 - axis * 0.08]);
        return loopMidpoint;
    }

    void AddGlyphLines(Color color, double thickness, IEnumerable<Point3D> points)
        => AddGlyphLine(color, thickness, new Point3DCollection(points));

    void AddGlyphLine(Color color, double thickness, Point3DCollection points)
    {
        if (points.Count < 2) return;
        viewport.Children.Add(new LinesVisual3D { Points = points, Color = color, Thickness = thickness });
    }

    void AddValueLabel(Point3D position, string text, Color color)
    {
        viewport.Children.Add(new BillboardTextVisual3D
        {
            Position = position, Text = text,
            Foreground = new SolidColorBrush(color), Background = Brushes.White, FontSize = 10
        });
    }

    /// <summary>Рисует стрелки распределённых и сосредоточенных нагрузок на активном участке стержня.</summary>
    void BuildMemberLoadGlyphs()
    {
        if (VM is not { ShowLoadGlyphs: true }) return;

        foreach (var glyph in VM.MemberLoadGlyphs)
        {
            var member = glyph.End - glyph.Start;
            double length = member.Length;

            if (!double.IsFinite(length) || length < 1e-12)
            {
                // Сосредоточенная нагрузка: Start == End, стержень-касательная неизвестна —
                // одна стрелка фиксированной длины в точке приложения.
                var pointMid = DrawLoadArrow(glyph.Start, glyph.LoadAtStart, new Vector3D(0, 0, 1), 0.3);
                if (VM.ShowLoadValues) AddMemberLoadValueLabel(glyph.Start, glyph.LoadAtStart, isIntensity: false);
                if (pointMid is { } mid) AddMemberLoadPickTarget(mid, glyph.MemberTag);
                continue;
            }

            AddGlyphLine(Colors.DarkGreen, 2.5, [glyph.Start, glyph.End]);
            Point3D? ribbonPickMidpoint = null;
            for (int i = 0; i <= 4; i++)
            {
                double t = i / 4.0;
                var point = glyph.Start + member * t;
                var value = new Vector3D(
                    glyph.LoadAtStart.X + (glyph.LoadAtEnd.X - glyph.LoadAtStart.X) * t,
                    glyph.LoadAtStart.Y + (glyph.LoadAtEnd.Y - glyph.LoadAtStart.Y) * t,
                    glyph.LoadAtStart.Z + (glyph.LoadAtEnd.Z - glyph.LoadAtStart.Z) * t);
                double arrowLength = Math.Clamp(length * 0.22, 0.08, 0.35);
                var arrowMid = DrawLoadArrow(point, value, member, arrowLength);
                // Берём середину первой отрисованной стрелки как точку прокси — устойчиво
                // работает и для трапециевидной нагрузки, меняющей знак вдоль пролёта.
                ribbonPickMidpoint ??= arrowMid;
            }
            if (VM.ShowLoadValues)
            {
                AddMemberLoadValueLabel(glyph.Start, glyph.LoadAtStart, isIntensity: true);
                if (glyph.LoadAtEnd != glyph.LoadAtStart)
                    AddMemberLoadValueLabel(glyph.End, glyph.LoadAtEnd, isIntensity: true);
            }
            if (ribbonPickMidpoint is { } ribbonMid) AddMemberLoadPickTarget(ribbonMid, glyph.MemberTag);
        }
    }

    /// <summary>Видимый маркер-«ручка» для выбора нагрузки стержня правым кликом (только в режиме
    /// редактирования). Ставится в середине стрелки, а не на оси стержня — не совпадает с трубкой
    /// стержня, поэтому рисуется непрозрачным без риска перекрыть её по глубине.</summary>
    void AddMemberLoadPickTarget(Point3D position, string memberTag)
    {
        if (VM is not { EditMode: true }) return;
        var handle = new SphereVisual3D { Center = position, Radius = 0.05, Fill = new SolidColorBrush(Colors.DarkGreen) };
        _loadPickTargets[handle] = (false, memberTag);
        viewport.Children.Add(handle);
    }

    /// <summary>Подпись модуля вектора нагрузки в кН (сосредоточенная) или кН/м (распределённая).</summary>
    void AddMemberLoadValueLabel(Point3D position, Vector3D value, bool isIntensity)
    {
        double magnitude = value.Length;
        if (!double.IsFinite(magnitude) || magnitude < 1e-12) return;
        double kilo = FemUnitConverter.NewtonsToKiloNewtons(magnitude);
        string unit = Loc.S(isIntensity ? "FemUnitKNPerM" : "FemUnitKN");
        AddValueLabel(position, $"{kilo:0.##} {unit}", Colors.DarkGreen);
    }

    /// <summary>Рисует одну стрелку нагрузки в точке `point` в направлении `value`. `memberTangent» —
    /// касательная стержня для устойчивого выбора поперечного направления оперения стрелки;
    /// при нулевой/параллельной касательной используется запасное глобальное направление.
    /// Возвращает середину стрелки (не null, если нагрузка нулевая — рисовать нечего).</summary>
    Point3D? DrawLoadArrow(Point3D point, Vector3D value, Vector3D memberTangent, double arrowLength)
    {
        double magnitude = value.Length;
        if (!double.IsFinite(magnitude) || magnitude < 1e-12) return null;

        var direction = value;
        direction.Normalize();
        var side = Vector3D.CrossProduct(memberTangent, direction);
        if (side.Length < 1e-10)
            side = Math.Abs(direction.Z) < 0.9
                ? Vector3D.CrossProduct(direction, new Vector3D(0, 0, 1))
                : Vector3D.CrossProduct(direction, new Vector3D(0, 1, 0));
        side.Normalize();
        var tip = point;
        var tail = point - direction * arrowLength;
        AddGlyphLines(Colors.DarkGreen, 2.2,
            [tail, tip,
             tip - direction * arrowLength * 0.32 + side * arrowLength * 0.18,
             tip,
             tip - direction * arrowLength * 0.32 - side * arrowLength * 0.18]);
        return tip + (tail - tip) * 0.5;
    }

    /// <summary>
    /// Рисует контуры сечений и положительные направления локальных Y/Z. Все знаки — тремя линиями по
    /// цвету: по линии на знак давало тысячи LinesVisual3D (каждая пересчитывается на каждом кадре) —
    /// на схемах с тысячами стержней вид открывался секундами и тормозил при вращении.
    /// </summary>
    void BuildSectionGlyphs()
    {
        if (VM is not { ShowSectionGlyphs: true }) return;

        // LinesVisual3D рисует отрезки парами точек: ломаную раскладываем на отрезки.
        var contours = new Point3DCollection();
        var axesY = new Point3DCollection();
        var axesZ = new Point3DCollection();
        void AddPolyline(IReadOnlyList<Point3D> points, bool close)
        {
            for (int i = 0; i + 1 < points.Count; i++) { contours.Add(points[i]); contours.Add(points[i + 1]); }
            if (close && points.Count > 2 && points[0] != points[^1]) { contours.Add(points[^1]); contours.Add(points[0]); }
        }

        foreach (var glyph in VM.SectionGlyphs)
        {
            double extent = glyph.Contours
                .SelectMany(contour => contour)
                .Select(point => Math.Sqrt(point.Y * point.Y + point.Z * point.Z))
                .DefaultIfEmpty(glyph.FallbackHalfSize)
                .Max();
            double halfSize = Math.Max(extent, glyph.FallbackHalfSize);

            if (glyph.Contours.Count == 0)
            {
                var y = glyph.LocalY * halfSize;
                var z = glyph.LocalZ * halfSize;
                AddPolyline([glyph.Center + y + z, glyph.Center - y + z, glyph.Center - y - z, glyph.Center + y - z], close: true);
            }
            else
            {
                foreach (var contour in glyph.Contours)
                    AddPolyline(contour.Select(point => glyph.Center + glyph.LocalY * point.Y + glyph.LocalZ * point.Z).ToList(),
                        close: true);
            }

            double axisLength = Math.Max(halfSize * 1.35, 0.08);
            axesY.Add(glyph.Center); axesY.Add(glyph.Center + glyph.LocalY * axisLength);
            axesZ.Add(glyph.Center); axesZ.Add(glyph.Center + glyph.LocalZ * axisLength);
        }

        AddGlyphLine(Colors.Gold, 1.5, contours);
        AddGlyphLine(Colors.LimeGreen, 1.2, axesY);
        AddGlyphLine(Colors.DeepSkyBlue, 1.2, axesZ);
    }

    /// <summary>Порог, после которого вместо сфер (по одной на узел) используется PointsVisual3D.
    /// Сферы дают per-node клик, но O(N) Visual3D — на импортированных моделях (>500 узлов) вешают UI.</summary>
    const int SphereNodeThreshold = 500;

    ModelVisual3D? _barPickVisual;
    string[] _barPickVertexTags = [];
    object? _barPickSource;

    void BuildBarPickMesh(List<(string Tag, Point3D P1, Point3D P2)> bars)
    {
        _barPickSource = bars;
        _barPickVisual = null;
        _barPickVertexTags = [];
        if (bars.Count == 0) return;
        // Каждый стержень — четырёхгранная призма толщиной 0,04 вдоль оси (для попадания кликом этого хватает).
        var positions = new Point3DCollection(bars.Count * 8);
        var indices = new Int32Collection(bars.Count * 24);
        var tags = new List<string>(bars.Count * 8);
        const double half = 0.02;
        foreach (var (tag, p1, p2) in bars)
        {
            var axis = p2 - p1;
            if (axis.Length < 1e-12) continue;
            axis.Normalize();
            var u = Vector3D.CrossProduct(axis, Math.Abs(axis.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0));
            u.Normalize();
            var v = Vector3D.CrossProduct(axis, u);
            u *= half; v *= half;
            int b = positions.Count;
            foreach (var p in (Point3D[])[p1, p2])
            {
                positions.Add(p + u + v); positions.Add(p - u + v); positions.Add(p - u - v); positions.Add(p + u - v);
            }
            for (int k = 0; k < 4; k++)
            {
                int a0 = b + k, a1 = b + (k + 1) % 4, c0 = b + 4 + k, c1 = b + 4 + (k + 1) % 4;
                indices.Add(a0); indices.Add(a1); indices.Add(c1);
                indices.Add(a0); indices.Add(c1); indices.Add(c0);
            }
            for (int i = 0; i < 8; i++) tags.Add(tag);
        }
        var mesh = new MeshGeometry3D { Positions = positions, TriangleIndices = indices };
        mesh.Freeze();
        var material = new DiffuseMaterial(new SolidColorBrush(Colors.Transparent));
        _barPickVisual = new ModelVisual3D { Content = new GeometryModel3D(mesh, material) { BackMaterial = material } };
        _barPickVertexTags = [.. tags];
    }

    /// <summary>Узел или стержень под попаданием луча: отдельные прокси или общая сетка стержней.</summary>
    bool TryGetPickTarget(RayMeshGeometry3DHitTestResult hit, out (bool IsNode, string Tag) target)
    {
        if (_pickTargets.TryGetValue(hit.VisualHit, out target)) return true;
        if (hit.VisualHit == _barPickVisual && hit.VertexIndex1 >= 0 && hit.VertexIndex1 < _barPickVertexTags.Length)
        {
            target = (false, _barPickVertexTags[hit.VertexIndex1]);
            return true;
        }
        return false;
    }

    void BuildEditProxies()
    {
        foreach (var visual in _pickTargets.Keys) viewport.Children.Remove(visual);
        _pickTargets.Clear();
        foreach (var visual in _planarRegionPickTargets.Keys) viewport.Children.Remove(visual);
        _planarRegionPickTargets.Clear();
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
        // Стержни для выбора кликом — одной прозрачной сеткой (по трубе на стержень — тысячи Visual3D на
        // импортированных схемах: секунды при открытии и при каждой смене выделения). Сетка строится заново
        // только при смене стержней; выделенные — отдельными видимыми трубами.
        if (_barPickVisual != null) viewport.Children.Remove(_barPickVisual);
        if (!ReferenceEquals(_barPickSource, vm.BarProxies)) BuildBarPickMesh(vm.BarProxies);
        if (_barPickVisual != null) viewport.Children.Add(_barPickVisual);
        var selectedElems = vm.Selection?.SelectedElemTags;
        if (selectedElems is { Count: > 0 })
            foreach (var (tag, p1, p2) in vm.BarProxies)
            {
                if (!selectedElems.Contains(tag)) continue;
                var pipe = new PipeVisual3D { Point1 = p1, Point2 = p2, Diameter = 0.04, Fill = new SolidColorBrush(Colors.OrangeRed) };
                _pickTargets[pipe] = (false, tag);
                viewport.Children.Add(pipe);
            }

        foreach (var pv in vm.PlanarRegionVisuals)
        {
            bool selected = vm.Selection?.SelectedElemTags.Contains(pv.ElemTag) == true;
            var color = selected ? Colors.OrangeRed : Colors.Transparent;
            var mat   = new DiffuseMaterial(new SolidColorBrush(color));
            var model = new GeometryModel3D(pv.Mesh, mat) { BackMaterial = mat };
            var visual = new ModelVisual3D { Content = model };
            _planarRegionPickTargets[visual] = pv.ElemTag;
            viewport.Children.Add(visual);
        }
    }

    void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (VM is not { EditMode: true } vm) return;
        viewport.Focus();
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
                TryGetPickTarget(meshHit, out var target))
                hits.Add(target);
            return HitTestResultBehavior.Continue;
        }
        string? planarRegionHit = null;
        HitTestResultBehavior PlanarRegionCallback(HitTestResult prResult)
        {
            if (prResult is RayMeshGeometry3DHitTestResult prMeshHit &&
                _planarRegionPickTargets.TryGetValue(prMeshHit.VisualHit, out var prTag))
                planarRegionHit = prTag;
            return HitTestResultBehavior.Continue;
        }

        VisualTreeHelper.HitTest(viewport, null, Callback, new PointHitTestParameters(position));
        if (hits.Count == 0)
        {
            VisualTreeHelper.HitTest(viewport, null, PlanarRegionCallback, new PointHitTestParameters(position));
            if (planarRegionHit != null && !_createPlateMode && !_createWallMode && !_createSpatialPlateMode && !_createBarMode)
                selection.ToggleElement(planarRegionHit, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            return;
        }

        var pick = hits.FirstOrDefault(h => h.IsNode);
        if (pick.Tag == null) pick = hits[0];

        if (_createPlateMode || _createWallMode || _createSpatialPlateMode)
        {
            if (!pick.IsNode) return;
            if (!_pendingFrameNodes.Contains(pick.Tag)) _pendingFrameNodes.Add(pick.Tag);
            if (_pendingFrameNodes.Count < RequiredFrameNodeCount) return;

            if (_createPlateMode) PlateFrameRequested?.Invoke(_pendingFrameNodes[0]);
            else if (_createWallMode) WallFrameRequested?.Invoke(_pendingFrameNodes[0], _pendingFrameNodes[1]);
            else SpatialPlateFrameRequested?.Invoke(_pendingFrameNodes[0], _pendingFrameNodes[1], _pendingFrameNodes[2]);

            _pendingFrameNodes.Clear();
            return;
        }

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

    DateTime _lastMosaicHover;

    /// <summary>Значение КЭ под курсором — в подсказку у курсора и в легенду мозаики (только пластины).</summary>
    void UpdateMosaicHover(MouseEventArgs e)
    {
        if (VM is not { } vm) return;
        if (vm.MosaicShellMeshes.Count == 0 && vm.MosaicBarSegments.Count == 0)
        {
            vm.Mosaic.SetHover(null);
            PlaceMosaicHoverTip(vm, e);
            return;
        }
        // Перебор треугольников сцены на каждое движение мыши заметен на больших схемах.
        var now = DateTime.UtcNow;
        if ((now - _lastMosaicHover).TotalMilliseconds < 40) { PlaceMosaicHoverTip(vm, e); return; }
        _lastMosaicHover = now;

        if (vm.MosaicBarSegments.Count > 0)
        {
            vm.Mosaic.SetHover(BarTagNear(vm, e.GetPosition(viewport)));
            PlaceMosaicHoverTip(vm, e);
            return;
        }

        string? tag = null;
        HitTestResultBehavior Callback(HitTestResult result)
        {
            if (result is RayMeshGeometry3DHitTestResult hit &&
                vm.ShellTagAt(hit.MeshHit, hit.VertexIndex1) is { } found)
            {
                tag = found;
                return HitTestResultBehavior.Stop;
            }
            return HitTestResultBehavior.Continue;
        }
        VisualTreeHelper.HitTest(viewport, null, Callback, new PointHitTestParameters(e.GetPosition(viewport)));
        vm.Mosaic.SetHover(tag);
        PlaceMosaicHoverTip(vm, e);
    }

    /// <summary>Стержень мозаики, ближайший к курсору на экране (не дальше нескольких пикселей); null — рядом нет.
    /// У линий нет заливки, поэтому вместо луча по сцене — расстояние до проекции стержня.</summary>
    string? BarTagNear(Fem3DVM vm, Point cursor)
    {
        const double tolerance = 6;
        var transform = Viewport3DHelper.GetTotalTransform(viewport.Viewport);
        bool Project(Point3D p, out Point screen)
        {
            var q = transform.Transform(new Point4D(p.X, p.Y, p.Z, 1));
            screen = q.W > 1e-9 ? new Point(q.X / q.W, q.Y / q.W) : default;
            return q.W > 1e-9; // точка за камерой
        }

        string? best = null;
        double bestDistance = tolerance;
        foreach (var (tag, p1, p2) in vm.MosaicBarSegments)
        {
            if (!Project(p1, out var a) || !Project(p2, out var b)) continue;
            var ab = b - a;
            double length2 = ab.LengthSquared;
            double t = length2 > 1e-9 ? Math.Clamp(Vector.Multiply(cursor - a, ab) / length2, 0, 1) : 0;
            double distance = (cursor - (a + ab * t)).Length;
            if (distance <= bestDistance) { bestDistance = distance; best = tag; }
        }
        return best;
    }

    /// <summary>Подсказка со значением КЭ следует за курсором; у правого и нижнего края — по другую сторону от него.</summary>
    void PlaceMosaicHoverTip(Fem3DVM vm, MouseEventArgs e)
    {
        if (vm.Mosaic.HoverText.Length == 0) { mosaicHoverTip.Visibility = Visibility.Collapsed; return; }
        mosaicHoverTip.Visibility = Visibility.Visible;
        mosaicHoverTip.UpdateLayout();
        var p = e.GetPosition(viewport);
        double x = p.X + 14, y = p.Y + 18;
        if (x + mosaicHoverTip.ActualWidth > viewport.ActualWidth) x = p.X - 6 - mosaicHoverTip.ActualWidth;
        if (y + mosaicHoverTip.ActualHeight > viewport.ActualHeight) y = p.Y - 6 - mosaicHoverTip.ActualHeight;
        mosaicHoverTip.Margin = new Thickness(Math.Max(0, x), Math.Max(0, y), 0, 0);
    }

    void Viewport_MouseLeave(object sender, MouseEventArgs e)
    {
        VM?.Mosaic.SetHover(null);
        mosaicHoverTip.Visibility = Visibility.Collapsed;
    }

    void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        UpdateMosaicHover(e);

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

    // Сетка КЭ по умолчанию выключена: рёбра пластин большой импортированной схемы (сотни тысяч
    // отрезков) пересчитываются при каждом движении камеры и тормозят вращение. Выбор — на сеанс.
    static bool s_showGrid;

    LinesVisual3D? CreateShellEdgesVisual() => VM?.ShellEdgePoints is { Count: > 0 } edgePts
        ? new LinesVisual3D { Points = edgePts, Color = Colors.DimGray, Thickness = 0.5 }
        : null;

    void GridToggle(object sender, RoutedEventArgs e)
    {
        s_showGrid = showGridCheck.IsChecked == true;
        if (VM != null) VM.ShowShellEdges = s_showGrid;   // рёбра строятся при первом включении
        if (!IsLoaded) return;
        _shellEdgesVisual ??= CreateShellEdgesVisual();
        ApplyGridVisuals();
    }

    void ApplyGridVisuals()
    {
        FemGridVisuals.Apply(
            viewport.Children,
            showGridCheck.IsChecked == true,
            _shellEdgesVisual,
            _meshVisual,
            _meshNodeGlyphVisual,
            _planarMeshEdgesVisual,
            _planarMeshNodeGlyphVisual);
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

    /// <summary>Включает общий показ сетки после построения расчётной сетки.</summary>
    public void ShowMeshOverlay() => showGridCheck.IsChecked = true;

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

        (string NodeTag, int Dof)? kinematicHit = null;
        HitTestResultBehavior KinematicCallback(HitTestResult result)
        {
            if (result is RayMeshGeometry3DHitTestResult meshHit &&
                _kinematicPickTargets.TryGetValue(meshHit.VisualHit, out var target))
            {
                kinematicHit = target;
                return HitTestResultBehavior.Stop;
            }
            return HitTestResultBehavior.Continue;
        }
        VisualTreeHelper.HitTest(viewport, null, KinematicCallback, new PointHitTestParameters(position));
        if (kinematicHit is { } kinematicTarget)
        {
            _contextMenuKinematicTarget = kinematicTarget;
            _contextMenuLoadTarget = null;
            var kinematicMenu = (ContextMenu)Resources["LoadContextMenu"];
            kinematicMenu.PlacementTarget = viewport;
            kinematicMenu.IsOpen = true;
            e.Handled = true;
            return;
        }

        (bool IsNodeLoad, string Tag)? loadHit = null;
        HitTestResultBehavior LoadCallback(HitTestResult result)
        {
            if (result is RayMeshGeometry3DHitTestResult meshHit &&
                _loadPickTargets.TryGetValue(meshHit.VisualHit, out var target))
            {
                loadHit = target;
                return HitTestResultBehavior.Stop;
            }
            return HitTestResultBehavior.Continue;
        }
        VisualTreeHelper.HitTest(viewport, null, LoadCallback, new PointHitTestParameters(position));
        if (loadHit is { } loadTarget)
        {
            _contextMenuLoadTarget = loadTarget;
            _contextMenuKinematicTarget = null;
            var loadMenu = (ContextMenu)Resources["LoadContextMenu"];
            loadMenu.PlacementTarget = viewport;
            loadMenu.IsOpen = true;
            e.Handled = true;
            return;
        }

        (bool IsNode, string Tag)? hit = null;
        HitTestResultBehavior Callback(HitTestResult result)
        {
            if (result is RayMeshGeometry3DHitTestResult meshHit &&
                TryGetPickTarget(meshHit, out var target))
            {
                hit = target;
                return HitTestResultBehavior.Stop;
            }
            return HitTestResultBehavior.Continue;
        }
        VisualTreeHelper.HitTest(viewport, null, Callback, new PointHitTestParameters(position));
        if (hit is { } target)
        {
            _contextMenuTargetTag = target.Tag;
            var menu = (ContextMenu)Resources[target.IsNode ? "NodeContextMenu" : "MemberContextMenu"];
            menu.PlacementTarget = viewport;
            menu.IsOpen = true;
            e.Handled = true;
            return;
        }

        string? planarRegionHit = null;
        HitTestResultBehavior PlanarRegionCallback(HitTestResult prResult)
        {
            if (prResult is RayMeshGeometry3DHitTestResult prMeshHit &&
                _planarRegionPickTargets.TryGetValue(prMeshHit.VisualHit, out var prTag))
            {
                planarRegionHit = prTag;
                return HitTestResultBehavior.Stop;
            }
            return HitTestResultBehavior.Continue;
        }
        VisualTreeHelper.HitTest(viewport, null, PlanarRegionCallback, new PointHitTestParameters(position));
        if (planarRegionHit is not { } prHitTag) return;

        _contextMenuTargetTag = prHitTag;
        var prMenu = (ContextMenu)Resources["PlanarRegionContextMenu"];
        prMenu.PlacementTarget = viewport;
        prMenu.IsOpen = true;
        e.Handled = true;
    }

    void LoadEditCtx_Click(object sender, RoutedEventArgs e)
    {
        if (Editor is not { } editor) return;
        if (VM?.SelectedDiagramLoadSource?.LoadCase is { } loadCase)
            editor.SelectedLoadCase = loadCase;

        if (_contextMenuKinematicTarget is { } kinematicTarget)
        {
            OpenKinematicLoadDialog(null, kinematicTarget.NodeTag);
            return;
        }
        if (_contextMenuLoadTarget is not { } target) return;
        if (target.IsNodeLoad) OpenNodeLoadDialog(null, target.Tag);
        else OpenMemberLoadDialog(null, target.Tag);
    }

    void LoadDeleteCtx_Click(object sender, RoutedEventArgs e)
    {
        if (Editor is not { } editor || VM?.SelectedDiagramLoadSource?.LoadCase is not { } loadCase) return;

        if (_contextMenuKinematicTarget is { } kinematicTarget)
        {
            var kinematicNode = editor.Session.Nodes.FirstOrDefault(n => n.NodeTag == kinematicTarget.NodeTag);
            if (kinematicNode != null) editor.DeleteKinematicLoad(kinematicNode, loadCase, kinematicTarget.Dof);
            return;
        }
        if (_contextMenuLoadTarget is not { } target) return;

        if (target.IsNodeLoad)
        {
            var node = editor.Session.Nodes.FirstOrDefault(n => n.NodeTag == target.Tag);
            if (node != null) editor.DeleteNodeLoad(node, loadCase);
        }
        else
        {
            var member = editor.Session.Members.FirstOrDefault(m => m.ElemTag == target.Tag);
            if (member != null) editor.DeleteMemberLoad(member, loadCase);
        }
    }

    void MemberDeleteCtx_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuTargetTag is not { } tag) return;
        MemberDeleteRequested?.Invoke(tag);
    }

    void PlanarRegionEditCtx_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuTargetTag is not { } tag) return;
        PlanarRegionEditRequested?.Invoke(tag);
    }

    void PlanarRegionDeleteCtx_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuTargetTag is not { } tag) return;
        PlanarRegionDeleteRequested?.Invoke(tag);
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

    void MemberRotationCtx_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuTargetTag is not { } tag) return;
        MemberRotationRequested?.Invoke(tag);
    }

    void MemberForcesCtx_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuTargetTag is not { } tag) return;
        MemberForcesRequested?.Invoke(tag);
    }

    void NodeLoadTool_Click(object sender, RoutedEventArgs e)
        => OpenNodeLoadDialog(Editor?.Selection?.SelectedNodeTags, null);

    void KinematicLoadTool_Click(object sender, RoutedEventArgs e)
        => OpenKinematicLoadDialog(Editor?.Selection?.SelectedNodeTags, null);

    void MemberLoadTool_Click(object sender, RoutedEventArgs e)
        => OpenMemberLoadDialog(Editor?.Selection?.SelectedElemTags, null);

    void NodeLoadCtx_Click(object sender, RoutedEventArgs e)
        => OpenNodeLoadDialog(null, _contextMenuTargetTag);

    void KinematicLoadCtx_Click(object sender, RoutedEventArgs e)
        => OpenKinematicLoadDialog(null, _contextMenuTargetTag);

    void MemberLoadCtx_Click(object sender, RoutedEventArgs e)
        => OpenMemberLoadDialog(null, _contextMenuTargetTag);

    void OpenNodeLoadDialog(IEnumerable<string>? selectedTags, string? contextTag)
    {
        if (Editor is not { } editor) return;
        var tags = selectedTags?.ToHashSet(StringComparer.Ordinal) ?? [];
        if (tags.Count == 0 && contextTag is { } tag) tags.Add(tag);
        var nodes = editor.Session.Nodes.Where(node => tags.Contains(node.NodeTag)).ToList();
        if (nodes.Count == 0)
        {
            MessageBox.Show(Loc.S("FemNodeLoadSelectNodes"), Loc.S("FemNodeLoadToolTip"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new FemNodeLoadDialog(nodes, editor) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    void OpenKinematicLoadDialog(IEnumerable<string>? selectedTags, string? contextTag)
    {
        if (Editor is not { } editor) return;
        var tags = selectedTags?.ToHashSet(StringComparer.Ordinal) ?? [];
        if (tags.Count == 0 && contextTag is { } tag) tags.Add(tag);
        var nodes = editor.Session.Nodes.Where(node => tags.Contains(node.NodeTag)).ToList();
        if (nodes.Count == 0)
        {
            MessageBox.Show(Loc.S("FemNodeLoadSelectNodes"), Loc.S("FemKinematicLoadToolTip"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new FemKinematicLoadDialog(nodes, editor) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    void OpenMemberLoadDialog(IEnumerable<string>? selectedTags, string? contextTag)
    {
        if (Editor is not { } editor) return;
        var tags = selectedTags?.ToHashSet(StringComparer.Ordinal) ?? [];
        if (tags.Count == 0 && contextTag is { } tag) tags.Add(tag);
        var members = editor.Session.Members
            .Where(member => member.ElemType == "beam" && tags.Contains(member.ElemTag)).ToList();
        if (members.Count == 0)
        {
            MessageBox.Show(Loc.S("FemMemberLoadSelectMembers"), Loc.S("FemMemberLoadToolTip"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new FemMemberLoadDialog(members, editor) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    public event Action<string>? MemberDeleteRequested;
    public event Action<string>? MemberSplitRequested;
    public event Action<string>? MemberSectionEditRequested;
    public event Action<string>? MemberPropertiesRequested;
    public event Action<string>? MemberRotationRequested;
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

    void NodeDeleteCtx_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuTargetTag is not { } tag) return;
        NodeDeleteRequested?.Invoke([tag]);
    }

    public event Action<string, double, double, double>? NodeMoveRequested;
    public event Action<string, double, double, double>? NodeCopyRequested;
    public event Action<string>? NodePropertiesRequested;
    public event Action<IReadOnlyList<string>>? NodeDeleteRequested;
}
