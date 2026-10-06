using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemMeshBarsView : UserControl
{
    readonly FemGroupTableTools _groups;

    internal FemMeshBarsView(FemMeshBarsSubNode node, AppViewModel app)
    {
        InitializeComponent();
        _groups = new FemGroupTableTools(app, node.Owner.Schema, meshBarsGrid, FemMemberGroup.KindMesh,
            row => ((FemElement)row).ElemTag);
        Loaded += async (_, _) =>
        {
            var elems = await node.Owner.LoadMeshBarsAsync();
            meshBarsGrid.ItemsSource = elems;
        };
    }

    void CreateGroup_Click(object sender, RoutedEventArgs e) => _groups.CreateGroup(CScore.Fem.FemMemberTypes.Beam);

    void AddToGroup_Click(object sender, RoutedEventArgs e) => _groups.ShowAddMenu((Button)sender);

    void RemoveFromGroup_Click(object sender, RoutedEventArgs e) => _groups.ShowRemoveMenu((Button)sender);
}
