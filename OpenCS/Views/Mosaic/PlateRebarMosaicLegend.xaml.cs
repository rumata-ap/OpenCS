using System.Windows.Controls;
using System.Windows.Input;

namespace OpenCS.Views.Mosaic;

/// <summary>Легенда мозаики армирования пластин и ручные границы шкалы (DataContext — PlateRebarMosaicVM).</summary>
public partial class PlateRebarMosaicLegend : UserControl
{
    public PlateRebarMosaicLegend() => InitializeComponent();

    // Enter применяет границы, не дожидаясь потери фокуса.
    void Thresholds_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }
}
