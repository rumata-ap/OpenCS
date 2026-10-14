using System.Threading;
using CScore.Fire.Entities;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Контекст выполнения задач (БД, огневые сущности, отмена, прогресс).</summary>
public sealed class TaskRunContext
{
    public DatabaseService? Database { get; init; }
    public IReadOnlyList<FireSectionDef>? FireSections { get; init; }
    public CancellationToken CancellationToken { get; init; }
    public IProgress<CalcTaskProgress>? Progress { get; init; }
}
