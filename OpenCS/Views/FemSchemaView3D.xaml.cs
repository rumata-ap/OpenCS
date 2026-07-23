using System.ComponentModel;
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

    readonly Dictionary<Visual3D, (bool IsNode, string Tag)> _pickTargets = new();
    readonly Dictionary<Visual3D, (bool IsNodeLoad, string Tag)> _loadPickTargets = new();
    PointsVisual3D? _editNodesVisual;
    string? _contextMenuTargetTag;
    (bool IsNodeLoad, string Tag)? _contextMenuLoadTarget;

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
            e.PropertyName == nameof(Fem3DVM.DiagramGlyphs) ||
            e.PropertyName == nameof(Fem3DVM.MemberLoadGlyphs) ||
            e.PropertyName == nameof(Fem3DVM.ShowSectionGlyphs) ||
            e.PropertyName == nameof(Fem3DVM.ShowLoadValues))
            BuildVisuals();
    }

    void BuildVisuals()
    {
        viewport.Children.Clear();
        _meshVisual = null;
        _meshNodeGlyphVisual = null;
        _loadPickTargets.Clear();
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
                    DrawForce(node, axis * glyph.Sign, side, up,
                        VM.ShowLoadValues ? FormatComponentValue(glyph.Component, glyph.Value, moment: false) : null,
                        Colors.Crimson);
                    AddNodeLoadPickTarget(node, glyph.NodeId);
                    break;
                case FemDiagramGlyphKind.Moment:
                    DrawMoment(node, axis * glyph.Sign, side, up,
                        VM.ShowLoadValues ? FormatComponentValue(glyph.Component, glyph.Value, moment: true) : null,
                        Colors.DarkOrange);
                    AddNodeLoadPickTarget(node, glyph.NodeId);
                    break;
                case FemDiagramGlyphKind.KinematicDisplacement:
                    DrawForce(node, axis * glyph.Sign, side, up,
                        VM.ShowLoadValues ? FormatKinematicValue(glyph.Component, glyph.Value, rotation: false) : null,
                        KinematicColor);
                    break;
                case FemDiagramGlyphKind.KinematicRotation:
                    DrawMoment(node, axis * glyph.Sign, side, up,
                        VM.ShowLoadValues ? FormatKinematicValue(glyph.Component, glyph.Value, rotation: true) : null,
                        KinematicColor);
                    break;
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

    /// <summary>Прозрачная сфера-прокси для выбора узловой нагрузки правым кликом (только в режиме
    /// редактирования). Один и тот же узел может дать несколько прокси (по числу компонент) —
    /// все ведут к одному и тому же узлу, что корректно для удаления/изменения нагрузки целиком.</summary>
    void AddNodeLoadPickTarget(Point3D node, int nodeId)
    {
        if (VM is not { EditMode: true }) return;
        string? nodeTag = Editor?.Session.Nodes.FirstOrDefault(n => n.Id == nodeId)?.NodeTag;
        if (nodeTag == null) return;
        var hitSphere = new SphereVisual3D { Center = node, Radius = 0.15, Fill = new SolidColorBrush(Colors.Transparent) };
        _loadPickTargets[hitSphere] = (true, nodeTag);
        viewport.Children.Add(hitSphere);
    }

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

    void DrawForce(Point3D node, Vector3D direction, Vector3D side, Vector3D up, string? valueText, Color color)
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
    }

    void DrawMoment(Point3D node, Vector3D axis, Vector3D side, Vector3D up, string? valueText, Color color)
    {
        var points = new Point3DCollection();
        for (int i = 0; i <= 16; i++)
        {
            double angle = Math.PI * 1.65 * i / 16;
            points.Add(node + side * (Math.Cos(angle) * 0.32) + up * (Math.Sin(angle) * 0.32));
        }
        AddGlyphLine(color, 2.5, points);
        if (valueText != null) AddValueLabel(node + side * 0.32 + up * 0.32, valueText, color);
        var tip = points[^1];
        var tangent = Vector3D.CrossProduct(axis, tip - node);
        tangent.Normalize();
        AddGlyphLines(color, 2.5,
            [tip, tip - tangent * 0.15 + axis * 0.08, tip, tip - tangent * 0.15 - axis * 0.08]);
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
                DrawLoadArrow(glyph.Start, glyph.LoadAtStart, new Vector3D(0, 0, 1), 0.3);
                if (VM.ShowLoadValues) AddMemberLoadValueLabel(glyph.Start, glyph.LoadAtStart, isIntensity: false);
                AddMemberLoadPickTarget(glyph.Start, glyph.MemberTag);
                continue;
            }

            AddGlyphLine(Colors.DarkGreen, 2.5, [glyph.Start, glyph.End]);
            for (int i = 0; i <= 4; i++)
            {
                double t = i / 4.0;
                var point = glyph.Start + member * t;
                var value = new Vector3D(
                    glyph.LoadAtStart.X + (glyph.LoadAtEnd.X - glyph.LoadAtStart.X) * t,
                    glyph.LoadAtStart.Y + (glyph.LoadAtEnd.Y - glyph.LoadAtStart.Y) * t,
                    glyph.LoadAtStart.Z + (glyph.LoadAtEnd.Z - glyph.LoadAtStart.Z) * t);
                double arrowLength = Math.Clamp(length * 0.22, 0.08, 0.35);
                DrawLoadArrow(point, value, member, arrowLength);
            }
            if (VM.ShowLoadValues)
            {
                AddMemberLoadValueLabel(glyph.Start, glyph.LoadAtStart, isIntensity: true);
                if (glyph.LoadAtEnd != glyph.LoadAtStart)
                    AddMemberLoadValueLabel(glyph.End, glyph.LoadAtEnd, isIntensity: true);
            }
            AddMemberLoadPickTarget(glyph.Start + member * 0.5, glyph.MemberTag);
        }
    }

    /// <summary>Прозрачная сфера-прокси для выбора нагрузки стержня правым кликом (только в режиме
    /// редактирования).</summary>
    void AddMemberLoadPickTarget(Point3D position, string memberTag)
    {
        if (VM is not { EditMode: true }) return;
        var hitSphere = new SphereVisual3D { Center = position, Radius = 0.15, Fill = new SolidColorBrush(Colors.Transparent) };
        _loadPickTargets[hitSphere] = (false, memberTag);
        viewport.Children.Add(hitSphere);
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
    /// при нулевой/параллельной касательной используется запасное глобальное направление.</summary>
    void DrawLoadArrow(Point3D point, Vector3D value, Vector3D memberTangent, double arrowLength)
    {
        double magnitude = value.Length;
        if (!double.IsFinite(magnitude) || magnitude < 1e-12) return;

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
    }

    /// <summary>Рисует контуры сечений и положительные направления локальных Y/Z.</summary>
    void BuildSectionGlyphs()
    {
        if (VM is not { ShowSectionGlyphs: true }) return;

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
                AddGlyphLine(Colors.Gold, 1.5,
                [glyph.Center + y + z, glyph.Center - y + z,
                 glyph.Center - y - z, glyph.Center + y - z,
                 glyph.Center + y + z]);
            }
            else
            {
                foreach (var contour in glyph.Contours)
                {
                    var points = new Point3DCollection(contour.Select(point =>
                        glyph.Center + glyph.LocalY * point.Y + glyph.LocalZ * point.Z));
                    AddGlyphLine(Colors.Gold, 1.5, points);
                }
            }

            double axisLength = Math.Max(halfSize * 1.35, 0.08);
            AddGlyphLine(Colors.LimeGreen, 1.2,
                [glyph.Center, glyph.Center + glyph.LocalY * axisLength]);
            AddGlyphLine(Colors.DeepSkyBlue, 1.2,
                [glyph.Center, glyph.Center + glyph.LocalZ * axisLength]);
        }
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

    void GridToggle(object sender, RoutedEventArgs e)
    {
        // showGridCheck.IsChecked="True" в XAML отличается от значения по умолчанию (False),
        // поэтому WPF поднимает Checked синхронно во время InitializeComponent(), до того как
        // viewport (объявлен ниже в XAML) будет подключён — тот же паттерн, что и в
        // OnDataContextChanged (см. выше).
        if (!IsLoaded) return;
        ApplyGridVisuals();
    }

    void ApplyGridVisuals()
    {
        FemGridVisuals.Apply(
            viewport.Children,
            showGridCheck.IsChecked == true,
            _shellEdgesVisual,
            _meshVisual,
            _meshNodeGlyphVisual);
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

    void LoadEditCtx_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuLoadTarget is not { } target || Editor is not { } editor) return;
        if (VM?.SelectedDiagramLoadSource?.LoadCase is { } loadCase)
            editor.SelectedLoadCase = loadCase;
        if (target.IsNodeLoad) OpenNodeLoadDialog(null, target.Tag);
        else OpenMemberLoadDialog(null, target.Tag);
    }

    void LoadDeleteCtx_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuLoadTarget is not { } target) return;
        if (Editor is not { } editor || VM?.SelectedDiagramLoadSource?.LoadCase is not { } loadCase) return;

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

    public event Action<string, double, double, double>? NodeMoveRequested;
    public event Action<string, double, double, double>? NodeCopyRequested;
    public event Action<string>? NodePropertiesRequested;
}
