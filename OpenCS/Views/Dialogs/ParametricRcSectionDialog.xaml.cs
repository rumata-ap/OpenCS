using System.Windows;
using OpenCS.ViewModels;

namespace OpenCS.Views.Dialogs;

/// <summary>Диалог создания и редактирования параметрического ЖБ-сечения.</summary>
public partial class ParametricRcSectionDialog : Window
{
    /// <summary>Модель полей диалога.</summary>
    public ParametricRcSectionVM ViewModel => (ParametricRcSectionVM)DataContext;

    /// <summary>Создаёт диалог.</summary>
    public ParametricRcSectionDialog(ParametricRcSectionVM? viewModel = null)
    {
        InitializeComponent();
        DataContext = viewModel ?? new ParametricRcSectionVM();
        ViewModel.RefreshPreview();
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanSave) return;
        DialogResult = true;
    }
}
