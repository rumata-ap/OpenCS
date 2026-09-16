using System.Linq;
using System.Windows.Controls;
using CScore;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class ShellLayeredSlsResultView : UserControl
{
   public ShellLayeredSlsResultView(CalcResult result, AppViewModel app, CalcTask task)
   {
      InitializeComponent();
      string sectionTag = app.PlateSections.FirstOrDefault(s => s.Id == task.SectionId)?.Tag ?? "";
      DataContext = new ShellLayeredSlsResultVM(result.DataJson, task.Tag, sectionTag, result.Created);
   }
}
