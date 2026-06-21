using System.ComponentModel;
using System.Windows;
using CScore;
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
