using CScore.Fem;

namespace OpenCS.ViewModels;

/// <summary>Выбирает сохранённый результат FEM-анализа для просмотра.</summary>
public static class FemAnalysisResultResolver
{
    /// <summary>Возвращает самый новый пригодный для просмотра анализ, включая частично сошедшийся. Секущий расчёт CSfea
    /// пропускается: у его результата свой формат (окно просмотра — срез 4е).</summary>
    public static FemAnalysis? FindLatestWithResult(IEnumerable<FemAnalysis> analyses)
    {
        ArgumentNullException.ThrowIfNull(analyses);
        return analyses
            .Where(a => a.ResultId is > 0 && a.Kind != OpenCS.Services.FemCsfeaRunner.AnalysisKind &&
                (a.Status is "ok" or "not_converged" or "partial"))
            .OrderByDescending(a => a.Id)
            .FirstOrDefault();
    }
}
