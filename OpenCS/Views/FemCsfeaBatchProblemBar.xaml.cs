using System.Windows;
using System.Windows.Controls;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>
/// Плашка не дошедших до предела λ и не посчитанных вариантов пакета CSfea; DataContext — <see cref="FemCsfeaResultVM"/>.
/// В окне результата «Показать» переключает на сводку пакета, под 3D-видом (<see cref="OpensResultWindow"/>) — открывает
/// окно результата на ней.
/// </summary>
public partial class FemCsfeaBatchProblemBar : UserControl
{
    public FemCsfeaBatchProblemBar() => InitializeComponent();

    /// <summary>«Показать» открывает окно результата (плашка под 3D-видом).</summary>
    public bool OpensResultWindow { get; set; }

    FemCsfeaResultVM? Vm => DataContext as FemCsfeaResultVM;

    static AppViewModel? App => Application.Current?.MainWindow?.DataContext as AppViewModel;

    void Show_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (OpensResultWindow) App?.OpenFemAnalysisResultTables(vm.Analysis, null, vm.ShownKey, showBatchProblems: true);
        else vm.ShowBatchProblems();
    }

    void NewBatch_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) App?.NewFemBatchFromProblems(vm);
    }
}
