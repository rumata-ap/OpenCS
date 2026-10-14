using CScore;
using CScore.Fem;

namespace OpenCS.Services;

/// <summary>
/// Выполняет расчёт одной постановки схемы и возвращает <b>несохранённый</b> <see cref="CalcResult"/>.
/// Сохранение и привязку результата к постановке делает вызывающий сервис. В приложении —
/// <see cref="FemAnalysisExecutorSubmodelRunner"/>, в тестах — живой OpenSees или заготовленный результат.
/// </summary>
public interface ISubmodelAnalysisRunner
{
    Task<CalcResult> RunAsync(FemSchema schema, FemAnalysis analysis, CancellationToken ct);
}
