using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Runtime;

namespace OpenCS.OpenSees.Services;

/// <summary>Контракт запуска одного анализа moment-curvature.</summary>
public interface ISectionAnalysisExecutor
{
    /// <summary>Запускает один внутренний расчёт сечения.</summary>
    Task<SectionAnalysisResult> RunAsync(
        OpenSeesSectionModel model,
        SectionAnalysisRequest request,
        OpenSeesRunRequest processRequest,
        CancellationToken cancellationToken);
}
