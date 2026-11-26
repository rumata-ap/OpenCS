using System.Windows;
using CScore.Fem.Imperfections;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Элемент несовершенства загружения «Несовершенство» (<see cref="FemImperfectionDialogVM"/>); результат — <see cref="Result"/>.</summary>
public partial class FemImperfectionDialog : Window
{
    readonly FemImperfectionDialogVM _vm;

    /// <summary>Элемент по полям диалога (после OK).</summary>
    public FemImperfection? Result { get; private set; }

    /// <summary>Запись элемента: текст ошибки (окно остаётся открытым) или null.</summary>
    public Func<FemImperfection, string?>? Save { get; init; }

    public FemImperfectionDialog(FemImperfectionDialogVM vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Loaded += (_, _) => { tagBox.Focus(); tagBox.SelectAll(); };
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.TryBuild(out var error) is not { } result)
        {
            errorText.Text = error;
            errorText.Visibility = Visibility.Visible;
            return;
        }
        if (Save?.Invoke(result) is { } saveError)
        {
            errorText.Text = saveError;
            errorText.Visibility = Visibility.Visible;
            return;
        }
        Result = result;
        DialogResult = true;
    }
}
