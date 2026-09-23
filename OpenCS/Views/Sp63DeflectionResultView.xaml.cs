using System.Windows.Controls;
using CScore;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Экран результата формульной проверки прогиба СП 63.</summary>
public partial class Sp63DeflectionResultView : UserControl
{
    public Sp63DeflectionResultView(string dataJson)
    {
        InitializeComponent();
        DataContext = new Sp63DeflectionResultVM(dataJson);
    }

    public Sp63DeflectionResultView(CalcResult result, CalcTask task, OpenCS.AppViewModel app)
    {
        InitializeComponent();
        DataContext = new Sp63DeflectionResultVM(result.DataJson, result, task, app);
    }
}
