using System.Windows.Controls;
using OpenCS.ViewModels;

namespace OpenCS.Views.Mosaic;

/// <summary>Выбор вида, набора усилий или проверки и компоненты мозаики по КЭ (DataContext — <see cref="PlateRebarMosaicVM"/>).</summary>
public partial class PlateRebarMosaicSelector : UserControl
{
    public PlateRebarMosaicSelector() => InitializeComponent();

    // После открытия вида могли дозагрузить файлы ASP/RBT, импортировать усилия или выполнить проверку.
    void Source_DropDownOpened(object? sender, System.EventArgs e) => (DataContext as PlateRebarMosaicVM)?.Reload();
}
