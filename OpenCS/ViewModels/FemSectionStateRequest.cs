using OpenCS.OpenSees.CScore;

namespace OpenCS.ViewModels;

/// <summary>Запрос показа состояния сечения в точке интегрирования FEM-результата OpenSees.
/// Формируется FemAnalysisResultVM, обрабатывается AppViewModel (открытие окна).</summary>
public sealed record FemSectionStateRequest(
    FemSectionLocationRow Location,
    int SectionId,
    string CalcTypeName,
    IReadOnlyDictionary<int, (double StressPa, double Strain)> RecordedFibers,
    string StepLabel,
    bool Converged,
    string PositionLabel);
