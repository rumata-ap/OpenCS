using CScore;
using OpenCS.ViewModels;
using System.Linq;
using System.Windows.Controls;

namespace OpenCS.Views;

/// <summary>Просмотр результата пакетной проверки прочности по НДМ.</summary>
public partial class StrengthNDMBatchResultView : UserControl
{
    public StrengthNDMBatchResultView(CalcResult result, AppViewModel app, CalcTask task)
    {
        InitializeComponent();
        var section = app.CrossSections.FirstOrDefault(s => s.Id == task.SectionId);
        DataContext = new StrengthNDMBatchVM(result, section, app.CalcSettings);
    }
}
