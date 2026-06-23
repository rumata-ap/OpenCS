using System.Windows.Controls;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemNodesView : UserControl
{
    internal FemNodesView(FemNodesSubNode node)
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            var nodes = await node.Owner.LoadNodesAsync();
            nodesGrid.ItemsSource = nodes;
        };
    }
}
