using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CScore.Fem;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemShellsView : UserControl, OpenCS.ViewModels.IContentPage
{
    readonly FemShellsSubNode _node;
    readonly AppViewModel     _app;

    internal FemShellsView(FemShellsSubNode node, AppViewModel app)
    {
        _node = node;
        _app  = app;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            var elems = await node.Owner.LoadShellsAsync();
            shellsGrid.ItemsSource = elems;
        };
    }

    void CreateGroup_Click(object sender, RoutedEventArgs e)
    {
        var selected = shellsGrid.SelectedItems.OfType<FemMember>().ToList();
        if (selected.Count == 0) return;
        _app.CreateFemMemberFromSelection(_node.Owner.Schema, selected);
    }

    async void ShellsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (shellsGrid.SelectedItem is not FemMember member || member.PlanarRegionId is not int regionId) return;
        var region = _app.db.GetPlanarRegions(_node.Owner.Schema.Id).FirstOrDefault(r => r.Id == regionId);
        if (region == null) return;

        var dlg = new PlanarRegionMemberDialog(_app, _node.Owner.Schema, region.Frame, member, region)
        {
            Owner = Window.GetWindow(this)
        };
        dlg.ShowDialog();

        var elems = await _node.Owner.LoadShellsAsync();
        shellsGrid.ItemsSource = elems;
    }

    async void DeleteShell_Click(object sender, RoutedEventArgs e)
    {
        if (shellsGrid.SelectedItem is not FemMember member || member.PlanarRegionId is not int regionId) return;
        _app.db.DeleteFemMember(member);
        _app.db.DeletePlanarRegion(regionId);

        var elems = await _node.Owner.LoadShellsAsync();
        shellsGrid.ItemsSource = elems;
    }
}
