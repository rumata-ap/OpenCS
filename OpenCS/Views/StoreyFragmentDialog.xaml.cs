using System.Windows;
using CScore.Fem.Fragment;
using OpenCS.Services;
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
        // Ошибки — под полями (VM); окно не закрывается, фокус — в поле первой ошибки.
        if (_vm.TryRequest(out _) is not { } request)
        {
            System.Windows.Controls.Control? field = _vm.FirstErrorField switch
            {
                FragmentAuthoring.FieldZ1 => z1Box,
                FragmentAuthoring.FieldZ2 => z2Box,
                FragmentAuthoring.FieldName => nameBox,
                FragmentAuthoring.FieldFloors => levelsList,
                _ => null,
            };
            field?.Focus();
            if (field is System.Windows.Controls.TextBox box) box.SelectAll();
            return;
        }
        Request = request;
        DialogResult = true;
    }
}
