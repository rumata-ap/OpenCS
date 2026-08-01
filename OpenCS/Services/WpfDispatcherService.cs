using System.Windows;

namespace OpenCS.Services
{
   /// <summary>Реализация IDispatcherService поверх WPF Dispatcher.</summary>
   public class WpfDispatcherService : IDispatcherService
   {
      public void BeginInvoke(Action action)
      {
         var dispatcher = Application.Current?.Dispatcher;
         if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke(action);
         else action();
      }

      public Task InvokeAsync(Action action)
      {
         var dispatcher = Application.Current?.Dispatcher;
         if (dispatcher != null && !dispatcher.CheckAccess())
            return dispatcher.InvokeAsync(action).Task;
         action();
         return Task.CompletedTask;
      }
   }
}
