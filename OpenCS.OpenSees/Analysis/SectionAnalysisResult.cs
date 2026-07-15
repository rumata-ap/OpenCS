using OpenCS.OpenSees.Runtime;

namespace OpenCS.OpenSees.Analysis;

/// <summary>Типизированный результат анализа секции OpenSees.</summary>
public sealed class SectionAnalysisResult
{
    /// <summary>Статус: ok, not_converged или error.</summary>
    public string Status { get; init; } = "error";

    /// <summary>Распарсенные строки истории.</summary>
    public IReadOnlyList<SectionHistoryRow> Rows { get; init; } = [];

    /// <summary>Диагностика процесса, parser и отсутствующих артефактов.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];

    /// <summary>Каталог сохранённых артефактов.</summary>
    public string ArtifactDirectory { get; init; } = "";

    /// <summary>Результат запуска внешнего процесса.</summary>
    public OpenSeesRunResult? RunResult { get; init; }
}
