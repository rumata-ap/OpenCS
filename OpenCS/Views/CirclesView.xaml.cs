using CScore;

using System.Windows.Controls;
using System.Windows.Threading;

namespace OpenCS.Views
{
   /// <summary>Табличное представление коллекции окружностей проекта.</summary>
   public partial class CirclesView : UserControl
   {
      AppViewModel mvm;

      public CirclesView(AppViewModel vm)
      {
         InitializeComponent();
         DataContext = vm;
         mvm = vm;
      }

      void CirclesGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
      {
         if (e.EditAction != DataGridEditAction.Commit) return;
         if (e.Row.Item is not CircleP cp) return;

         // Биндинг ещё не применён — ждём следующего кадра
         Dispatcher.InvokeAsync(() =>
         {
            cp.Diameter = cp.Radius * 2;
            cp.Area     = Math.PI * cp.Radius * cp.Radius;
            mvm.db.SaveCircle(cp);
         }, DispatcherPriority.Background);
      }
   }
}
