using System.Windows.Controls;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemMeshShellsView : UserControl
{
    internal FemMeshShellsView(FemMeshShellsSubNode node)
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            var elems = await node.Owner.LoadMeshShellsAsync();
            meshShellsGrid.ItemsSource = elems;
        };
    }
}
