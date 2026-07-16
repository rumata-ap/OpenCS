using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Tcl;

/// <summary>
/// Контракт генератора детерминированного Tcl-сценария пространственного расчёта сечения.
/// </summary>
public interface ISpatialSectionTclGenerator
{
    /// <summary>
    /// Генерирует сценарий одного луча кривизн при заданной продольной силе.
    /// </summary>
    string Generate(OpenSeesSectionModel model, SpatialSectionAnalysisRequest request);
}
