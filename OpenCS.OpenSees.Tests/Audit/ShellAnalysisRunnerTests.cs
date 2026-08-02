using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;

namespace OpenCS.OpenSees.Tests.Audit;

public sealed class ShellAnalysisRunnerTests
{
    [Fact]
    public void DetermineOutcome_NonZeroExitCode_IsExecutionFailedEvenWhenStatusCompleted()
    {
        var process = new OpenSeesRunResult { ExitCode = 1 };
        var parsed = new ShellResult { Status = "completed" };

        Assert.Equal(ShellAnalysisOutcome.ExecutionFailed,
            ShellAnalysisRunner.DetermineOutcome(process, parsed, parseError: null));
    }

    [Fact]
    public void DetermineOutcome_TimedOut_IsTimedOut()
    {
        var process = new OpenSeesRunResult { ExitCode = 0, TimedOut = true };

        Assert.Equal(ShellAnalysisOutcome.TimedOut,
            ShellAnalysisRunner.DetermineOutcome(process, parsed: null, parseError: null));
    }

    [Fact]
    public void DetermineOutcome_ParseError_IsParseFailed()
    {
        var process = new OpenSeesRunResult { ExitCode = 0 };

        Assert.Equal(ShellAnalysisOutcome.ParseFailed,
            ShellAnalysisRunner.DetermineOutcome(process, parsed: null, parseError: "boom"));
    }

    [Fact]
    public void DetermineOutcome_ConvergedStep_IsCompleted()
    {
        var process = new OpenSeesRunResult { ExitCode = 0 };
        var parsed = new ShellResult
        {
            Steps = [new RCShellStepResult(1, 0, 1.0, true, [], [], [], [], [])]
        };

        Assert.Equal(ShellAnalysisOutcome.Completed,
            ShellAnalysisRunner.DetermineOutcome(process, parsed, parseError: null));
    }

    [Fact]
    public void DetermineOutcome_NoConvergedStep_IsNotConvergedEvenWhenStatusCompleted()
    {
        var process = new OpenSeesRunResult { ExitCode = 0 };
        var parsed = new ShellResult
        {
            Status = "completed",
            Steps = [new RCShellStepResult(1, 0, 1.0, false, [], [], [], [], [])]
        };

        Assert.Equal(ShellAnalysisOutcome.NotConverged,
            ShellAnalysisRunner.DetermineOutcome(process, parsed, parseError: null));
    }

