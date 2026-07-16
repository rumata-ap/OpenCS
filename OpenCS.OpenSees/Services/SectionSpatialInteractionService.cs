using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Runtime;

namespace OpenCS.OpenSees.Services;

/// <summary>
/// Последовательно выполняет полный набор лучей пространственной диаграммы N–Mx–My.
/// </summary>
public sealed class SectionSpatialInteractionService
{
    private readonly ISpatialSectionAnalysisExecutor _executor;

    /// <summary>Создаёт оркестратор с исполнителем одного пространственного прогона.</summary>
    public SectionSpatialInteractionService(ISpatialSectionAnalysisExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    /// <summary>
    /// Выполняет лучи в порядке продольных сил, а внутри каждой силы — в порядке углов от 0 до 360°.
    /// </summary>
    public async Task<SectionSpatialInteractionResult> RunAsync(
        OpenSeesSectionModel model,
        SectionSpatialInteractionRequest request,
        OpenSeesRunRequest processRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(processRequest);

        model.Validate();
        request.Validate();

        List<SectionSpatialInteractionPoint> points = [];
        List<string> diagnostics = [];

        foreach (double axialForce in request.AxialForcesN)
        {
            foreach (double angle in request.GenerateAnglesDegrees())
            {
                cancellationToken.ThrowIfCancellationRequested();

                SpatialSectionAnalysisResult analysis;
                try
                {
                    analysis = await _executor.RunAsync(
                        model,
                        new SpatialSectionAnalysisRequest
                        {
                            AxialForceN = axialForce,
                            AngleDegrees = angle,
                            MaxCurvature = request.MaxCurvature,
                            Increments = request.Increments,
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
                    string message = exception.Message;
                    points.Add(new SectionSpatialInteractionPoint
                    {
                        AxialForceN = axialForce,
                        AngleDegrees = angle,
                        Status = "error",
                        Diagnostics = [message]
                    });
                    diagnostics.Add(FormatDiagnostic(axialForce, angle, message));
                    continue;
                }

                SpatialSectionHistoryRow? terminal = analysis.Rows.LastOrDefault(row => row.Converged);
                SectionSpatialInteractionPoint point = new()
                {
                    AxialForceN = axialForce,
                    AngleDegrees = angle,
                    MomentMxNm = terminal?.MomentMxNm,
                    MomentMyNm = terminal?.MomentMyNm,
                    CurvatureMx = terminal?.CurvatureMx,
                    CurvatureMy = terminal?.CurvatureMy,
                    TerminalRow = terminal,
                    HistoryRows = analysis.Rows.ToArray(),
                    Status = analysis.Status,
                    Diagnostics = analysis.Diagnostics,
                    ArtifactDirectory = analysis.ArtifactDirectory
                };
                points.Add(point);

                foreach (string diagnostic in analysis.Diagnostics)
                    diagnostics.Add(FormatDiagnostic(axialForce, angle, diagnostic));
            }
        }

        string status = points.Any(point => point.Status == "error")
            ? "error"
            : points.Any(point => point.Status != "ok")
                ? "not_converged"
                : "ok";

        return new SectionSpatialInteractionResult
        {
            Status = status,
            Points = points,
            Diagnostics = diagnostics
        };
    }

    private static string FormatDiagnostic(double axialForce, double angle, string diagnostic) =>
        $"N={axialForce}, angle={angle}°: {diagnostic}";
}
