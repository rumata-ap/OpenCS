using System.Windows;
using CScore.Fem.Fragment;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>«Фрагмент этажа…»: перекрытие из списка (или отметки вручную) и имя новой схемы — см.
/// <see cref="StoreyFragmentDialogVM"/>.</summary>
public partial class StoreyFragmentDialog : Window
{
    readonly StoreyFragmentDialogVM _vm;

    /// <summary>Запрос сборки после «Собрать».</summary>
    public StoreyFragmentRequest? Request { get; private set; }

    public StoreyFragmentDialog(StoreyFragmentDialogVM vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        Loaded += (_, _) =>
        {
            if (vm.Manual) z1Box.Focus();
            else
            {
                if (vm.SelectedLevel is { } row) levelsList.ScrollIntoView(row);
                levelsList.Focus();
            }
        };
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.TryRequest(out var error) is not { } request)
        {
            MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Request = request;
        DialogResult = true;
    }
}
