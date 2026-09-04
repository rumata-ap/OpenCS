using CScore;

namespace OpenCS.Reporting;

/// <summary>Контекст построения отчёта с расчётной задачей, результатом и иллюстрациями.</summary>
public sealed class ReportContext
{
    /// <summary>Расчётная задача.</summary>
    public CalcTask Task { get; }
    /// <summary>Результат расчёта.</summary>
    public CalcResult Result { get; }
    /// <summary>Сечение, использованное расчётной задачей; нужно для геометрии и материалов.</summary>
    public CrossSection? Section { get; }
    /// <summary>Встроенные SVG по именам, например stress и strain.</summary>
    public IReadOnlyDictionary<string, string> Images { get; }

    /// <summary>Создаёт контекст отчёта.</summary>
    public ReportContext(CalcTask task, CalcResult result,
        IReadOnlyDictionary<string, string>? images = null)
        : this(task, result, null, images)
    {
    }

    /// <summary>Создаёт контекст с моделью сечения и встроенными иллюстрациями.</summary>
    public ReportContext(CalcTask task, CalcResult result, CrossSection? section,
        IReadOnlyDictionary<string, string>? images = null)
    {
        Task = task;
        Result = result;
        Section = section;
        Images = images ?? new Dictionary<string, string>();
    }
}

/// <summary>Общий контракт поставщика отчёта для отдельного типа расчётной задачи.</summary>
public interface IReportProvider
{
    /// <summary>Проверяет, поддерживает ли поставщик тип задачи.</summary>
    bool CanHandle(CalcTask task);

    /// <summary>Строит нейтральный документ отчёта.</summary>
    ReportDocument Build(ReportContext context);
}
