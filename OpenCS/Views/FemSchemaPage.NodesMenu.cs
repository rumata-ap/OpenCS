using System.Windows.Controls;
using CScore.Fem;

namespace OpenCS.Views;

public partial class FemSchemaPage
{
    /// <summary>
    /// Меню строк вкладки «Узлы» редактора — то же, что у страницы «Узлы» из дерева (одна строка — меню узла дерева,
    /// несколько — выделить, переместить, копировать, удалить). Двойной щелчок здесь оставлен правке ячейки.
    /// </summary>
    void NodesGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var menu = new ContextMenu();
        var rows = nodesGrid.SelectedItems.OfType<FemNode>().ToList();
        bool filled = rows.Count > 0 && _app.FemNodeTreeItemFor(_schema, rows[0]) != null
            && FemMemberTreeMenus.FillNodeRows(menu.Items, _schema, rows, n => _app.FemNodeTreeItemFor(_schema, n)!, _app);
        if (!filled)
        {
            e.Handled = true;
            return;
        }
        nodesGrid.ContextMenu = menu;
    }
}
