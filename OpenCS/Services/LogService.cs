using System;
using System.Collections.ObjectModel;

namespace OpenCS.Services
{
   /// <summary>
   /// Реализация логирования через ObservableCollection.
   /// </summary>
   public class LogService : ILogService
   {
      public ObservableCollection<LogEntry> LogEntries { get; } = [];

      public void Info(string message) => LogEntries.Add(new(message, LogLevel.Info, DateTime.Now));
      public void Warning(string message) => LogEntries.Add(new(message, LogLevel.Warning, DateTime.Now));
      public void Error(string message) => LogEntries.Add(new(message, LogLevel.Error, DateTime.Now));
   }
}