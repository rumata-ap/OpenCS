using System;
using System.Collections.ObjectModel;

namespace OpenCS.Services
{
   /// <summary>
   /// Уровень логирования.
   /// </summary>
   public enum LogLevel
   {
      Info,
      Warning,
      Error
   }

   /// <summary>
   /// Запись лога с сообщением, уровнем и временем.
   /// </summary>
   public class LogEntry
   {
      public string Message { get; }
      public LogLevel Level { get; }
      public DateTime Timestamp { get; }
      public string FormattedMessage => $"{Timestamp:HH:mm:ss} | {Level} | {Message}";

      public LogEntry(string message, LogLevel level, DateTime timestamp)
      {
         Message = message;
         Level = level;
         Timestamp = timestamp;
      }
   }

   /// <summary>
   /// Сервис логирования. Заменяет прямое создание TextBlock для логов в ViewModel.
   /// </summary>
   public interface ILogService
   {
      void Info(string message);
      void Warning(string message);
      void Error(string message);
      ObservableCollection<LogEntry> LogEntries { get; }
   }
}