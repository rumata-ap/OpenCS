using System.Windows.Controls;
using CScore;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class TotalCurvatureBatchResultView : UserControl
{
    public TotalCurvatureBatchResultView(CalcResult result)
    {
        InitializeComponent();
        DataContext = new TotalCurvatureBatchVM(result);
    }
}
