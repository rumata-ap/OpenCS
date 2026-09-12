using System.Windows.Controls;
using CScore;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Экран результата упрощённой проверки ширины раскрытия трещин СП 63.</summary>
public partial class Sp63CrackWidthResultView : UserControl
{
   /// <summary>Создаёт экран по JSON результата без дополнительного контекста.</summary>
   public Sp63CrackWidthResultView(string dataJson)
   {
      InitializeComponent();
      DataContext = new Sp63CrackWidthResultVM(dataJson);
   }

   /// <summary>Создаёт экран с метками задачи, сечения и исходными усилиями.</summary>
   public Sp63CrackWidthResultView(CalcResult result, CalcTask task, OpenCS.AppViewModel app)
   {
      InitializeComponent();
      DataContext = new Sp63CrackWidthResultVM(result.DataJson, result, task, app);
   }
}
