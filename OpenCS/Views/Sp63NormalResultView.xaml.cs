using System.Windows.Controls;
using CScore;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Экран результата упрощённой проверки нормального сечения СП 63.</summary>
public partial class Sp63NormalResultView : UserControl
{
   /// <summary>Создаёт экран по JSON результата без дополнительного контекста.</summary>
   public Sp63NormalResultView(string dataJson)
   {
      InitializeComponent();
      DataContext = new Sp63NormalResultVM(dataJson);
   }

   /// <summary>Создаёт экран с метками задачи, сечения и исходными усилиями.</summary>
   public Sp63NormalResultView(CalcResult result, CalcTask task, OpenCS.AppViewModel app)
   {
      InitializeComponent();
      DataContext = new Sp63NormalResultVM(result.DataJson, result, task, app);
   }
}
