using CScore;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views;

/// <summary>Просмотр результата R-проверки огнестойкости.</summary>
public partial class FireRCheckResultView : UserControl
{
    public FireRCheckResultView(CalcResult result, AppViewModel app, CalcTask? task)
    {
        InitializeComponent();
        DataContext = new FireRCheckResultHostVM(result, app, task);
    }

    sealed class FireRCheckResultHostVM
    {
        public FireRCheckSummaryVM Summary { get; }
        public FireRCheckPlotsVM Plots { get; }

        public FireRCheckResultHostVM(CalcResult result, AppViewModel app, CalcTask? task)
        {
            Summary = new FireRCheckSummaryVM(result);
            Plots = new FireRCheckPlotsVM(result, task, app);
        }
    }
}
