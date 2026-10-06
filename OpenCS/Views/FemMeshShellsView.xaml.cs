using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemMeshShellsView : UserControl
{
    readonly FemGroupTableTools _groups;
    readonly FemMeshShellsSubNode _node;

    internal FemMeshShellsView(FemMeshShellsSubNode node, AppViewModel app)
    {
        InitializeComponent();
        _node = node;
        _groups = new FemGroupTableTools(app, node.Owner.Schema, meshShellsGrid, FemMemberGroup.KindMesh,
            row => ((FemElement)row).ElemTag);
        Loaded += async (_, _) =>
        {
            var elems = await node.Owner.LoadMeshShellsAsync();
            meshShellsGrid.ItemsSource = elems;
        };
    }

    void CreateGroup_Click(object sender, RoutedEventArgs e) => _groups.CreateGroup(CScore.Fem.FemMemberTypes.Shell);

    /// <summary>«КонЭ из выделенных…»: после создания перечитывается колонка владельцев КЭ.</summary>
    async void CreateMembers_Click(object sender, RoutedEventArgs e)
    {
        if (_groups.CreateMembers(CScore.Fem.FemMemberTypes.Shell))
            meshShellsGrid.ItemsSource = await _node.Owner.LoadMeshShellsAsync();
    }

    void AddToGroup_Click(object sender, RoutedEventArgs e) => _groups.ShowAddMenu((Button)sender);

    void RemoveFromGroup_Click(object sender, RoutedEventArgs e) => _groups.ShowRemoveMenu((Button)sender);
}
