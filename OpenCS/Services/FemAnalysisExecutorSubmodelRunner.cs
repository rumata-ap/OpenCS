using CScore;
using CScore.Fem;

namespace OpenCS.Services;

/// <summary>Раннер постановок субмодели через штатный <see cref="Tasks.FemAnalysisExecutor"/> приложения
/// (сечения, материалы, диаграммы и настройки OpenSees берутся из <see cref="AppViewModel"/>).</summary>
public sealed class FemAnalysisExecutorSubmodelRunner(AppViewModel app) : ISubmodelAnalysisRunner
{
    public Task<CalcResult> RunAsync(FemSchema schema, FemAnalysis analysis, CancellationToken ct) =>
        Tasks.FemAnalysisExecutor.RunAsync(app, schema, analysis, ct);
}
