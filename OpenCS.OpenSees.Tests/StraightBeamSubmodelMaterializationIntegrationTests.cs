using CScore.Fem;
using CScore.Submodel;
using CScore.Tests.Submodel;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;
using Xunit.Abstractions;

namespace OpenCS.OpenSees.Tests;

/// <summary>
/// Эталон материализации на живом OpenSees (opt-in): линейный расчёт родителя-рамы → извлечение →
/// граничный сценарий → материализация → линейный расчёт субмодели → сверка. При λ = 1 субмодель
/// должна воспроизвести перемещения и концевые усилия родителя на цепочке, а реакции фиксации
/// жёстких мод — быть нулевыми (невязка равновесия).
/// </summary>
public sealed class StraightBeamSubmodelMaterializationIntegrationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("13")]
    [InlineData("12,13,14")]
    public async Task PortalFrame_MaterializedChain_ReproducesParent(string selection)
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        var parentModel = PortalFrameReference.Model();
        var parent = FemLinearResultParentAdapter.FromLinearResult(await RunParent(executable, parentModel));

        var report = await MaterializeAndVerify(executable, parentModel, parent, selection.Split(','), []);

        Assert.Equal(6, report.Summary.GaugeDofs.Count);
        Log(report.Report);
        AssertPassed(report);
    }

    [Fact]
    public async Task PortalFrame_InvertedBoundaryForces_FailVerification()
    {
        // Отрицательный контроль: знак граничных сил, типичный для ошибки конвенции localForce.
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        var parentModel = PortalFrameReference.Model();
        var parent = FemLinearResultParentAdapter.FromLinearResult(await RunParent(executable, parentModel));

        var report = await MaterializeAndVerify(executable, parentModel, parent, ["13"], [],
            plan => plan with { NodeLoads = plan.NodeLoads.Select(l => new FemNodeLoad
            {
                Id = l.Id, LoadCaseId = l.LoadCaseId, NodeId = l.NodeId,
                Fx = -l.Fx, Fy = -l.Fy, Fz = -l.Fz, Mx = -l.Mx, My = -l.My, Mz = -l.Mz
            }).ToList() });

        Log(report.Report);
        Assert.False(report.Report.Passed);
    }

    [Fact]
    public async Task PortalFrame_KinematicStartEnd_NeedsNoGaugeAndReproducesParent()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        var parentModel = PortalFrameReference.Model();
        var parent = FemLinearResultParentAdapter.FromLinearResult(await RunParent(executable, parentModel));
        var overrides = Enumerable.Range(0, 6).Select(d => new DofOverride(true, d, DofMode.Kinematic)).ToList();

        var report = await MaterializeAndVerify(executable, parentModel, parent, ["13"], overrides);

        Assert.Empty(report.Summary.GaugeDofs);
        Log(report.Report);
        AssertPassed(report);
    }

    sealed record Verified(SubmodelVerificationReport Report, SubmodelMaterializationSummary Summary);

    void Log(SubmodelVerificationReport report)
    {
        foreach (var (name, d) in new[] { ("перемещения", report.Translation), ("повороты", report.Rotation),
                     ("концевые силы", report.EndForce), ("концевые моменты", report.EndMoment),
                     ("реакции-силы", report.ReactionForce), ("реакции-моменты", report.ReactionMoment) })
            output.WriteLine($"{name}: {d.Value:G6} (масштаб {d.Scale:G6}, отн. {(d.Scale > 0 ? d.Value / d.Scale : 0):G3}) в {d.Tag} DOF {d.Dof + 1}");
    }

    static void AssertPassed(Verified verified)
    {
        var report = verified.Report;
        Assert.True(report.Passed, string.Join(" | ", report.Diagnostics.Select(d => d.Message)));
        Assert.DoesNotContain(report.Diagnostics, d => d.Code == SubmodelMaterializationDiagnostics.Info && d.Message.Contains("не сверяется"));
        Assert.True(report.Translation.Scale > 0 && report.EndMoment.Scale > 0);
        // Явная проверка невязки: реакции во всех DOF с sp совпадают с p_control − p_applied.
        Assert.True(report.ReactionForce.Value <= Math.Max(1e-6 * report.ReactionForce.Scale, 1e-6),
            $"Невязка сил в DOF с sp: {report.ReactionForce.Value} Н в узле {report.ReactionForce.Tag}");
        Assert.True(report.ReactionMoment.Value <= Math.Max(1e-6 * report.ReactionMoment.Scale, 1e-6),
            $"Невязка моментов в DOF с sp: {report.ReactionMoment.Value} Н·м в узле {report.ReactionMoment.Tag}");
    }

    static async Task<Verified> MaterializeAndVerify(string executable, ParentModel parentModel, IParentLinearResult parent,
        string[] selection, IReadOnlyList<DofOverride> overrides,
        Func<SubmodelMaterializationPlan, SubmodelMaterializationPlan>? tamper = null)
    {
        var (extraction, meshNodes, meshElements) = PortalFrameReference.ExtractWithMesh(parentModel, selection);
        var scenario = PortalFrameReference.Build(parentModel, extraction, parent, overrides);
        Assert.NotEqual(ScenarioStatus.Blocked, scenario.Status);
        var build = SubmodelMaterializationPlanner.Plan(new(extraction, scenario, meshNodes, meshElements, parentModel.Nodes));
        Assert.True(build.IsSuccess, string.Join(" | ", build.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        var plan = tamper is null ? build.Plan! : tamper(build.Plan!);

        var child = await RunLinear(executable, new FemLinearWorkflowInput(plan.MeshNodes, plan.MeshElements, plan.Nodes,
            plan.Members, plan.NodeLoads, SubmodelMaterializationResolverTests.SectionProps())
        {
            ResolvedMemberLoads = plan.MemberLoads, ResolvedKinematicLoads = plan.KinematicLoads
        });
        var report = SubmodelLinearVerification.Compare(extraction, scenario, plan.Summary, parent,
            FemLinearResultParentAdapter.FromLinearResult(child));
        return new Verified(report, plan.Summary);
    }

    static Task<FemLinearResult> RunParent(string executable, ParentModel parent) =>
        RunLinear(executable, new FemLinearWorkflowInput(parent.MeshNodes, parent.MeshElements, parent.Nodes, parent.Members,
            [.. PortalFrameReference.NodeLoads()], SubmodelMaterializationResolverTests.SectionProps())
        {
            ResolvedMemberLoads = [.. PortalFrameReference.MemberLoads()]
        });

    internal static async Task<FemLinearResult> RunLinear(string executable, FemLinearWorkflowInput input)
    {
        string root = Path.Combine(Path.GetTempPath(), "opencs-submodel-materialization", Guid.NewGuid().ToString("N"));
        try
        {
            var output = await new FemLinearAnalysisWorkflow(new FemLinearAnalysisService(
                    new FemLinearTclGenerator(), new OpenSeesProcessRunner(),
                    new OpenSeesArtifactStore(root), new FemLinearResultParser()))
                .RunAsync(input, new OpenSeesRunRequest
                {
                    ExecutablePath = executable, WorkingDirectory = Path.GetTempPath(), Timeout = TimeSpan.FromSeconds(30)
                }, CancellationToken.None);
            Assert.True(output.Status == "ok", $"status={output.Status}; errors={string.Join(" | ", output.Errors)}");
            return output.Result!;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
