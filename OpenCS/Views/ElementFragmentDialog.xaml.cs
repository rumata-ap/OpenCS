using System.Windows;
using System.Windows.Input;
using CScore.Fem.Fragment;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>«Фрагмент вокруг элементов…»: отступ d, перекрытия с/по или «вся высота», имя — см.
/// <see cref="ElementFragmentDialogVM"/>. d принимается по потере фокуса, Enter или «Пересчитать».</summary>
public partial class ElementFragmentDialog : Window
{
    readonly ElementFragmentDialogVM _vm;

    /// <summary>Запрос сборки после «Собрать».</summary>
    public StoreyFragmentRequest? Request { get; private set; }

    public ElementFragmentDialog(ElementFragmentDialogVM vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        Loaded += (_, _) => offsetBox.Focus();
    }

    async void Offset_LostFocus(object sender, RoutedEventArgs e) => await _vm.ApplyOffsetAsync();

    async void Recalc_Click(object sender, RoutedEventArgs e) => await _vm.ApplyOffsetAsync();

    async void Offset_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await _vm.ApplyOffsetAsync();
    }

    async void Apply_Click(object sender, RoutedEventArgs e)
    {
        await _vm.ApplyOffsetAsync();
        if (_vm.TryRequest(out var error) is not { } request)
        {
            MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Request = request;
        DialogResult = true;
    }
}
