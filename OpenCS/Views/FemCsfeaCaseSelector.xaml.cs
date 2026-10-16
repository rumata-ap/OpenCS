using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>
/// Выбор показанного в линейном результате CSfea: загружение, комбинация схемы (суперпозиция) или огибающая группы
/// комбинаций. Общий для окна
/// результата и строки результата под 3D-видом; DataContext — <see cref="FemCsfeaResultVM"/>.
/// </summary>
public partial class FemCsfeaCaseSelector : UserControl
{
    public FemCsfeaCaseSelector() => InitializeComponent();

    FemCsfeaResultVM? Vm => DataContext as FemCsfeaResultVM;

    void Mode_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (ReferenceEquals(sender, combinationsMode)) vm.ShowCombinations = true;
        else if (ReferenceEquals(sender, envelopesMode)) vm.ShowEnvelopes = true;
        else { vm.ShowCombinations = false; vm.ShowEnvelopes = false; }
        // Повторный щелчок по нажатой кнопке снимает её локально — вернуть состояние из VM.
        foreach (var button in new[] { casesMode, combinationsMode, envelopesMode })
            button.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
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
