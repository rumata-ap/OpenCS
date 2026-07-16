using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Runtime;

namespace OpenCS.OpenSees.Services;

/// <summary>
/// Контракт запуска одного пространственного расчёта moment-curvature.
/// </summary>
public interface ISpatialSectionAnalysisExecutor
{
    /// <summary>
    /// Выполняет один луч расчёта при заданных N и направлении кривизны.
    /// </summary>
    Task<SpatialSectionAnalysisResult> RunAsync(
        OpenSeesSectionModel model,
        SpatialSectionAnalysisRequest request,
        OpenSeesRunRequest processRequest,
        CancellationToken cancellationToken);
}
