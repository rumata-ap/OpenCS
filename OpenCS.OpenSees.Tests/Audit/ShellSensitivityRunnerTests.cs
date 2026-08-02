using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests.Audit;

public sealed class ShellSensitivityRunnerTests
{
    [Fact]
    public void Evaluate_MetricsWithinTolerance_Passes()
    {
        ShellMeshSensitivityReport report = ShellSensitivityRunner.Evaluate(
            [Case(ShellSensitivityLevel.Coarse, 100.0, "mesh:a"),
             Case(ShellSensitivityLevel.Medium, 100.5, "mesh:b"),
             Case(ShellSensitivityLevel.Fine, 101.0, "mesh:c")],
            relativeTolerance: 0.1);

        Assert.Equal(ShellAuditVerdict.Passed, report.Verdict);
        Assert.True(report.MaxRelativeDeviation <= 0.1);
    }

    [Fact]
    public void Evaluate_DeviationBeyondTolerance_IsMeshDependent()
    {
        ShellMeshSensitivityReport report = ShellSensitivityRunner.Evaluate(
            [Case(ShellSensitivityLevel.Coarse, 100.0, "mesh:a"),
             Case(ShellSensitivityLevel.Fine, 150.0, "mesh:b")],
            relativeTolerance: 0.1);

        Assert.Equal(ShellAuditVerdict.MeshDependent, report.Verdict);
        Assert.Contains(report.Diagnostics, diagnostic => diagnostic.Code == ShellDiagnosticCodes.MeshDependent);
    }

    [Fact]
    public void Evaluate_DuplicateFingerprints_Blocks()
    {
        ShellMeshSensitivityReport report = ShellSensitivityRunner.Evaluate(
            [Case(ShellSensitivityLevel.Coarse, 100.0, "same"),
             Case(ShellSensitivityLevel.Medium, 101.0, "same"),
             Case(ShellSensitivityLevel.Fine, 102.0, "other")],
            relativeTolerance: 0.1);

        Assert.Equal(ShellAuditVerdict.Blocked, report.Verdict);
        Assert.Contains(report.Diagnostics, diagnostic =>
            diagnostic.Code == ShellDiagnosticCodes.SensitivityCaseIncomplete);
    }

    [Fact]
    public void Evaluate_FailedCase_Blocks()
    {
        ShellSensitivityCaseReport failed = new(
            ShellSensitivityLevel.Fine, 0, "mesh:c", ShellAnalysisOutcome.ExecutionFailed);
        ShellMeshSensitivityReport report = ShellSensitivityRunner.Evaluate(
            [Case(ShellSensitivityLevel.Coarse, 100.0, "mesh:a"), failed],
            relativeTolerance: 0.1);

        Assert.Equal(ShellAuditVerdict.Blocked, report.Verdict);
    }

    [Fact]
    public void MetricFor_ExtractsMaxReactionComponent()
    {
        var result = new ShellResult
        {
            Status = "completed",
            Reactions =
            [
                new ShellNodeReaction(1, 0, 0, 1200, 0, 0, 0),
                new ShellNodeReaction(2, 0, 0, -800, 0, 0, 0)
            ]
        };

        Assert.Equal(1200.0, ShellSensitivityRunner.MetricFor(result), 12);
    }

    [Fact]
    public async Task RunAsync_DeterministicFactoryAndFakeRunner_ProducesEvaluatedReport()
    {
        var factory = new FixedCaseFactory(
        [
            new ShellSensitivityCase(ShellSensitivityLevel.Coarse, new ShellOpenSeesModel(), "mesh:a"),
            new ShellSensitivityCase(ShellSensitivityLevel.Medium, new ShellOpenSeesModel(), "mesh:b"),
            new ShellSensitivityCase(ShellSensitivityLevel.Fine, new ShellOpenSeesModel(), "mesh:c")
        ]);
        var runner = new ShellSensitivityRunner(factory, new FakeAnalysisRunner(
            CompletedWithReactions(100.0),
            CompletedWithReactions(150.0),
            CompletedWithReactions(200.0)));
        var policy = new ShellAuditPolicy { SensitivityRelativeTolerance = 0.1 };

        ShellMeshSensitivityReport report = await runner.RunAsync(policy, @"C:\fake\OpenSees.exe", CancellationToken.None);

        Assert.Equal(3, report.Cases.Count);
        Assert.Equal(ShellAuditVerdict.MeshDependent, report.Verdict);
        Assert.All(report.Cases, sensitivityCase =>
            Assert.Equal(ShellAnalysisOutcome.Completed, sensitivityCase.Outcome));
    }

    private static ShellSensitivityCaseReport Case(
        ShellSensitivityLevel level, double metric, string fingerprint) =>
        new(level, metric, fingerprint, ShellAnalysisOutcome.Completed);

    private static ShellAnalysisRunResult CompletedWithReactions(double fz) =>
        new(ShellAnalysisOutcome.Completed,
            new ShellResult
            {
                Status = "completed",
                Reactions = [new ShellNodeReaction(1, 0, 0, fz, 0, 0, 0)]
            },
            null, null);

    private sealed class FixedCaseFactory : IShellSensitivityCaseFactory
    {
        private readonly IReadOnlyList<ShellSensitivityCase> _cases;

        public FixedCaseFactory(IReadOnlyList<ShellSensitivityCase> cases) => _cases = cases;

        public IReadOnlyList<ShellSensitivityCase> Create(IReadOnlyList<ShellSensitivityLevel> levels) => _cases;
    }

    private sealed class FakeAnalysisRunner : IShellAnalysisRunner
    {
        private readonly ShellAnalysisRunResult[] _results;
        private int _index;

        public FakeAnalysisRunner(params ShellAnalysisRunResult[] results) => _results = results;

        public Task<ShellAnalysisRunResult> RunAsync(
            ShellOpenSeesModel model, string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult(_results[_index++ % _results.Length]);
    }
}
