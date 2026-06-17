using CScore;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views;

/// <summary>Просмотр результата пакетного расчёта состояния деформаций.</summary>
public partial class StrainStateBatchResultView : UserControl
{
    public StrainStateBatchResultView(CalcResult result)
    {
        InitializeComponent();
        DataContext = new StrainStateBatchVM(result);
    }
}
