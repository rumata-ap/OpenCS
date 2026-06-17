using CScore;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views;

/// <summary>Просмотр результата пакетной R-проверки.</summary>
public partial class FireRCheckBatchResultView : UserControl
{
    public FireRCheckBatchResultView(CalcResult result)
    {
        InitializeComponent();
        DataContext = new FireRCheckBatchVM(result);
    }
}
