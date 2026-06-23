using System.Windows.Controls;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class FemBarsView : UserControl
{
    internal FemBarsView(FemBarsSubNode node)
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            var elems = await node.Owner.LoadBarsAsync();
            barsGrid.ItemsSource = elems;
        };
    }
}
