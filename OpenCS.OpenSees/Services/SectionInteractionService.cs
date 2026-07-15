using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Runtime;

namespace OpenCS.OpenSees.Services;

/// <summary>Последовательно строит одноосную диаграмму взаимодействия N-M.</summary>
public sealed class SectionInteractionService
{
    private readonly ISectionAnalysisExecutor _executor;

    /// <summary>Создаёт сервис с исполнителем одного внутреннего анализа.</summary>
    public SectionInteractionService(ISectionAnalysisExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    /// <summary>Запускает отдельный monotonic moment-curvature анализ для каждой силы.</summary>
    public async Task<SectionInteractionResult> RunAsync(
        OpenSeesSectionModel model,
        SectionInteractionRequest request,
        OpenSeesRunRequest processRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(processRequest);

        model.Validate();
        request.Validate();

        List<SectionInteractionPoint> points = [];
        List<string> diagnostics = [];

        foreach (double axialForce in request.AxialForcesN)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SectionAnalysisResult analysis;
            try
            {
                analysis = await _executor.RunAsync(
                    model,
                    new SectionAnalysisRequest
                    {
                        AxialForceN = axialForce,
                        MaxCurvature = request.MaxCurvature,
                        Increments = request.Increments,
                        Axis = request.Axis,
                        Convention = request.Convention
                    },
                    processRequest,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                points.Add(new SectionInteractionPoint
                {
                    AxialForceN = axialForce,
                    Status = "error",
                    Diagnostics = [exception.Message]
                });
                diagnostics.Add($"N={axialForce}: {exception.Message}");
                continue;
            }

            SectionHistoryRow? lastConverged = analysis.Rows.LastOrDefault(row => row.Converged);
            points.Add(new SectionInteractionPoint
            {
                AxialForceN = axialForce,
                BendingMomentNm = lastConverged?.BendingMomentNm,
                Curvature = lastConverged?.Curvature,
                TerminalRow = lastConverged,
                Status = analysis.Status,
                Diagnostics = analysis.Diagnostics,
                ArtifactDirectory = analysis.ArtifactDirectory
            });

            foreach (string diagnostic in analysis.Diagnostics)
                diagnostics.Add($"N={axialForce}: {diagnostic}");
        }

        string status = points.Any(point => point.Status == "error")
            ? "error"
            : points.Any(point => point.Status != "ok")
                ? "not_converged"
                : "ok";

        return new SectionInteractionResult
        {
            Status = status,
            Points = points,
            Diagnostics = diagnostics
        };
    }
}
