using CScore;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views;

public partial class CrackWidthBatchResultView : UserControl
{
    public CrackWidthBatchResultView(CalcResult result, AppViewModel app, CalcTask task)
    {
        InitializeComponent();
        DataContext = new CrackWidthBatchVM(result);
    }
}
