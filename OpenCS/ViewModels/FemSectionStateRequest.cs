using OpenCS.OpenSees.CScore;

namespace OpenCS.ViewModels;

/// <summary>Запрос показа состояния сечения в точке интегрирования FEM-результата OpenSees.
/// Формируется FemAnalysisResultVM, обрабатывается AppViewModel (открытие окна).
/// Чтение записанных волокон ленивое: вызывается из фонового потока, т.к. файл
/// состояний может быть большим (сотни МБ).</summary>
public sealed record FemSectionStateRequest(
    FemSectionLocationRow Location,
    int SectionId,
    string CalcTypeName,
    Func<IReadOnlyDictionary<int, (double StressPa, double Strain)>> LoadRecordedFibers,
    string StepLabel,
    bool Converged,
    string PositionLabel);
