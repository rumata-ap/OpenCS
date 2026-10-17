using System.Windows;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>«Наборы усилий по вариантам пакета»: группы, тип загружений, охват, оценка (<see cref="FemCsfeaBatchForceSetsVM"/>).</summary>
public partial class FemCsfeaBatchForceSetsDialog : Window
{
    public FemCsfeaBatchForceSetsDialog(FemCsfeaBatchForceSetsVM vm)
    {
        InitializeComponent();
        DataContext = Vm = vm;
    }

    public FemCsfeaBatchForceSetsVM Vm { get; }

    void Create_Click(object sender, RoutedEventArgs e)
    {
        if (!Vm.CanCreate) return;
        Vm.Remember();
        DialogResult = true;
    }
}