    [Fact]
    public async Task RunAsync_Success_WritesArtifactsAndParsesCompleted()
    {
        ShellOpenSeesModel model = ShellModelFixtures.Q4Elastic();
        using var temp = new TempRoot("opencs-audit-runner-success");
        var runner = new ShellAnalysisRunner(
            new ShellTclGenerator(),
            new OpenSeesArtifactStore(temp.Root),
            new WriteFixtureProcessRunner(model, converged: true),
            new ShellResultParser());

        ShellAnalysisRunResult result = await runner.RunAsync(model, @"C:\fake\OpenSees.exe", CancellationToken.None);

        Assert.Equal(ShellAnalysisOutcome.Completed, result.Outcome);
        Assert.NotNull(result.Result);
        Assert.NotNull(result.ArtifactDirectory);
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory!, "script.tcl")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory!, "exit.json")));
    }

    [Fact]
    public async Task RunAsync_NoConvergedStepWithMarker_IsNotConverged()
    {
        ShellOpenSeesModel model = ShellModelFixtures.Q4Elastic();
        using var temp = new TempRoot("opencs-audit-runner-notconverged");
        var runner = new ShellAnalysisRunner(
            new ShellTclGenerator(),
            new OpenSeesArtifactStore(temp.Root),
            new WriteFixtureProcessRunner(model, converged: false),
            new ShellResultParser());

        ShellAnalysisRunResult result = await runner.RunAsync(model, @"C:\fake\OpenSees.exe", CancellationToken.None);

        Assert.Equal(ShellAnalysisOutcome.NotConverged, result.Outcome);
    }

    [Fact]
    public async Task RunAsync_ParseFailure_IsParseFailed()
    {
        ShellOpenSeesModel model = ShellModelFixtures.Q4Elastic();
        using var temp = new TempRoot("opencs-audit-runner-parsefailed");
        var runner = new ShellAnalysisRunner(
            new ShellTclGenerator(),
            new OpenSeesArtifactStore(temp.Root),
            new NoOutputProcessRunner(),
            new ShellResultParser());

        ShellAnalysisRunResult result = await runner.RunAsync(model, @"C:\fake\OpenSees.exe", CancellationToken.None);

        Assert.Equal(ShellAnalysisOutcome.ParseFailed, result.Outcome);
        Assert.Null(result.Result);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public async Task RunAsync_NonZeroExitCode_IsExecutionFailedDespiteValidParse()
    {
        ShellOpenSeesModel model = ShellModelFixtures.Q4Elastic();
        using var temp = new TempRoot("opencs-audit-runner-exitcode");
        var runner = new ShellAnalysisRunner(
            new ShellTclGenerator(),
            new OpenSeesArtifactStore(temp.Root),
            new WriteFixtureProcessRunner(model, converged: true) { ExitCode = 1 },
            new ShellResultParser());

        ShellAnalysisRunResult result = await runner.RunAsync(model, @"C:\fake\OpenSees.exe", CancellationToken.None);

        Assert.Equal(ShellAnalysisOutcome.ExecutionFailed, result.Outcome);
    }

    private static void WriteFixture(string directory, ShellOpenSeesModel model, bool converged)
    {
        NormalizedShellNode[] nodes = model.Nodes.OrderBy(node => node.Tag).ToArray();
        NormalizedShellElement[] elements = model.Elements.OrderBy(element => element.Tag).ToArray();
        int[] restrained = nodes.Where(node => node.Fixed.Any(fixedDof => fixedDof))
            .Select(node => node.Tag).ToArray();

        File.WriteAllText(Path.Combine(directory, "recorder_order.json"),
            "{\"nodeTags\":[" + string.Join(',', nodes.Select(node => node.Tag)) +
            "],\"restrainedTags\":[" + string.Join(',', restrained) +
            "],\"shellElementTags\":[" + string.Join(',', elements.Select(element => element.Tag)) +
            "],\"nonlinearBeamElementTags\":[],\"sectionForceGroups\":[]}");
        File.WriteAllText(Path.Combine(directory, "step_status.out"),
            converged ? "1 0 1.0 1 0\n" : "1 0 1.0 0 0\n");
        File.WriteAllText(Path.Combine(directory, "shell_node_disp.out"),
            "1.0 " + string.Join(' ', Enumerable.Repeat("0.001", nodes.Length * 6)) + "\n");
        if (restrained.Length > 0)
            File.WriteAllText(Path.Combine(directory, "shell_node_reactions.out"),
                "1.0 " + string.Join(' ', Enumerable.Repeat("100", restrained.Length * 6)) + "\n");
        File.WriteAllText(Path.Combine(directory, "shell_element_forces.out"),
            "1.0 " + string.Join(' ', Enumerable.Repeat("1", elements.Sum(element => element.NodeTags.Count * 6))) + "\n");
        File.WriteAllText(Path.Combine(directory, "completed.marker"), "done\n");
    }

    private sealed class WriteFixtureProcessRunner : IOpenSeesProcessRunner
    {
        private readonly ShellOpenSeesModel _model;
        private readonly bool _converged;

        public WriteFixtureProcessRunner(ShellOpenSeesModel model, bool converged)
        {
            _model = model;
            _converged = converged;
        }

        public int ExitCode { get; init; }

        public Task<OpenSeesRunResult> RunAsync(OpenSeesRunRequest request, CancellationToken cancellationToken)
        {
            WriteFixture(request.WorkingDirectory, _model, _converged);
            return Task.FromResult(new OpenSeesRunResult { ExitCode = ExitCode });
        }
    }

    private sealed class NoOutputProcessRunner : IOpenSeesProcessRunner
    {
        public Task<OpenSeesRunResult> RunAsync(OpenSeesRunRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OpenSeesRunResult { ExitCode = 0 });
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot(string name)
        {
            Root = Path.Combine(Path.GetTempPath(), name, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
