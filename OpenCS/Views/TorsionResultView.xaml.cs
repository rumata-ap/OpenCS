using System.Windows.Controls;
using CScore;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Страница результата задачи кручения Сен-Венана.</summary>
public partial class TorsionResultView : UserControl
{
    public TorsionResultView(CalcResult result, AppViewModel app, CalcTask task)
    {
        InitializeComponent();
        DataContext = new TorsionResultVM(result);
    }
}
