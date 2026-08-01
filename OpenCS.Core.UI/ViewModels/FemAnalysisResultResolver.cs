using CScore.Fem;

namespace OpenCS.ViewModels;

/// <summary>Выбирает сохранённый результат FEM-анализа для просмотра.</summary>
public static class FemAnalysisResultResolver
{
    /// <summary>Возвращает самый новый пригодный для просмотра анализ, включая частично сошедшийся.</summary>
    public static FemAnalysis? FindLatestWithResult(IEnumerable<FemAnalysis> analyses)
    {
        ArgumentNullException.ThrowIfNull(analyses);
        return analyses
            .Where(a => a.ResultId is > 0 &&
                (a.Status is "ok" or "not_converged" or "partial"))
            .OrderByDescending(a => a.Id)
            .FirstOrDefault();
    }
}
