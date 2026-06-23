using System.Windows.Controls;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemShellsView : UserControl
{
    internal FemShellsView(FemShellsSubNode node)
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            var elems = await node.Owner.LoadShellsAsync();
            shellsGrid.ItemsSource = elems;
        };
    }
}
