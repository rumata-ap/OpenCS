using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Общий рапорт прогресса пакетных задач.</summary>
internal static class BatchProgress
{
    public static void Report(TaskRunContext? ctx, ref int done, int total)
    {
        ctx?.CancellationToken.ThrowIfCancellationRequested();
        int k = Interlocked.Increment(ref done);
        ctx?.Progress?.Report(new CalcTaskProgress
        {
            Fraction = total > 0 ? (double)k / total : 1.0,
            Message = string.Format(Loc.S("CalcTaskBatchProgress"), k, total)
        });
    }

    /// <summary>
    /// Строки пакета 0…total−1: при «Многопоточном пакетном расчёте» — параллельно (тело не должно делить
    /// между строками изменяемое состояние: сечение с фибрами, решатель с тёплым стартом), иначе по порядку.
    /// Прогресс — после каждой строки; отмена — между строками; исключение строки выходит без AggregateException.
    /// </summary>
    public static void ForEachRow(int total, CalcSettings settings, TaskRunContext? ctx, Action<int> body)
    {
        int done = 0;
        void Row(int i)
        {
            body(i);
            Report(ctx, ref done, total);
        }

        if (!settings.BatchParallel || total < 2)
        {
            for (int i = 0; i < total; i++) Row(i);
            return;
        }

        try
        {
            Parallel.For(0, total, new ParallelOptions { CancellationToken = ctx?.CancellationToken ?? default }, Row);
        }
        catch (AggregateException ex)
        {
            ExceptionDispatchInfo.Capture(ex.Flatten().InnerExceptions[0]).Throw();
        }
    }
}
