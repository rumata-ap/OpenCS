using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CScore.Fem;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>
/// Таблица узлов КонЭ схемы (узел дерева «Узлы»). Меню строки — то же, что у узла в дереве; у нескольких строк — общие
/// действия (выделить, переместить, копировать, удалить). Двойной щелчок — свойства узла в редакторе схемы.
/// </summary>
public partial class FemNodesView : UserControl
{
    readonly FemNodesSubNode _node;
    readonly AppViewModel _app;

    internal FemNodesView(FemNodesSubNode node, AppViewModel app)
    {
        _node = node;
        _app = app;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            var nodes = await node.Owner.LoadNodesAsync();
            nodesGrid.ItemsSource = nodes;
        };
    }

    FemSchema Schema => _node.Owner.Schema;

    /// <summary>Узел дерева для строки (меню узла дерева работает с ним); не загружен в дерево — новый.</summary>
    FemNodeTreeItem ItemFor(FemNode n) =>
        _node.Nodes.FirstOrDefault(i => i.Node.NodeTag == n.NodeTag) ?? new FemNodeTreeItem(n, _node.Owner);

    void NodesGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var menu = new ContextMenu();
        if (!FemMemberTreeMenus.FillNodeRows(menu.Items, Schema, nodesGrid.SelectedItems.OfType<FemNode>().ToList(), ItemFor, _app))
        {
            e.Handled = true;
            return;
        }
        nodesGrid.ContextMenu = menu;
    }

    void NodesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject d) return;
        while (d != null && d is not DataGridRow) d = VisualTreeHelper.GetParent(d);
        if (d is not DataGridRow { Item: FemNode node }) return;
        e.Handled = true;
        _app.OpenFemNodeInEditor(Schema, node.NodeTag);
    }
}
