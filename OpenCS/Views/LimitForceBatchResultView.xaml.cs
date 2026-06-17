namespace OpenCS.Views;

using CScore;

public partial class LimitForceBatchResultView : System.Windows.Controls.UserControl
{
    public LimitForceBatchResultView(CalcResult result)
    {
        InitializeComponent();
        DataContext = new ViewModels.LimitForceBatchVM(result);
    }
}
