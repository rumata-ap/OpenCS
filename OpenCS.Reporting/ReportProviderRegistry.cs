using CScore;

namespace OpenCS.Reporting;

/// <summary>Реестр поставщиков отчётов: подбирает поставщика по типу расчётной задачи.
/// Добавление отчёта для нового типа задачи — реализовать <see cref="IReportProvider"/>
/// и включить его в список экземпляра реестра.</summary>
public sealed class ReportProviderRegistry
{
    readonly IReadOnlyList<IReportProvider> _providers;

    /// <summary>Создаёт реестр из перечня поставщиков; порядок определяет приоритет.</summary>
    public ReportProviderRegistry(IEnumerable<IReportProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToList();
    }

    /// <summary>Возвращает первого поставщика, поддерживающего задачу.</summary>
    /// <exception cref="NotSupportedException">Поставщик для типа задачи не зарегистрирован.</exception>
    public IReportProvider Resolve(CalcTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return _providers.FirstOrDefault(p => p.CanHandle(task))
            ?? throw new NotSupportedException($"Нет поставщика отчёта для задачи типа '{task.Kind}'.");
    }
}
