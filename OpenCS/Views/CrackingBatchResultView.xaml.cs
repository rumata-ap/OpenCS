using CScore;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views;

public partial class CrackingBatchResultView : UserControl
{
    public CrackingBatchResultView(CalcResult result, AppViewModel app, CalcTask task)
    {
        InitializeComponent();
        DataContext = new CrackingBatchVM(result);
    }
}
