namespace OpenCS.Services
{
   /// <summary>Маршалинг в UI-поток (WPF Dispatcher / Avalonia Dispatcher.UIThread).</summary>
   public interface IDispatcherService
   {
      void BeginInvoke(Action action);
      Task InvokeAsync(Action action);

      /// <summary>Пересчитывает CanExecute всех команд (WPF: CommandManager.InvalidateRequerySuggested).</summary>
      void InvalidateRequerySuggested();
   }
}
