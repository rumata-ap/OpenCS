using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;

namespace OpenCS.OpenSees.Services;

/// <summary>Оркестрирует линейный расчёт FEM-схемы: Tcl → запуск → парсинг → артефакты.</summary>
public sealed class FemLinearAnalysisService
{
    private readonly FemLinearTclGenerator _generator;
    private readonly IOpenSeesProcessRunner _runner;
    private readonly OpenSeesArtifactStore _store;
    private readonly FemLinearResultParser _parser;

    public FemLinearAnalysisService(
        FemLinearTclGenerator generator,
        IOpenSeesProcessRunner runner,
        OpenSeesArtifactStore store,
        FemLinearResultParser? parser = null)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _parser = parser ?? new FemLinearResultParser();
    }

    /// <summary>Выполняет полный цикл линейного расчёта, сохраняя артефакты при любой ошибке.</summary>
    public async Task<FemLinearResult> RunAsync(FemLinearModel model, OpenSeesRunRequest processRequest, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(processRequest);

        OpenSeesArtifact artifact;
        try
        {
            ct.ThrowIfCancellationRequested();
            string script = _generator.Generate(model);
            artifact = _store.Create();
            _store.WriteScript(artifact, script);
            _store.WriteManifest(artifact, new OpenSeesManifest { Status = "running" });
        }
        catch (Exception ex)
        {
            return new FemLinearResult { Status = "error", Diagnostics = [ex.Message] };
        }

        var actual = new OpenSeesRunRequest
        {
            ExecutablePath = processRequest.ExecutablePath,
            Arguments = processRequest.Arguments,
            WorkingDirectory = artifact.DirectoryPath,
            ScriptPath = artifact.ScriptPath,
            Timeout = processRequest.Timeout
        };

        OpenSeesRunResult run;
        try
        {
            run = await _runner.RunAsync(actual, ct);
            _store.WriteRunResult(artifact, run);
        }
        catch (Exception ex)
        {
            _store.WriteManifest(artifact, new OpenSeesManifest { Status = "error", Diagnostics = [ex.Message] });
            return new FemLinearResult { Status = "error", Diagnostics = [ex.Message], ArtifactDirectory = artifact.DirectoryPath };
        }

        var diagnostics = new List<string>();
        if (run.ExitCode != 0) diagnostics.Add($"OpenSees завершился с кодом {run.ExitCode}.");
        if (run.TimedOut) diagnostics.Add("OpenSees остановлен по таймауту.");
        if (run.Cancelled) diagnostics.Add("Запуск OpenSees отменён.");
        if (!string.IsNullOrWhiteSpace(run.Stderr)) diagnostics.Add(run.Stderr.Trim());

        IReadOnlyList<FemNodeDisplacement> disp = [];
        IReadOnlyList<FemNodeReaction> react = [];
        IReadOnlyList<FemElementEndForces> forces = [];
        bool parsed = false;
        try
        {
            (disp, react, forces) = _parser.Parse(artifact.DirectoryPath);
            parsed = true;
        }
        catch (OpenSeesResultException ex)
        {
            diagnostics.Add($"{ex.Code}: {ex.Message}");
        }

        string status = run.ExitCode == 0 && !run.TimedOut && !run.Cancelled && parsed ? "ok"
            : run.Cancelled ? "cancelled"
            : parsed ? "not_converged" : "error";

        _store.WriteManifest(artifact, new OpenSeesManifest { Status = status, ExitCode = run.ExitCode, Diagnostics = diagnostics });

        return new FemLinearResult
        {
            Status = status,
            Displacements = disp,
            Reactions = react,
            ElementForces = forces,
            Diagnostics = diagnostics,
            ArtifactDirectory = artifact.DirectoryPath
        };
    }
}
