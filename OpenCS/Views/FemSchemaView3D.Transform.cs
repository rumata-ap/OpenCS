using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>
/// Команды «Переместить» (M) и «Копировать» (C) в духе AutoCAD: командная строка внизу вида, базовая и вторая
/// точки щелчком с привязкой к узлу (ближайший в пределах <see cref="SnapPixels"/>) или ввод с клавиатуры,
/// резиновая линия от базовой точки и «призрак» переносимых элементов. Без привязки точка курсора лежит в плоскости,
/// проходящей через базовую точку перпендикулярно взгляду. Выполняет операции страница схемы
/// (<see cref="FragmentTransformRequested"/>).
/// </summary>
public partial class FemSchemaView3D
{
    const double SnapPixels = 12;

    FemTransformCommand? _transform;
    LinesVisual3D? _transformRubber;
    LinesVisual3D? _transformGhost;
    PointsVisual3D? _transformSnapMarker;
    Point3D? _transformCursor;
    /// <summary>Отрезки переносимых элементов в исходном положении.</summary>
    List<Point3D> _transformGhostBase = [];

    /// <summary>Перемещение (copies = 0) или копирование: теги узлов и КонЭ, вектор, число копий.</summary>
    public event Action<IReadOnlyCollection<string>, IReadOnlyCollection<string>, Vector3D, int>? FragmentTransformRequested;

    public bool IsTransformActive => _transform != null;

    void MoveTool_Click(object sender, RoutedEventArgs e) => StartTransformFromSelection(FemTransformKind.Move);
    void CopyTool_Click(object sender, RoutedEventArgs e) => StartTransformFromSelection(FemTransformKind.Copy);
    void TransformClose_Click(object sender, RoutedEventArgs e) => EndTransform();

    void StartTransformFromSelection(FemTransformKind kind)
    {
        var selection = Editor?.Selection;
        StartTransform(kind, selection?.SelectedNodeTags.ToList() ?? [], selection?.SelectedElemTags.ToList() ?? []);
    }

