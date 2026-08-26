using CScore;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views;

/// <summary>Просмотр результата расчёта температурной кривизны огневого сечения.</summary>
public partial class FireThermalCurvatureResultView : UserControl
{
   /// <summary>Создать представление результата.</summary>
   public FireThermalCurvatureResultView(CalcResult result, AppViewModel app, CalcTask task)
   {
      InitializeComponent();
      DataContext = new FireThermalCurvatureResultVM(result);
   }
}
