using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>
/// Выбор показанного в линейном результате CSfea: загружение или комбинация схемы (суперпозиция). Общий для окна
/// результата и строки результата под 3D-видом; DataContext — <see cref="FemCsfeaResultVM"/>.
/// </summary>
public partial class FemCsfeaCaseSelector : UserControl
{
    public FemCsfeaCaseSelector() => InitializeComponent();

    FemCsfeaResultVM? Vm => DataContext as FemCsfeaResultVM;

    void Mode_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        vm.ShowCombinations = ReferenceEquals(sender, combinationsMode);
        // Повторный щелчок по нажатой кнопке снимает её локально — вернуть состояние из VM.
        casesMode.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
        combinationsMode.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
    }

    void Pick_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var dialog = new FemLoadSourcePickerDialog(vm.PickerSources, vm.SelectedCaseItem?.Source, null,
            Utilites.Loc.S("FemCsfeaPickTitle"))
        {
            Owner = Window.GetWindow(this),
        };
        if (dialog.ShowDialog() == true && dialog.Result is { } source) vm.SelectSource(source);
    }
}
