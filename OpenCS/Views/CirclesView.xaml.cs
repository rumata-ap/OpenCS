using CScore;
using OpenCS.Services;
using OpenCS.Utilites;

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

      /// <summary>Значения строки до правки — чтобы вернуть их, если служба не примет правку.</summary>
      (CircleP Circle, string Tag, string? Set, double X, double Y, double R)? _before;

      void CirclesGrid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
      {
         if (e.Row.Item is CircleP cp && _before?.Circle != cp)
            _before = (cp, cp.Tag, cp.GeometrySet, cp.X, cp.Y, cp.Radius);
      }

      /// <summary>Правка строки: проверка и запись через <see cref="ContourAuthoring.SaveCircle"/>; ошибка — откат и журнал.</summary>
      void CirclesGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
      {
         if (e.Row.Item is not CircleP cp) return;
         var before = _before;
         _before = null;
         if (e.EditAction != DataGridEditAction.Commit) return;

         // Биндинг ещё не применён — ждём следующего кадра
         Dispatcher.InvokeAsync(() =>
         {
            var errors = ContourAuthoring.SaveCircle(mvm.db, cp);
            if (errors.Count > 0)
            {
               mvm.LogService.Warning(string.Format(Loc.S("CircleInvalidLog"), cp.Tag, errors[0].Message));
               if (before is { } b && ReferenceEquals(b.Circle, cp))
               {
                  cp.Tag = b.Tag; cp.GeometrySet = b.Set; cp.X = b.X; cp.Y = b.Y; cp.Radius = b.R;
               }
            }
            else
               mvm.MarkDirty(SaveCategory.Circles);
            circlesGrid.Items.Refresh();
         }, DispatcherPriority.Background);
      }
   }
}
