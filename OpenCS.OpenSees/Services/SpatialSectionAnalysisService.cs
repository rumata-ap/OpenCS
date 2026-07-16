using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Tcl;

namespace OpenCS.OpenSees.Services;

/// <summary>
/// Оркестрирует один пространственный запуск OpenSees и разбор его артефактов.
/// </summary>
public sealed class SpatialSectionAnalysisService : ISpatialSectionAnalysisExecutor
{
    private readonly ISpatialSectionTclGenerator _generator;
    private readonly IOpenSeesProcessRunner _runner;
    private readonly OpenSeesArtifactStore _artifactStore;
    private readonly SpatialSectionResultParser _parser;

    /// <summary>
    /// Создаёт сервис с внедряемыми генератором, runner-ом и хранилищем артефактов.
    /// </summary>
    public SpatialSectionAnalysisService(
        ISpatialSectionTclGenerator generator,
        IOpenSeesProcessRunner runner,
        OpenSeesArtifactStore artifactStore,
        SpatialSectionResultParser? parser = null)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _parser = parser ?? new SpatialSectionResultParser();
    }

    /// <inheritdoc />
    public async Task<SpatialSectionAnalysisResult> RunAsync(
        OpenSeesSectionModel model,
        SpatialSectionAnalysisRequest request,
        OpenSeesRunRequest processRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(processRequest);

        cancellationToken.ThrowIfCancellationRequested();

        OpenSeesArtifact? artifact = null;
        try
        {
            string script = _generator.Generate(model, request);
            cancellationToken.ThrowIfCancellationRequested();

            artifact = _artifactStore.Create();
            _artifactStore.WriteScript(artifact, script);
            _artifactStore.WriteManifest(artifact, new OpenSeesManifest { Status = "running" });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ErrorResult(exception.Message, artifact?.DirectoryPath ?? "");
        }

        OpenSeesRunRequest actualRequest = new()
        {
            ExecutablePath = processRequest.ExecutablePath,
            Arguments = processRequest.Arguments,
            WorkingDirectory = artifact.DirectoryPath,
            ScriptPath = artifact.ScriptPath,
            Timeout = processRequest.Timeout
        };

        OpenSeesRunResult runResult;
        try
        {
            runResult = await _runner.RunAsync(actualRequest, cancellationToken);
            _artifactStore.WriteRunResult(artifact, runResult);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string message = exception.Message;
            _artifactStore.WriteManifest(artifact, new OpenSeesManifest
            {
                Status = "error",
                Diagnostics = [message]
            });
            return ErrorResult(message, artifact.DirectoryPath);
        }

        List<string> diagnostics = [];
        if (runResult.ExitCode != 0)
            diagnostics.Add($"OpenSees завершился с кодом {runResult.ExitCode}.");
        if (runResult.TimedOut)
            diagnostics.Add("OpenSees остановлен по таймауту.");
        if (runResult.Cancelled)
            diagnostics.Add("Запуск OpenSees отменён.");
        if (!string.IsNullOrWhiteSpace(runResult.Stderr))
            diagnostics.Add(runResult.Stderr.Trim());

        IReadOnlyList<SpatialSectionHistoryRow> rows = [];
        bool parserFailed = false;
        try
        {
            rows = _parser.Parse(
                Path.Combine(artifact.DirectoryPath, "section_history.out"),
                Path.Combine(artifact.DirectoryPath, "completed.marker"));
        }
        catch (OpenSeesResultException exception)
        {
            parserFailed = true;
            diagnostics.Add($"{exception.Code}: {exception.Message}");
        }
        catch (Exception exception)
        {
            parserFailed = true;
            diagnostics.Add(exception.Message);
        }

        bool converged = rows.Count > 0 && rows.All(row => row.Converged);
        string status = parserFailed
            ? "error"
            : runResult.ExitCode == 0 && !runResult.TimedOut && !runResult.Cancelled && converged
                ? "ok"
                : "not_converged";

        _artifactStore.WriteManifest(artifact, new OpenSeesManifest
        {
            Status = status,
            ExitCode = runResult.ExitCode,
            Diagnostics = diagnostics
        });

        return new SpatialSectionAnalysisResult
        {
            Status = status,
            Rows = rows,
            Diagnostics = diagnostics,
            ArtifactDirectory = artifact.DirectoryPath,
            RunResult = runResult
        };
    }

    private static SpatialSectionAnalysisResult ErrorResult(string message, string artifactDirectory) => new()
    {
        Status = "error",
        Diagnostics = [message],
        ArtifactDirectory = artifactDirectory
    };
}
