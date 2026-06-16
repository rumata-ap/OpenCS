using CScore;

namespace OpenCS.Tasks
{
   /// <summary>
   /// Интерфейс обработчика расчётной задачи конкретного вида.
   /// </summary>
   public interface ITaskHandler
   {
      string Kind { get; }

      /// <summary>
      /// Выполняет расчёт и возвращает CalcResult. Никогда не бросает — ошибки кодируются в Status.
      /// </summary>
      CalcResult Run(CalcTask task, CrossSection section, LoadItem item);
   }
}
