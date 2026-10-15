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

/// <summary>
/// Режим «Грани пластин» — выбор сторон контуров пластинчатых КонЭ для линейных связей: наведение подсвечивает
/// ближайшую грань (в пределах <see cref="SnapPixels"/>), щелчок добавляет или снимает её, Shift+щелчок — все грани
/// пластины; ПКМ или кнопка внизу — диалог «Линейная связь». Выбранные грани подсвечиваются и вне режима (выбор из
/// дерева схемы).
/// </summary>
public partial class FemSchemaView3D
{
    bool _edgePickMode;
    FemPlateEdge? _hoverEdge;
    LinesVisual3D? _edgeHoverVisual;
    LinesVisual3D? _edgeSelectionVisual;

    static readonly Color EdgeHoverColor = Colors.Gold;
    static readonly Color EdgeSelectionColor = Color.FromRgb(0xE8, 0x87, 0x0E);

    void EdgePickTool_Click(object sender, RoutedEventArgs e) => SetEdgePickMode(edgePickButton.IsChecked == true);
    void EdgePickClose_Click(object sender, RoutedEventArgs e) => SetEdgePickMode(false);
    void EdgePickAssign_Click(object sender, RoutedEventArgs e) => EditSelectedEdgesLineSupport();

    /// <summary>Пункт «Линейная связь…» меню пластины: режим граней, выбраны все грани этой пластины.</summary>
    void PlateLineSupportCtx_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuTargetTag is not { } tag) return;
        SetEdgePickMode(true);
        if (VM?.PlateEdges.GetValueOrDefault(tag) is { Count: > 0 } edges && Editor?.Selection is { } selection)
            selection.Set([], [], [], [], edges.Select(x => x.Key));
    }

    /// <summary>Включает или выключает режим граней; прочие режимы построения и команда M/C завершаются.</summary>
    public void SetEdgePickMode(bool on)
    {
        if (on && (VM is not { EditMode: true } || Editor == null)) on = false;
        else if (on && VM!.PlateEdges.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), Loc.S("FemLineSupportNoEdges"), Loc.S("FemLineSupportTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            on = false;
        }
        if (on)
        {
            EndTransform();
            Editor!.CreateNodeMode = Editor.CreateBarMode = Editor.CreatePlateMode = false;
            Editor.CreateWallMode = Editor.CreateSpatialPlateMode = false;
        }
        _edgePickMode = on;
        edgePickButton.IsChecked = on;
        edgePickBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        SetHoverEdge(null);
        UpdateEdgeSelectionVisual();
        if (on) viewport.Focus();
    }

    /// <summary>Щелчок в режиме граней. True — обработан.</summary>
    bool HandleEdgePickClick(Point screen)
    {
        if (!_edgePickMode) return false;
        if (EdgeAt(screen) is { } edge && Editor?.Selection is { } selection)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && VM?.PlateEdges.GetValueOrDefault(edge.Key.MemberTag) is { } all)
                selection.TogglePlateEdges(all.Select(x => x.Key).ToList());
            else
                selection.TogglePlateEdge(edge.Key);
        }
        return true;
    }

    /// <summary>Движение мыши в режиме граней — подсветка ближайшей. True — обработано.</summary>
    bool HandleEdgePickMove(Point screen)
    {
        if (!_edgePickMode) return false;
        SetHoverEdge(EdgeAt(screen));
        return true;
    }

    /// <summary>Esc выходит из режима граней.</summary>
    bool HandleEdgePickKey(KeyEventArgs e)
    {
        if (!_edgePickMode || e.Key != Key.Escape) return false;
        SetEdgePickMode(false);
        return true;
    }

    /// <summary>ПКМ в режиме граней: грань под курсором добавляется к выбору, меню — назначить, снять, выйти.</summary>
    bool ShowEdgePickMenu(MouseButtonEventArgs e)
    {
        if (!_edgePickMode || Editor is not { } editor) return false;
        if (EdgeAt(e.GetPosition(viewport)) is { } edge && !editor.Selection.SelectedPlateEdges.Contains(edge.Key))
            editor.Selection.TogglePlateEdge(edge.Key);
        bool any = editor.Selection.SelectedPlateEdges.Count > 0;
        var menu = new ContextMenu { PlacementTarget = viewport };
        var assign = CommandIcons.Item("FemLineSupportMenu");
        assign.IsEnabled = any;
        assign.Click += (_, _) => EditSelectedEdgesLineSupport();
        var remove = CommandIcons.Item("FemLineSupportRemove");
        remove.IsEnabled = any && editor.Selection.SelectedPlateEdges.Any(k => editor.LineSupportAt(k) != null);
        remove.Click += (_, _) => editor.RemoveLineSupports(editor.Selection.SelectedPlateEdges.ToList());
        var exit = CommandIcons.Item("FemLineSupportPickExit");
        exit.Click += (_, _) => SetEdgePickMode(false);
        menu.Items.Add(assign);
        menu.Items.Add(remove);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);
        menu.IsOpen = true;
        e.Handled = true;
        return true;
    }

    /// <summary>Диалог «Линейная связь» для выбранных граней; начальные значения — связь первой грани (если есть).</summary>
    public void EditSelectedEdgesLineSupport()
    {
        if (VM is not { } vm || Editor is not { } editor) return;
        var byKey = vm.PlateEdges.Values.SelectMany(x => x).ToDictionary(x => x.Key);
        var edges = editor.Selection.SelectedPlateEdges.Where(byKey.ContainsKey).Select(k => byKey[k]).ToList();
        if (edges.Count == 0) return;
        var current = edges.Select(x => editor.LineSupportAt(x.Key)).FirstOrDefault(s => s != null);
        var dialog = new FemLineSupportDialog(Loc.S("FemLineSupportTitle"), DescribeEdges(edges), current)
        {
            Owner = Window.GetWindow(this),
        };
        if (dialog.ShowDialog() != true) return;
        editor.SetLineSupports(edges, dialog.Result);
    }

    /// <summary>«Граней: 3, общая длина 12 м — 12, сторона 1; 12, сторона 2; …».</summary>
    public static string DescribeEdges(IReadOnlyCollection<FemPlateEdge> edges)
    {
        string list = string.Join("; ", edges.Take(8).Select(x => x.Key.ToString())) + (edges.Count > 8 ? "; …" : "");
        return string.Format(CultureInfo.CurrentCulture, Loc.S("FemLineSupportEdges"), edges.Count, edges.Sum(x => x.Length), list);
    }

    /// <summary>Ближайшая к курсору грань пластины в пределах <see cref="SnapPixels"/> экранных пикселей.</summary>
    FemPlateEdge? EdgeAt(Point screen)
    {
        if (VM is not { } vm || vm.PlateEdges.Count == 0) return null;
        var toScreen = Viewport3DHelper.GetTotalTransform(viewport.Viewport);
        double best = SnapPixels;
        FemPlateEdge? nearest = null;
        foreach (var edge in vm.PlateEdges.Values.SelectMany(x => x))
        {
            var a = toScreen.Transform(new Point3D(edge.A.X, edge.A.Y, edge.A.Z));
            var b = toScreen.Transform(new Point3D(edge.B.X, edge.B.Y, edge.B.Z));
            double d = DistanceToSegment(screen, new Point(a.X, a.Y), new Point(b.X, b.Y));
            if (d < best) { best = d; nearest = edge; }
        }
        return nearest;
    }

    static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        double l2 = ab.LengthSquared;
        double t = l2 > 0 ? Math.Clamp(Vector.Multiply(p - a, ab) / l2, 0, 1) : 0;
        return (p - (a + ab * t)).Length;
    }

    void SetHoverEdge(FemPlateEdge? edge)
    {
        _hoverEdge = edge;
        if (edge == null)
        {
            if (_edgeHoverVisual != null) viewport.Children.Remove(_edgeHoverVisual);
            return;
        }
        _edgeHoverVisual ??= new LinesVisual3D { Color = EdgeHoverColor, Thickness = 5 };
        _edgeHoverVisual.Points = new Point3DCollection([ToPoint(edge.A), ToPoint(edge.B)]);
        EnsureChild(_edgeHoverVisual);
    }

    /// <summary>Подсветка выбранных граней и строка режима; вызывается при смене выбора и после пересборки сцены.</summary>
    void UpdateEdgeSelectionVisual()
    {
        var keys = Editor?.Selection.SelectedPlateEdges;
        if (_edgePickMode)
            edgePickStatus.Text = string.Format(CultureInfo.CurrentCulture, Loc.S("FemLineSupportPickStatus"), keys?.Count ?? 0);
        if (VM is not { } vm || keys is not { Count: > 0 })
        {
            if (_edgeSelectionVisual != null) viewport.Children.Remove(_edgeSelectionVisual);
            return;
        }
        var set = keys.ToHashSet();
        var points = new Point3DCollection();
        foreach (var edge in vm.PlateEdges.Values.SelectMany(x => x).Where(x => set.Contains(x.Key)))
        {
            points.Add(ToPoint(edge.A));
            points.Add(ToPoint(edge.B));
        }
        _edgeSelectionVisual ??= new LinesVisual3D { Color = EdgeSelectionColor, Thickness = 4.5 };
        _edgeSelectionVisual.Points = points;
        EnsureChild(_edgeSelectionVisual);
        if (_hoverEdge != null) SetHoverEdge(_hoverEdge);
    }

    static Point3D ToPoint(CScore.Planar.PlanarVector3 p) => new(p.X, p.Y, p.Z);
}