    /// <summary>Запускает команду для узлов и КонЭ (из панели, меню объекта, дерева).</summary>
    public void StartTransform(FemTransformKind kind, IReadOnlyCollection<string> nodeTags, IReadOnlyCollection<string> memberTags)
    {
        if (VM is not { EditMode: true } || Editor is not { } editor) return;
        var members = editor.Session.Members.Select(m => m.ElemTag).ToHashSet(StringComparer.Ordinal);
        memberTags = memberTags.Where(members.Contains).ToList();
        if (nodeTags.Count + memberTags.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), Loc.S("FemTransformSelectFirst"),
                Loc.S(kind == FemTransformKind.Move ? "FemFragmentMoveTitle" : "FemFragmentCopyTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        EndTransform();
        editor.CreateNodeMode = editor.CreateBarMode = editor.CreatePlateMode = false;
        editor.CreateWallMode = editor.CreateSpatialPlateMode = false;

        var command = new FemTransformCommand(kind, nodeTags, memberTags);
        command.MoveRequested += v => FragmentTransformRequested?.Invoke(command.NodeTags, command.MemberTags, v, 0);
        command.CopyRequested += (v, n) => FragmentTransformRequested?.Invoke(command.NodeTags, command.MemberTags, v, n);
        command.Finished += EndTransform;
        command.PropertyChanged += (_, _) => UpdateTransformPreview();
        _transform = command;
        _transformGhostBase = GhostSegments(nodeTags, memberTags);
        transformBar.DataContext = command;
        transformIcon.Source = (ImageSource)FindResource(kind == FemTransformKind.Move ? "CmdIcon_Move" : "CmdIcon_Copy");
        transformStatus.Text = "";
        transformInput.Text = "";
        transformBar.Visibility = Visibility.Visible;
        FocusTransformInput();
    }

    void EndTransform()
    {
        if (_transform is not { } command) return;
        _transform = null;
        command.Finish();
        transformBar.Visibility = Visibility.Collapsed;
        transformBar.DataContext = null;
        _transformCursor = null;
        foreach (var visual in new Visual3D?[] { _transformRubber, _transformGhost, _transformSnapMarker })
            if (visual != null) viewport.Children.Remove(visual);
        _transformRubber = _transformGhost = null;
        _transformSnapMarker = null;
        viewport.Focus();
    }

    void FocusTransformInput() =>
        Dispatcher.BeginInvoke(() => { if (_transform != null) Keyboard.Focus(transformInput); },
            System.Windows.Threading.DispatcherPriority.Input);

    void TransformInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_transform is not { } command) return;
        switch (e.Key)
        {
            case Key.Enter:
                command.Submit(transformInput.Text, _transformCursor);
                transformInput.Text = "";
                e.Handled = true;
                break;
            case Key.Escape:
                EndTransform();
                e.Handled = true;
                break;
            case Key.F8:
                command.Ortho = !command.Ortho;
                e.Handled = true;
                break;
        }
    }

    /// <summary>Клавиши вида во время команды: Esc — завершить, F8 — Орто. Вне команды: M / C — запустить.</summary>
    bool HandleTransformKey(KeyEventArgs e)
    {
        if (_transform is { } command)
        {
            if (e.Key == Key.Escape) { EndTransform(); return true; }
            if (e.Key == Key.F8) { command.Ortho = !command.Ortho; return true; }
            if (e.Key == Key.Enter) { command.Submit(transformInput.Text, _transformCursor); transformInput.Text = ""; return true; }
            return false;
        }
        if (Keyboard.Modifiers != ModifierKeys.None || VM is not { EditMode: true }) return false;
        // Клавиши M и C по положению — в любой раскладке (в русской это «Ь» и «С»).
        var kind = e.Key switch { Key.M => FemTransformKind.Move, Key.C => FemTransformKind.Copy, _ => (FemTransformKind?)null };
        if (kind == null) return false;
        StartTransformByHotkey(kind.Value);
        return true;
    }

    /// <summary>Буква запустившей команду клавиши не должна попасть в командную строку: её текст приходит после
    /// KeyDown — пропускаем его до конца текущей обработки ввода.</summary>
    bool _swallowHotkeyText;

    void StartTransformByHotkey(FemTransformKind kind)
    {
        _swallowHotkeyText = true;
        Dispatcher.BeginInvoke(() => _swallowHotkeyText = false, System.Windows.Threading.DispatcherPriority.Background);
        StartTransformFromSelection(kind);
    }

    /// <summary>Горячие клавиши по букве: кириллические «М» (переместить) и «С» (копировать) в русской раскладке.</summary>
    static FemTransformKind? HotkeyByText(string text) => text switch
    {
        "м" or "М" => FemTransformKind.Move,
        "с" or "С" => FemTransformKind.Copy,
        _ => null,
    };

    /// <summary>Набор с клавиатуры, пока фокус во viewport: во время команды уходит в командную строку, вне её —
    /// кириллические «М» / «С» запускают команду.</summary>
    void TransformPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;
        if (_swallowHotkeyText)
        {
            _swallowHotkeyText = false;
            e.Handled = true;
            return;
        }
        if (_transform == null)
        {
            if (viewport.IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.None
                && VM is { EditMode: true } && HotkeyByText(e.Text) is { } kind)
            {
                StartTransformFromSelection(kind);
                e.Handled = true;
            }
            return;
        }
        if (transformInput.IsKeyboardFocusWithin) return;
        transformInput.Text += e.Text;
        transformInput.CaretIndex = transformInput.Text.Length;
        Keyboard.Focus(transformInput);
        e.Handled = true;
    }

    /// <summary>Щелчок по виду во время команды. True — щелчок обработан командой.</summary>
    bool HandleTransformClick(Point screen)
    {
        if (_transform is not { } command) return false;
        if (TransformPointAt(screen, out _) is { } point) command.PickPoint(point);
        FocusTransformInput();
        return true;
    }

    /// <summary>Движение мыши во время команды: привязка, резиновая линия, призрак.</summary>
    bool HandleTransformMove(Point screen)
    {
        if (_transform == null) return false;
        _transformCursor = TransformPointAt(screen, out bool snapped);
        UpdateSnapMarker(snapped ? _transformCursor : null);
        UpdateTransformPreview();
        return true;
    }

    /// <summary>Точка под курсором: ближайший узел в пределах <see cref="SnapPixels"/> или пересечение с плоскостью
    /// через базовую точку (без неё — через центр взгляда) перпендикулярно направлению взгляда.</summary>
    Point3D? TransformPointAt(Point screen, out bool snapped)
    {
        snapped = false;
        if (VM is not { } vm || viewport.Camera is not ProjectionCamera camera) return null;
        var toScreen = Viewport3DHelper.GetTotalTransform(viewport.Viewport);
        double best = SnapPixels * SnapPixels;
        Point3D? nearest = null;
        foreach (var (_, position) in vm.NodeProxies)
        {
            var p = toScreen.Transform(position);
            double dx = p.X - screen.X, dy = p.Y - screen.Y, d = dx * dx + dy * dy;
            if (d < best) { best = d; nearest = position; }
        }
        if (nearest != null) { snapped = true; return nearest; }
        var origin = _transform?.BasePoint ?? camera.Position + camera.LookDirection;
        return Viewport3DHelper.UnProject(viewport.Viewport, screen, origin, camera.LookDirection);
    }

    void UpdateSnapMarker(Point3D? point)
    {
        if (point is not { } p)
        {
            if (_transformSnapMarker != null) viewport.Children.Remove(_transformSnapMarker);
            return;
        }
        _transformSnapMarker ??= new PointsVisual3D { Color = Colors.Gold, Size = 9 };
        _transformSnapMarker.Points = new Point3DCollection([p]);
        EnsureChild(_transformSnapMarker);
    }

    void UpdateTransformPreview()
    {
        if (_transform is not { } command) return;
        if (command.BasePoint is not { } basePoint || _transformCursor is not { } cursor
            || command.PreviewOffset(cursor) is not { } offset)
        {
            transformStatus.Text = "";
            if (_transformRubber != null) viewport.Children.Remove(_transformRubber);
            if (_transformGhost != null) viewport.Children.Remove(_transformGhost);
            return;
        }
        transformStatus.Text = FemTransformCommand.Describe(offset);

        _transformRubber ??= new LinesVisual3D { Color = Colors.Gold, Thickness = 2 };
        _transformRubber.Points = new Point3DCollection([basePoint, basePoint + offset]);
        EnsureChild(_transformRubber);

        _transformGhost ??= new LinesVisual3D { Color = Color.FromRgb(0xE8, 0x87, 0x0E), Thickness = 1.5 };
        _transformGhost.Points = new Point3DCollection(_transformGhostBase.Select(p => p + offset));
        EnsureChild(_transformGhost);
    }

    /// <summary>Пересборка вида очищает Children — вспомогательные объекты команды возвращаются при следующем показе.</summary>
    void EnsureChild(Visual3D visual)
    {
        if (!viewport.Children.Contains(visual)) viewport.Children.Add(visual);
    }

    /// <summary>Отрезки переносимого: стержни — от узла к узлу, пластины — контур, отдельные узлы — крестик.</summary>
    List<Point3D> GhostSegments(IReadOnlyCollection<string> nodeTags, IReadOnlyCollection<string> memberTags)
    {
        var segments = new List<Point3D>();
        if (VM is not { } vm || Editor is not { } editor) return segments;
        var positions = vm.NodeProxies.ToDictionary(n => n.Tag, n => n.Position, StringComparer.Ordinal);
        var memberSet = memberTags.ToHashSet(StringComparer.Ordinal);
        foreach (var member in editor.Session.Members.Where(m => memberSet.Contains(m.ElemTag)))
        {
            var points = (JsonSerializer.Deserialize<int[]>(member.NodeIdsJson) ?? [])
                .Select(id => positions.TryGetValue(id.ToString(), out var p) ? p : (Point3D?)null)
                .OfType<Point3D>().ToList();
            int edges = points.Count > 2 ? points.Count : points.Count - 1;
            for (int i = 0; i < edges; i++)
            {
                segments.Add(points[i]);
                segments.Add(points[(i + 1) % points.Count]);
            }
        }
        const double r = 0.15;
        foreach (var tag in nodeTags)
        {
            if (!positions.TryGetValue(tag, out var p)) continue;
            segments.AddRange([p + new Vector3D(-r, 0, 0), p + new Vector3D(r, 0, 0),
                p + new Vector3D(0, -r, 0), p + new Vector3D(0, r, 0),
                p + new Vector3D(0, 0, -r), p + new Vector3D(0, 0, r)]);
        }
        return segments;
    }
}
