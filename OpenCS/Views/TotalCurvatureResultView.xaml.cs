using System.Windows.Controls;
using CScore;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class TotalCurvatureResultView : UserControl
{
    public TotalCurvatureResultView(CalcResult result)
    {
        InitializeComponent();
        SummaryView.DataContext = new TotalCurvatureSummaryVM(result);
    }
}
