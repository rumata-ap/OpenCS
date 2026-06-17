using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace OpenCS.Services
{
   /// <summary>
   /// Реализация логирования через ObservableCollection.
   /// Все операции добавления гарантированно выполняются в UI-потоке
   /// через Dispatcher.BeginInvoke, что исключает reentrancy и
   /// cross-thread исключения в VirtualizingStackPanel.
   /// </summary>
   public class LogService : ILogService
   {
      public ObservableCollection<LogEntry> LogEntries { get; } = [];

      void AddEntry(LogEntry entry)
      {
         var app = Application.Current;
         if (app == null) { LogEntries.Add(entry); return; }

         var d = app.Dispatcher;
         if (d.CheckAccess())
            LogEntries.Add(entry);
         else
            d.BeginInvoke(DispatcherPriority.Normal, (Action)(() => LogEntries.Add(entry)));
      }

      public void Info(string message)    => AddEntry(new(message, LogLevel.Info,    DateTime.Now));
      public void Warning(string message) => AddEntry(new(message, LogLevel.Warning, DateTime.Now));
      public void Error(string message)   => AddEntry(new(message, LogLevel.Error,   DateTime.Now));
   }
}
