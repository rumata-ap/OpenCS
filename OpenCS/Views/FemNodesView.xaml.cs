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
        var rows = nodesGrid.SelectedItems.OfType<FemNode>().ToList();
        if (rows.Count == 0) { e.Handled = true; return; }
        var menu = new ContextMenu();
        if (rows.Count == 1)
            FemMemberTreeMenus.FillNode(menu.Items, ItemFor(rows[0]), _app);
        else
        {
            var tags = rows.Select(r => r.NodeTag).ToList();
            menu.Items.Add(new MenuItem { Header = string.Format(Utilites.Loc.S("FemNodesTableSelected"), tags.Count), IsEnabled = false });
            menu.Items.Add(new Separator());
            menu.Items.Add(FemMemberTreeMenus.Item("FemBoundarySelect", () => _app.InFemSchemaEditor(Schema, page => page.SelectInSchema(tags, []))));
            menu.Items.Add(FemMemberTreeMenus.Item("FemNodeMoveCtx", () => _app.TransformFemFragmentInEditor(Schema, tags, [], copy: false)));
            menu.Items.Add(FemMemberTreeMenus.Item("FemNodeCopyCtx", () => _app.TransformFemFragmentInEditor(Schema, tags, [], copy: true)));
            menu.Items.Add(new Separator());
            menu.Items.Add(FemMemberTreeMenus.Item("FemNodeDeleteCtx", () => _app.InFemSchemaEditor(Schema, page => page.DeleteNodes(tags))));
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
