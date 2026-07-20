using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemShellsView : UserControl
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
}
