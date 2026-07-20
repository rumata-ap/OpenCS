using System.Windows.Controls;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemMeshNodesView : UserControl
{
    internal FemMeshNodesView(FemMeshNodesSubNode node)
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            var nodes = await node.Owner.LoadMeshNodesAsync();
            meshNodesGrid.ItemsSource = nodes;
        };
    }
}
