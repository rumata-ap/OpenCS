using System.Windows;
using System.Windows.Controls;
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

    /// <summary>Импортные нагрузки не правятся в ячейках.</summary>
    void Grid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is FemLoadsRow { IsEditable: false }) e.Cancel = true;
    }
}
