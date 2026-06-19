using OpenCS.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenCS.Views;

public partial class FireMeshPlotView : UserControl
{
    public FireMeshPlotView()
    {
        InitializeComponent();
    }

    void IsolineStep_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        => CommitIsolineStep();

    void IsolineStep_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            CommitIsolineStep();
    }

    void CommitIsolineStep()
    {
        if (DataContext is FireMeshPlotVM vm)
            vm.CommitIsolineStepText();
    }

    public void RequestFitToView() => meshCanvas.RequestAutoFitOnShow();
}
