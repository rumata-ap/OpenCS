using System.Windows;
using System.Windows.Controls;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemMeshNodesView : UserControl
{
    readonly FemMeshNodesSubNode _node;

    internal FemMeshNodesView(FemMeshNodesSubNode node)
    {
        InitializeComponent();
        _node = node;
        Loaded += async (_, _) =>
        {
            var rows = await node.Owner.LoadMeshNodeRowsAsync();
            meshNodesGrid.ItemsSource = rows;
        };
    }

    void Grid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Двойной щелчок по заголовку столбца сортирует, а не открывает правку.
        if (e.OriginalSource is DependencyObject source && FindParent<DataGridRow>(source) == null) return;
        EditSelected();
    }

    void EditBoundary_Click(object sender, RoutedEventArgs e) => EditSelected();

    /// <summary>Правка ГУ выбранного узла сетки (закрепление и пружины) с записью «вручную».</summary>
    void EditSelected()
    {
        if (meshNodesGrid.SelectedItem is not FemMeshNodeRow row) return;
        var dialog = new FemMeshNodeBoundaryDialog(row) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        int invalidated = _node.Owner.SetMeshNodeBoundary(row, dialog.Mask, dialog.Stiffnesses);
        var log = (Application.Current?.MainWindow?.DataContext as AppViewModel)?.LogService;
        log?.Info(string.Format(Loc.S("FemMeshNodeBoundarySaved"), row.NodeTag,
            row.SupportText.Length > 0 ? row.SupportText : "—", row.SpringText.Length > 0 ? row.SpringText : "—"));
        if (invalidated > 0) log?.Info(string.Format(Loc.S("ScadBoundaryAnalysesInvalidated"), invalidated));
        meshNodesGrid.Items.Refresh();
    }

    static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        for (var current = child; current != null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }
}
