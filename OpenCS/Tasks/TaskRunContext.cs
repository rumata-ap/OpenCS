using CScore.Fire.Entities;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Контекст выполнения задач, требующих доступа к БД и огневым сущностям.</summary>
public sealed class TaskRunContext
{
    public DatabaseService? Database { get; init; }
    public IReadOnlyList<FireSectionDef>? FireSections { get; init; }
}
