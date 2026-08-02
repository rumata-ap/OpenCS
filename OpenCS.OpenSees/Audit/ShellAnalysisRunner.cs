using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;

namespace OpenCS.OpenSees.Audit;

/// <summary>Реализация IShellAnalysisRunner поверх generator, artifact store,
/// process runner и result parser.</summary>
public sealed class ShellAnalysisRunner : IShellAnalysisRunner
{
    private readonly ShellTclGenerator _generator;
    private readonly OpenSeesArtifactStore _artifactStore;
    private readonly IOpenSeesProcessRunner _processRunner;
    private readonly ShellResultParser _resultParser;
    private readonly TimeSpan _timeout;

    /// <summary>Создаёт runner. При timeout == default используется 60 секунд.</summary>
    public ShellAnalysisRunner(
        ShellTclGenerator generator,
        OpenSeesArtifactStore artifactStore,
        IOpenSeesProcessRunner processRunner,
        ShellResultParser resultParser,
        TimeSpan timeout = default)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _resultParser = resultParser ?? throw new ArgumentNullException(nameof(resultParser));
        _timeout = timeout == default ? TimeSpan.FromSeconds(60) : timeout;
    }

    /// <inheritdoc />
    public async Task<ShellAnalysisRunResult> RunAsync(
        ShellOpenSeesModel model,
        string executablePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        OpenSeesArtifact artifact = _artifactStore.Create();
        _artifactStore.WriteScript(artifact, _generator.Generate(model));

        OpenSeesRunResult run = await _processRunner.RunAsync(
            new OpenSeesRunRequest
            {
                ExecutablePath = executablePath,
                WorkingDirectory = artifact.DirectoryPath,
                ScriptPath = artifact.ScriptPath,
                Timeout = _timeout
            }, cancellationToken);

        _artifactStore.WriteRunResult(artifact, run);

        ShellResult? parsed = null;
        string? parseError = null;
        try
        {
            parsed = _resultParser.Parse(
                artifact.DirectoryPath, model.Elements.ToDictionary(element => element.Tag));
        }
        catch (Exception exception)
        {
            parseError = exception.Message;
        }

        ShellAnalysisOutcome outcome = DetermineOutcome(run, parsed, parseError);
        string? errorMessage = parseError ??
            (outcome is ShellAnalysisOutcome.ExecutionFailed or ShellAnalysisOutcome.TimedOut ? run.Stderr : null);

        return new ShellAnalysisRunResult(outcome, parsed, artifact.DirectoryPath, errorMessage);
    }

    /// <summary>Определяет outcome с приоритетом timeout, exit code, parse error и сходимости.</summary>
    public static ShellAnalysisOutcome DetermineOutcome(
        OpenSeesRunResult processResult,
        ShellResult? parsed,
        string? parseError)
    {
        ArgumentNullException.ThrowIfNull(processResult);

        if (processResult.TimedOut)
            return ShellAnalysisOutcome.TimedOut;
        if (processResult.ExitCode != 0)
            return ShellAnalysisOutcome.ExecutionFailed;
        if (parseError is not null || parsed is null)
            return ShellAnalysisOutcome.ParseFailed;
        if (parsed.Steps.Any(step => step.Converged))
            return ShellAnalysisOutcome.Completed;
        return ShellAnalysisOutcome.NotConverged;
    }
}
