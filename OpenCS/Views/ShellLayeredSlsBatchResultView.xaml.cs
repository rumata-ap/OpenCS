using System.Windows.Controls;
using CScore;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class ShellLayeredSlsBatchResultView : UserControl
{
   public ShellLayeredSlsBatchResultView(CalcResult result, AppViewModel app, CalcTask task)
   {
      InitializeComponent();
      DataContext = new ShellLayeredSlsBatchResultVM(result.DataJson);
   }
}
