using System.Threading;
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
}
