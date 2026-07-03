using CScore;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views;

/// <summary>Просмотр результата пакетной проверки прочности по НДМ.</summary>
public partial class StrengthNDMBatchResultView : UserControl
{
    public StrengthNDMBatchResultView(CalcResult result)
    {
        InitializeComponent();
        DataContext = new StrengthNDMBatchVM(result);
    }
}
