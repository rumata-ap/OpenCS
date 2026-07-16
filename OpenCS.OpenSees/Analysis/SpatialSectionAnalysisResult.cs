using OpenCS.OpenSees.Runtime;

namespace OpenCS.OpenSees.Analysis;

/// <summary>Результат одного пространственного OpenSees-запуска.</summary>
public sealed class SpatialSectionAnalysisResult
{
    /// <summary>Статус: ok, not_converged или error.</summary>
    public string Status { get; init; } = "error";

    /// <summary>Полная радиальная история.</summary>
    public IReadOnlyList<SpatialSectionHistoryRow> Rows { get; init; } = [];

    /// <summary>Диагностика запуска и разбора результата.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];

    /// <summary>Каталог артефактов запуска.</summary>
    public string ArtifactDirectory { get; init; } = "";

    /// <summary>Результат внешнего процесса.</summary>
    public OpenSeesRunResult? RunResult { get; init; }
}
