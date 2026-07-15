using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Tcl;

/// <summary>Контракт генератора детерминированного Tcl-сценария OpenSees.</summary>
public interface IOpenSeesTclGenerator
{
    /// <summary>Генерирует сценарий анализа заданной fiber-секции.</summary>
    string Generate(OpenSeesSectionModel model, SectionAnalysisRequest request);
}
