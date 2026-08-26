using System.Windows.Controls;
using CScore;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Просмотр результата задачи собственного предела огнестойкости.</summary>
public partial class FireRTimeResultView : UserControl
{
   public FireRTimeResultView(CalcResult result, AppViewModel app, CalcTask task)
   {
      InitializeComponent();
      DataContext = new FireRTimeResultVM(result);
   }
}
