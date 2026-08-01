using System.Windows.Input;

namespace OpenCS.Utilites
{
   public class RelayCommand : ICommand
   {
       Action<object?> execute;
       Func<object?, bool>? canExecute;
       private event EventHandler? CanExecuteChangedCore;

       public event EventHandler? CanExecuteChanged
       {
          add { }
          remove { }
       }

      /// <summary>Уведомляет подписчиков о необходимости перезапроса CanExecute.</summary>
      public void RaiseCanExecuteChanged() => CanExecuteChangedCore?.Invoke(this, EventArgs.Empty);

      public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
      {
         this.execute = execute;
         this.canExecute = canExecute;
      }

      public bool CanExecute(object? parameter)
      {
         return canExecute == null || canExecute(parameter);
      }

      public void Execute(object? parameter)
      {
         execute(parameter);
      }
   }
}
