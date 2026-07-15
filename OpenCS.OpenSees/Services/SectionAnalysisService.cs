using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Tcl;

namespace OpenCS.OpenSees.Services;

/// <summary>Оркестрирует генерацию Tcl, внешний запуск, parsing и сохранение артефактов.</summary>
public sealed class SectionAnalysisService : ISectionAnalysisExecutor
{
    private readonly IOpenSeesTclGenerator _generator;
    private readonly IOpenSeesProcessRunner _runner;
    private readonly OpenSeesArtifactStore _artifactStore;
    private readonly SectionResultParser _parser;

    /// <summary>Создаёт сервис с внедряемыми генератором, runner’ом и хранилищем.</summary>
    public SectionAnalysisService(
        IOpenSeesTclGenerator generator,
        IOpenSeesProcessRunner runner,
        OpenSeesArtifactStore artifactStore,
        SectionResultParser? parser = null)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _parser = parser ?? new SectionResultParser();
    }

    /// <summary>Выполняет полный цикл анализа с сохранением артефактов при любой ошибке.</summary>
    public async Task<SectionAnalysisResult> RunAsync(
        OpenSeesSectionModel model,
        SectionAnalysisRequest request,
        OpenSeesRunRequest processRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(processRequest);

        OpenSeesArtifact artifact;
        string script;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            script = _generator.Generate(model, request);
            artifact = _artifactStore.Create();
            _artifactStore.WriteScript(artifact, script);
            _artifactStore.WriteManifest(artifact, new OpenSeesManifest { Status = "running" });
        }
        catch (Exception exception)
        {
            return new SectionAnalysisResult
            {
                Status = "error",
                Diagnostics = [exception.Message]
            };
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
        catch (Exception exception)
        {
            string message = exception.Message;
            _artifactStore.WriteManifest(artifact, new OpenSeesManifest
            {
                Status = "error",
                Diagnostics = [message]
            });
            return new SectionAnalysisResult
            {
                Status = "error",
                Diagnostics = [message],
                ArtifactDirectory = artifact.DirectoryPath
            };
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

        IReadOnlyList<SectionHistoryRow> rows = [];
        try
        {
            rows = _parser.Parse(artifact.DirectoryPath + "\\section_history.out", artifact.DirectoryPath + "\\completed.marker", request.Axis);
        }
        catch (OpenSeesResultException exception)
        {
            diagnostics.Add($"{exception.Code}: {exception.Message}");
        }

        bool converged = rows.Count > 0 && rows.All(row => row.Converged);
        string status = runResult.ExitCode == 0 && !runResult.TimedOut && !runResult.Cancelled && converged
            ? "ok"
            : "not_converged";

        _artifactStore.WriteManifest(artifact, new OpenSeesManifest
        {
            Status = status,
            ExitCode = runResult.ExitCode,
            Diagnostics = diagnostics
        });

        return new SectionAnalysisResult
        {
            Status = status,
            Rows = rows,
            Diagnostics = diagnostics,
            ArtifactDirectory = artifact.DirectoryPath,
            RunResult = runResult
        };
    }
}
