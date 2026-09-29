using System.Windows.Controls;
using OpenCS.ViewModels;

namespace OpenCS.Views.Mosaic;

/// <summary>Выбор источника и компоненты мозаики армирования пластин (DataContext — <see cref="PlateRebarMosaicVM"/>).</summary>
public partial class PlateRebarMosaicSelector : UserControl
{
    public PlateRebarMosaicSelector() => InitializeComponent();

    // Файлы ASP/RBT могли дозагрузить из меню схемы после открытия вида.
    void Source_DropDownOpened(object? sender, System.EventArgs e) => (DataContext as PlateRebarMosaicVM)?.Reload();
}
