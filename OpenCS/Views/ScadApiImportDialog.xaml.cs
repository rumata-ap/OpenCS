using System.Windows;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Диалог импорта схемы из проекта SCAD (.SPR) через SCADAPIX.dll.</summary>
public partial class ScadApiImportDialog : Window
{
    public ScadApiImportDialog(ScadApiImportVM vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    void Import_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
