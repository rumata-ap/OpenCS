namespace OpenCS.Services
{
   /// <summary>Абстракция маршалинга в UI-поток (WPF Dispatcher / Avalonia Dispatcher.UIThread).</summary>
   public interface IDispatcherService
   {
      void BeginInvoke(Action action);
      Task InvokeAsync(Action action);
   }
}
