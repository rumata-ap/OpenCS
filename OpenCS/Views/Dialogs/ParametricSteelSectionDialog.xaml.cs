using System.Windows;
using OpenCS.ViewModels;

namespace OpenCS.Views.Dialogs;

/// <summary>Диалог создания и редактирования параметрического МК-сечения.</summary>
public partial class ParametricSteelSectionDialog : Window
{
    /// <summary>Модель полей диалога.</summary>
    public ParametricSteelSectionVM ViewModel => (ParametricSteelSectionVM)DataContext;

    /// <summary>Создаёт диалог.</summary>
    public ParametricSteelSectionDialog(ParametricSteelSectionVM viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanSave) return;
        DialogResult = true;
    }
}
