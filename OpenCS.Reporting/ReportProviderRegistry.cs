using CScore;

namespace OpenCS.Reporting;

/// <summary>Реестр поставщиков отчётов: подбирает поставщика по типу расчётной задачи.
/// Добавление отчёта для нового типа задачи — реализовать <see cref="IReportProvider"/>
/// и включить его в список экземпляра реестра.</summary>
public sealed class ReportProviderRegistry
{
    readonly IReadOnlyList<IReportProvider> _providers;

    /// <summary>Виды задач, для которых зарегистрирован поставщик.</summary>
    public IReadOnlyCollection<string> SupportedKinds { get; }

    /// <summary>Создаёт реестр из перечня поставщиков; порядок определяет приоритет.</summary>
    public ReportProviderRegistry(IEnumerable<IReportProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToList();
        SupportedKinds = _providers
            .SelectMany(provider => provider.SupportedKinds)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Пытается подобрать поставщика; false, если для задачи его нет.</summary>
    public bool TryResolve(CalcTask task, out IReportProvider provider)
    {
        ArgumentNullException.ThrowIfNull(task);
        provider = _providers.FirstOrDefault(candidate => candidate.CanHandle(task))!;
        return provider != null;
    }

    /// <summary>Возвращает первого поставщика, поддерживающего задачу.</summary>
    /// <exception cref="NotSupportedException">Поставщик для типа задачи не зарегистрирован.</exception>
    public IReportProvider Resolve(CalcTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (TryResolve(task, out var provider))
            return provider;
        throw new NotSupportedException($"Нет поставщика отчёта для задачи типа '{task.Kind}'.");
    }
}
