using System.IO;
using System.Text.Json;
using CScore;
using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;
using OpenCS.OpenSees.Tcl;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Обработчик задачи одноосной диаграммы N-M через внешний OpenSees.</summary>
public sealed class OpenSeesSectionInteractionHandler : ITaskHandler
{
    /// <inheritdoc />
    public string Kind => "opensees_section_interaction_nm";

    /// <inheritdoc />
    public CalcResult Run(
        CalcTask task,
        CrossSection section,
        LoadItem item,
        CalcSettings settings,
        TaskRunContext? ctx = null)
    {
        string created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            ctx?.CancellationToken.ThrowIfCancellationRequested();
            OpenSeesSectionInteractionParams parameters =
                OpenSeesSectionInteractionParams.Parse(task.ParamsJson);
            SectionBendingAxis axis = parameters.Axis == "My"
                ? SectionBendingAxis.My
                : SectionBendingAxis.Mx;

            Dictionary<int, Material> materials = ctx?.Database?.Materials
                .Where(material => material.Id != 0)
                .ToDictionary(material => material.Id)
                ?? [];

            OpenSeesSectionModel model = CrossSectionToOpenSeesAdapter.Build(
                section,
                task.CalcType,
                materials,
                ctx?.Database?.Diagrams,
                new CrossSectionToOpenSeesAdapter.Options());

            OpenSeesExecutableInfo executable = new OpenSeesExecutableResolver(
                Path.Combine(AppContext.BaseDirectory, "OpenSees.exe"))
                .Resolve(parameters.ExecutablePath);

            SectionInteractionRequest request = new()
            {
                AxialForcesN = parameters.AxialForcesKn
                    .Select(CScoreUnitConverter.KiloNewtonsToNewtons)
                    .ToArray(),
                MaxCurvature = parameters.MaxCurvature,
                Increments = parameters.Increments,
                Axis = axis,
                Convention = model.Convention
            };

            SectionAnalysisService analysisExecutor = new(
                new SectionMomentCurvatureTclGenerator(),
                new OpenSeesProcessRunner(),
                new OpenSeesArtifactStore(Path.Combine(AppContext.BaseDirectory, "OpenSeesArtifacts")));
            SectionInteractionResult analysis = new SectionInteractionService(analysisExecutor)
                .RunAsync(
                    model,
                    request,
                    new OpenSeesRunRequest
                    {
                        ExecutablePath = executable.Path,
                        WorkingDirectory = Path.GetTempPath(),
                        Timeout = TimeSpan.FromSeconds(parameters.TimeoutSeconds)
                    },
                    ctx?.CancellationToken ?? CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            return new CalcResult
            {
                TaskId = task.Id,
                TaskKind = task.Kind,
                TaskTag = task.Tag,
                Created = created,
                Status = analysis.Status,
                DataJson = JsonSerializer.Serialize(analysis)
            };
        }
        catch (OperationCanceledException exception)
        {
            return ErrorResult(task, created, Loc.S("OpenSeesTaskCancelled"), exception.Message, "not_converged");
        }
        catch (Exception exception)
        {
            return ErrorResult(task, created, Loc.S("OpenSeesTaskErrorFormat"), exception.Message, "error");
        }
    }

    private static CalcResult ErrorResult(
        CalcTask task,
        string created,
        string format,
        string detail,
        string status)
    {
        string message = format.Contains("{0}", StringComparison.Ordinal)
            ? string.Format(format, detail)
            : format;
        return new CalcResult
        {
            TaskId = task.Id,
            TaskKind = task.Kind,
            TaskTag = task.Tag,
            Created = created,
            Status = status,
            DataJson = JsonSerializer.Serialize(new { error = message })
        };
    }
}
