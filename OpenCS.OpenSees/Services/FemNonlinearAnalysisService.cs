using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;

namespace OpenCS.OpenSees.Services;

/// <summary>Оркестрирует нелинейный расчёт FEM-схемы: Tcl → запуск → парсинг → артефакты.</summary>
public sealed class FemNonlinearAnalysisService
{
    /// <summary>Причины завершения стадии, считающиеся успехом. Всё остальное (включая
    /// "failed", "not_run_due_to_previous_failure" и незнакомые будущие строки) —
    /// не считается успехом.</summary>
    public static readonly IReadOnlySet<string> SuccessfulStageReasons = new HashSet<string>
    {
        "load_control_completed", "target_reached", "max_steps_reached",
        "zero_step_target_already_reached", "continuation_target_reached",
        "continuation_max_steps_reached", "continuation_zero_step_target_already_reached"
    };

    /// <summary>Целостность stage-sidecar (path_control_stage_status.out) — единственный
    /// источник истины о том, чем закончилась каждая стадия. Проверяет ТОЧНОЕ совпадение
    /// множества индексов {0..totalStages-1} (не только Count — два дубля на один индекс и
    /// пропуск другого дали бы одинаковый Count), и что каждая причина входит в whitelist
    /// SuccessfulStageReasons.</summary>
    public static bool ComputeAllConverged(bool parsed, IReadOnlyList<FemStageCompletion> stageCompletions,
        IReadOnlyList<FemNonlinearStepResult> steps, int totalStages)
    {
        if (!parsed) return false;
        if (stageCompletions.Count != totalStages) return false;
        var indices = stageCompletions.Select(c => c.StageIndex).ToHashSet();
        if (indices.Count != totalStages || !indices.SetEquals(Enumerable.Range(0, totalStages)))
            return false;
        if (!stageCompletions.All(c => SuccessfulStageReasons.Contains(c.Reason)))
            return false;
        return steps.All(s => s.Converged);
    }

    private readonly FemNonlinearTclGenerator _generator;
    private readonly IOpenSeesProcessRunner _runner;
    private readonly OpenSeesArtifactStore _store;
    private readonly FemNonlinearResultParser _parser;

    public FemNonlinearAnalysisService(
        FemNonlinearTclGenerator generator,
        IOpenSeesProcessRunner runner,
        OpenSeesArtifactStore store,
        FemNonlinearResultParser? parser = null)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _parser = parser ?? new FemNonlinearResultParser();
    }

    /// <summary>Выполняет полный цикл нелинейного расчёта, сохраняя артефакты при любой ошибке.</summary>
    public async Task<FemNonlinearResult> RunAsync(FemNonlinearModel model, OpenSeesRunRequest processRequest, CancellationToken ct)
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
            return new FemNonlinearResult { Status = "error", Diagnostics = [ex.Message] };
        }

        var actual = new OpenSeesRunRequest
        {
            ExecutablePath = processRequest.ExecutablePath,
            Arguments = processRequest.Arguments,
            WorkingDirectory = artifact.DirectoryPath,
            ScriptPath = artifact.ScriptPath,
            Timeout = processRequest.Timeout,
            OnOutputLine = processRequest.OnOutputLine
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
            return new FemNonlinearResult { Status = "error", Diagnostics = [ex.Message], ArtifactDirectory = artifact.DirectoryPath };
        }

        var diagnostics = new List<string>();
        if (run.ExitCode != 0) diagnostics.Add($"OpenSees завершился с кодом {run.ExitCode}.");
        if (run.TimedOut) diagnostics.Add("OpenSees остановлен по таймауту.");
        if (run.Cancelled) diagnostics.Add("Запуск OpenSees отменён.");
        if (!string.IsNullOrWhiteSpace(run.Stderr)) diagnostics.Add(run.Stderr.Trim());

        IReadOnlyList<FemNonlinearStepResult> steps = [];
        bool parsed = false;
        try
        {
            steps = _parser.Parse(artifact.DirectoryPath);
            parsed = true;
        }
        catch (OpenSeesResultException ex)
        {
            diagnostics.Add($"{ex.Code}: {ex.Message}");
        }

        IReadOnlyList<FemPathControlSwitch> switches = [];
        IReadOnlyList<FemStageCompletion> stageCompletions = [];
        if (parsed)
        {
            try
            {
                switches = _parser.ParseSwitches(Path.Combine(artifact.DirectoryPath, "path_control_switches.out"));
                stageCompletions = _parser.ParseStageCompletions(Path.Combine(artifact.DirectoryPath, "path_control_stage_status.out"));
            }
            catch (OpenSeesResultException ex)
            {
                diagnostics.Add($"{ex.Code}: {ex.Message}");
                parsed = false; // структурная порча sidecar — тот же уровень серьёзности, что порча step_status.out
            }
        }

        bool allConverged = ComputeAllConverged(parsed, stageCompletions, steps, model.Stages.Count);
        string status = run.ExitCode == 0 && !run.TimedOut && !run.Cancelled && allConverged ? "ok"
            : run.Cancelled ? "cancelled"
            : parsed ? "not_converged" : "error";

        _store.WriteManifest(artifact, new OpenSeesManifest { Status = status, ExitCode = run.ExitCode, Diagnostics = diagnostics });

        return new FemNonlinearResult
        {
            Status = status,
            Steps = steps,
            Diagnostics = diagnostics,
            ArtifactDirectory = artifact.DirectoryPath,
            LimitReached = steps.Any(s => !s.Converged),
            LastConvergedLoadFactor = steps.Where(s => s.Converged)
                .Select(s => s.LoadFactor).DefaultIfEmpty(0).Max(),
            FailedLoadFactor = steps.FirstOrDefault(s => !s.Converged)?.LoadFactor,
            RefinementDivisions = model.Policy.RefinementDivisions,
            CalcTypeName = model.CalcTypeName,
            StageTags = model.Stages.Select(s => s.Tag).ToList(),
            StagePathControls = model.Stages.Select(s => s.PathControl).ToList(),
            PathControlSwitches = switches,
            StageCompletions = stageCompletions,
            FiberStateFileName = File.Exists(Path.Combine(artifact.DirectoryPath, "nonlinear_fiber_states.out"))
                ? "nonlinear_fiber_states.out" : null,
            SectionOrderFileName = File.Exists(Path.Combine(artifact.DirectoryPath, "nonlinear_section_order.json"))
                ? "nonlinear_section_order.json" : null
        };
    }
}
