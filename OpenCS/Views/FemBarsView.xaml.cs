using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemBarsView : UserControl
{
    readonly FemBarsSubNode _node;
    readonly AppViewModel   _app;

    internal FemBarsView(FemBarsSubNode node, AppViewModel app)
    {
        _node = node;
        _app  = app;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            var elems = await node.Owner.LoadBarsAsync();
            barsGrid.ItemsSource = elems;
        };
    }

    void NewMember_Click(object sender, RoutedEventArgs e)
    {
        var selected = barsGrid.SelectedItems.OfType<FemElement>().ToList();
        var initialRange = selected.Count > 0
            ? string.Join(" ", selected.Select(el => el.ElemTag))
            : "";
        var dlg = new FemMemberDialog(initialRange);
        if (dlg.ShowDialog() != true) return;
        var ids = LiraElemRangeDialog.ParseRange(dlg.Range);
        if (ids.Count == 0) return;
        _app.CreateFemMemberFromRange(_node.Owner.Schema, ids, dlg.MemberTag, dlg.MemberType);
    }

    void CreateGroup_Click(object sender, RoutedEventArgs e)
    {
        var selected = barsGrid.SelectedItems.OfType<FemElement>().ToList();
        if (selected.Count == 0) return;
        _app.CreateFemMemberFromSelection(_node.Owner.Schema, selected);
    }

    void AutoGroup_Click(object sender, RoutedEventArgs e)
        => _app.AutoGroupFemMembersBySection(_node.Owner.Schema);
}
