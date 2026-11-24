using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CScore;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class PrestressLossDialog : Window
{
    PrestressLossDlgVM? _vm;

    public PrestressLossDialog(AppViewModel app, CalcTask task)
    {
        InitializeComponent();
        _vm = new PrestressLossDlgVM(app, task, this);
        DataContext = _vm;
        _vm.PropertyChanged += VmOnPropertyChanged;
        UpdateColumnVisibility();
    }

    /// <summary>
    /// «Сохранить» (Enter): ячейка с нераспознанным числом (красная рамка) — сообщение, окно остаётся открытым: в
    /// параметрах осталось бы прежнее значение, и молча записывать его нельзя.
    /// </summary>
    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (FindInvalid(this) is { } bad)
        {
            MessageBox.Show(this, Loc.S("PunchingInvalidNumber"), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            bad.Focus();
            return;
        }
        _vm.Save();
    }

    static Control? FindInvalid(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Control { IsVisible: true } c && Validation.GetHasError(c)) return c;
            if (FindInvalid(child) is { } found) return found;
        }
        return null;
    }

    void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PrestressLossDlgVM.IsOnSupports)
                           or nameof(PrestressLossDlgVM.IsOnConcrete))
            UpdateColumnVisibility();
    }

    void UpdateColumnVisibility()
    {
        if (_vm == null) return;
        var supV = _vm.IsOnSupports ? Visibility.Visible : Visibility.Collapsed;
        var conV = _vm.IsOnConcrete ? Visibility.Visible : Visibility.Collapsed;

        colSubMethod.Visibility    = supV;
        colDtDefault.Visibility    = supV;
        colDt.Visibility           = supV;
        colFormDefault.Visibility  = supV;
        colNForms.Visibility       = supV;
        colDlForm.Visibility       = supV;
        colLForm.Visibility        = supV;
        colAnchorDefault.Visibility = supV;
        colDlAnchor.Visibility     = supV;
        colLAnchor.Visibility      = supV;

        colOmega1.Visibility = conV;
        colKFric.Visibility  = conV;
        colX.Visibility      = conV;
        colTheta.Visibility  = conV;
    }
}
