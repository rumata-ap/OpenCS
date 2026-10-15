using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>
/// Вкладка «Загружения»: таблица загружений и нагрузки активного загружения (<see cref="FemLoadsTabVM"/>). Внешний
/// DataContext — редактор схемы; модель вкладки создаётся при его смене.
/// </summary>
public partial class FemLoadCasesPanel : UserControl
{
    FemLoadsTabVM? _vm;

    public FemLoadCasesPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            _vm = DataContext is FemSchemaEditorVM editor ? new FemLoadsTabVM(editor) : null;
            if (_vm != null) _vm.LoadCaseRenamed += lc => LoadCaseRenamed?.Invoke(lc);
            root.DataContext = _vm;
        };
        foreach (var grid in new[] { loadCasesGrid, nodeLoadsGrid, memberLoadsGrid, elementLoadsGrid, kinematicLoadsGrid })
            grid.CellEditEnding += CommitRowOnCellEnd;
    }

    /// <summary>
    /// Правка применяется по выходу из ячейки (Enter, Tab, щелчок мимо), а не только при уходе со строки: иначе значение
    /// оставалось в строке DataGrid и не доходило до схемы, если фокус уходил в 3D или на другую вкладку.
    /// </summary>
    void CommitRowOnCellEnd(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || sender is not DataGrid grid) return;
        Dispatcher.BeginInvoke(() => grid.CommitEdit(DataGridEditingUnit.Row, true),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>Активное загружение переименовано в таблице (страница схемы запоминает новое имя).</summary>
    public event Action<CScore.Fem.FemLoadCase>? LoadCaseRenamed;

    void AddLoadCase_Click(object sender, RoutedEventArgs e) => _vm?.AddLoadCase();

    void DeleteLoadCase_Click(object sender, RoutedEventArgs e) => _vm?.DeleteLoadCase();

    void AddNodeLoads_Click(object sender, RoutedEventArgs e) => _vm?.AddNodeLoadsToSelection();

    void AddMemberLoads_Click(object sender, RoutedEventArgs e) => _vm?.AddMemberLoadsToSelection();

    void AddKinematicLoads_Click(object sender, RoutedEventArgs e) => _vm?.AddKinematicLoadsToSelection();

    void AddAreaLoad_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        var dialog = new FemAreaLoadDialog(_vm.Editor.MemberGroups) { Owner = Window.GetWindow(this) };
        dialog.Validate = d =>
        {
            _vm.AddAreaLoad(d.TargetMode, d.Group, d.TagsText, d.PressurePa, d.Direction);
            return _vm.Status;
        };
        dialog.ShowDialog();
    }

    void DeleteRows_Click(object sender, RoutedEventArgs e)
    {
        if (_vm != null && (sender as FrameworkElement)?.Tag is DataGrid grid)
            _vm.DeleteRows(grid.SelectedItems.OfType<FemLoadsRow>().ToList());
    }

    /// <summary>
    /// Строка таблицы — активное загружение. Смена откладывается до конца обработки щелчка: она перечитывает таблицы,
    /// а повторный вход в выделение DataGrid (щелчок по ячейке-списку другой строки) ронял его автоматизацию
    /// (ArgumentNullException в DataGridItemAutomationPeer).
    /// </summary>
    void LoadCasesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Выделение, пришедшее из модели (строка уже активна), обратно не отправляется: иначе отложенные вызовы
        // с прежней и новой строкой переключали активное загружение по кругу.
        if (_vm is not { } vm || loadCasesGrid.SelectedItem is not FemLoadCaseRow row || row.IsActive) return;
        Dispatcher.BeginInvoke(() =>
        {
            // Применяется только последнее выделение пользователя.
            if (ReferenceEquals(vm, _vm) && ReferenceEquals(loadCasesGrid.SelectedItem, row) && !row.IsActive
                && vm.LoadCaseRows.Contains(row))
                vm.SelectedLoadCaseRow = row;
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>Правка с одного щелчка: ячейка сразу входит в правку, у списка раскрывается выбор.</summary>
    void Cell_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGridCell { IsEditing: false, IsReadOnly: false } cell) return;
        if (cell.DataContext is FemLoadsRow { IsEditable: false }) return;
        if (FindParent<DataGrid>(cell) is not { } grid) return;
        if (!cell.IsFocused) cell.Focus();
        if (grid.SelectionUnit != DataGridSelectionUnit.Cell && FindParent<DataGridRow>(cell) is { IsSelected: false } row
            && grid.SelectionMode == DataGridSelectionMode.Single)
            row.IsSelected = true;
        if (cell.Column is not DataGridComboBoxColumn) return;
        grid.BeginEdit(e);
        Dispatcher.BeginInvoke(() =>
        {
            if (FindChild<ComboBox>(cell) is { } combo) combo.IsDropDownOpen = true;
        }, System.Windows.Threading.DispatcherPriority.Loaded);
        e.Handled = true;
    }

    static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        for (var p = VisualTreeHelper.GetParent(child); p != null; p = VisualTreeHelper.GetParent(p))
            if (p is T t) return t;
        return null;
    }

    static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) return t;
            if (FindChild<T>(c) is { } r) return r;
        }
        return null;
    }

    /// <summary>ПКМ по загружениям: «Вид нагрузки (СП 20)» выделенным строкам одной отменой.</summary>
    void LoadCasesGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var rows = loadCasesGrid.SelectedItems.OfType<FemLoadCaseRow>().ToList();
        if (_vm is not { } vm || rows.Count == 0) { e.Handled = true; return; }
        var kind = new MenuItem { Header = Utilites.Loc.S("FemLoadsSetKindMenu"), Icon = Helpers.MenuIcon.Create("CmdIcon_Edit", loadCasesGrid) };
        foreach (var choice in FemLoadsTabVM.Sp20Types)
        {
            var item = new MenuItem { Header = choice.Label, IsChecked = rows.All(r => r.Sp20Type == choice.Key) };
            item.Click += (_, _) => vm.SetSp20Type(rows, choice.Key);
            kind.Items.Add(item);
        }
        var menu = new ContextMenu();
        menu.Items.Add(kind);
        loadCasesGrid.ContextMenu = menu;
    }

    /// <summary>Импортные нагрузки не правятся в ячейках.</summary>
    void Grid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is FemLoadsRow { IsEditable: false }) e.Cancel = true;
    }
}
