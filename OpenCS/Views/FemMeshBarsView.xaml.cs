using System.Windows.Controls;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemMeshBarsView : UserControl
{
    internal FemMeshBarsView(FemMeshBarsSubNode node)
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            var elems = await node.Owner.LoadMeshBarsAsync();
            meshBarsGrid.ItemsSource = elems;
        };
    }
}
