using CScore;
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
/// Эталоны нелинейного расчёта материализованной субмодели на живом OpenSees (opt-in): линейный родитель
/// «колонна–ригель–колонна» → извлечение ригеля «13» (силовые концы, gauge — 6 DOF начального узла) →
/// нелинейный расчёт ребёнка с физически нелинейным железобетонным сечением → сверка по шагам.
/// </summary>
public sealed class StraightBeamSubmodelNonlinearIntegrationTests(ITestOutputHelper output)
{
    sealed record Run(SubmodelNonlinearVerificationReport Report, NonlinearStatesAdapterOutcome States, FemNonlinearResult Result);

    [Fact]
    public async Task LinearTransf_EndForcesMatchParent_DisplacementsDiffer()
    {
        // При силовых концах цепочка статически определима относительно жёстких мод: концевые усилия
        // задаются нагрузками и не зависят от жёсткости — совпадают с родителем при любом законе материала.
        string executable = OpenSeesTestExecutable.ResolveOrSkip();

        var run = await RunChild(executable, "Linear", loadScale: 1);

        var report = run.Report;
        Log(report);
        Assert.True(report.LambdaReached, string.Join(" | ", run.Result.Diagnostics));
        Assert.False(report.MemberLoadsLumped);
        // Допуск 1e-5: recorder нелинейного генератора пишет 6 значащих цифр.
        Assert.True(report.AtReached.EndForce.Value <= Math.Max(1e-5 * report.AtReached.EndForce.Scale, 1e-6),
            $"концевые силы: {report.AtReached.EndForce.Value} при масштабе {report.AtReached.EndForce.Scale}");
        Assert.True(report.AtReached.EndMoment.Value <= Math.Max(1e-5 * report.AtReached.EndMoment.Scale, 1e-6),
            $"концевые моменты: {report.AtReached.EndMoment.Value} при масштабе {report.AtReached.EndMoment.Scale}");
        Assert.True(report.MaxGaugeForce!.MaxForce <= Math.Max(1e-5 * report.AtReached.EndForce.Scale, 1e-6),
            $"невязка gauge (силы): {report.MaxGaugeForce.MaxForce}");
        Assert.True(report.MaxGaugeMoment!.MaxMoment <= Math.Max(1e-5 * report.AtReached.EndMoment.Scale, 1e-6),
            $"невязка gauge (моменты): {report.MaxGaugeMoment.MaxMoment}");
        // Сечение треснуло: перемещения нелинейного ребёнка заметно отличаются от линейного родителя.
        Assert.True(report.AtReached.Translation.Value > 1e-3 * report.AtReached.Translation.Scale,
            $"перемещения почти совпали ({report.AtReached.Translation.Value}) — нелинейность не проявилась");
    }

    [Fact]
    public async Task Corotational_RunsWithLumpedLoadsAndWarns()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();

        var run = await RunChild(executable, "Corotational", loadScale: 1);

        Log(run.Report);
        Assert.True(run.Report.LambdaReached, string.Join(" | ", run.Result.Diagnostics));
        Assert.True(run.States.MemberLoadsLumped);
        Assert.Contains(run.Result.Diagnostics, d => d == FemElementForceCorrection.LumpedLoadsDiagnostic);
        Assert.Contains(run.Report.Diagnostics, d => d.Code == SubmodelNonlinearDiagnostics.MemberLoadsApproximated && !d.IsError);
    }

    [Fact]
    public async Task Overload_StopsBeforeLambdaOneAndReports()
    {
        // k по формуле: расчётный максимум момента в КЭ 13 (концевые моменты родителя + qL²/8) при
        // λ = 1 втрое превышает оценку предельного момента сечения. Оценка максимума консервативна, а
        // сечение держит больше оценки (сжатая арматура), поэтому исчерпание наступает не при λ ≈ 1/3,
        // а около 0,55–0,6 (при множителе 2 было λ_last = 0,85 — слишком близко к границе теста).
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        var parent = FemLinearResultParentAdapter.FromLinearResult(
            await StraightBeamSubmodelMaterializationIntegrationTests.RunParent(executable, PortalFrameReference.Model()));
        Assert.True(parent.TryGetEndForces("13", out var f));
        double mMax = new[] { f.I.Ry, f.I.Rz, f.J.Ry, f.J.Rz }.Max(Math.Abs) + Math.Abs(PortalFrameReference.Q) * 2 * 2 / 8;
        double k = 3 * SubmodelRcSectionFixture.UltimateMomentEstimate / mMax;

        var run = await RunChild(executable, "Linear", loadScale: k);

        output.WriteLine($"M_max = {mMax:G6} Н·м, M_ult ≈ {SubmodelRcSectionFixture.UltimateMomentEstimate:G6} Н·м, k = {k:G4}, " +
                         $"λ_last = {run.Report.ReachedLambda:G4}, статус {run.Result.Status}");
        Assert.True(run.States.Steps!.Count >= 2);
        Assert.InRange(run.Report.ReachedLambda, 0.2, 0.9);
        Assert.False(run.Report.LambdaReached);
        Assert.Contains(run.Report.Diagnostics, d => d.Code == SubmodelNonlinearDiagnostics.LambdaNotReached && !d.IsError);
    }

    /// <summary>
    /// Родитель → извлечение «13» → сценарий → план → нелинейный расчёт ребёнка через резолвер. При
    /// <paramref name="loadScale"/> ≠ 1 все нагрузки плана (включая заданные перемещения gauge)
    /// умножаются на k — это то же, что линейный родитель с нагрузками × k; сверка тогда идёт с
    /// неумноженным родителем, и её числа не проверяются (тест смотрит только на λ).
    /// </summary>
    async Task<Run> RunChild(string executable, string geomTransf, double loadScale)
    {
        var parentModel = PortalFrameReference.Model();
        var parent = FemLinearResultParentAdapter.FromLinearResult(
            await StraightBeamSubmodelMaterializationIntegrationTests.RunParent(executable, parentModel));
        var (extraction, meshNodes, meshElements) = PortalFrameReference.ExtractWithMesh(parentModel, ["13"]);
        var scenario = PortalFrameReference.Build(parentModel, extraction, parent);
        var build = SubmodelMaterializationPlanner.Plan(new(extraction, scenario, meshNodes, meshElements, parentModel.Nodes));
        Assert.True(build.IsSuccess, string.Join(" | ", build.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        var plan = build.Plan!;
        Assert.Equal(6, plan.Summary.GaugeDofs.Count);

        var stage = new FemNonlinearStageInput("λ", Scale(plan.NodeLoads, loadScale), LoadFactorStep: 0.05, MaxLoadFactor: 1.0)
        {
            MemberLoads = Scale(plan.MemberLoads, loadScale),
            KinematicLoads = plan.KinematicLoads.Select(l => new FemKinematicLoad
            {
                Id = l.Id, LoadCaseId = l.LoadCaseId, NodeId = l.NodeId, Dof = l.Dof, Value = l.Value * loadScale
            }).ToList()
        };
        var (section, materials) = SubmodelRcSectionFixture.Create();
        var input = new FemNonlinearWorkflowInput(plan.MeshNodes, plan.MeshElements, plan.Nodes, plan.Members, [stage],
            new Dictionary<int, CrossSection> { [PortalFrameReference.SectionId] = section }, materials, null, CalcType.C,
            new FemNonlinearAnalysisOptions(geomTransf, 10, 1e-6, 50, 5) { RecordFiberStates = false });

        string root = Path.Combine(Path.GetTempPath(), "opencs-submodel-nonlinear", Guid.NewGuid().ToString("N"));
        try
        {
            var workflow = await new FemNonlinearAnalysisWorkflow(new FemNonlinearAnalysisService(
                    new FemNonlinearTclGenerator(), new OpenSeesProcessRunner(), new OpenSeesArtifactStore(root), new FemNonlinearResultParser()))
                .RunAsync(input, new OpenSeesRunRequest
                {
                    ExecutablePath = executable, WorkingDirectory = Path.GetTempPath(), Timeout = TimeSpan.FromSeconds(120)
                }, CancellationToken.None);
            Assert.True(workflow.Result is not null, $"status={workflow.Status}; {string.Join(" | ", workflow.Errors)}");

            var states = FemNonlinearResultParentAdapter.FromNonlinearResult(workflow.Result!);
            Assert.True(states.Error is null, states.Error?.Message);
            var report = SubmodelNonlinearVerification.Compare(extraction, scenario, plan.Summary, parent, states.Steps!,
                states.MemberLoadsLumped);
            return new Run(report, states, workflow.Result!);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static List<FemNodeLoad> Scale(IEnumerable<FemNodeLoad> loads, double k) => loads.Select(l => new FemNodeLoad
    {
        Id = l.Id, LoadCaseId = l.LoadCaseId, NodeId = l.NodeId,
        Fx = k * l.Fx, Fy = k * l.Fy, Fz = k * l.Fz, Mx = k * l.Mx, My = k * l.My, Mz = k * l.Mz
    }).ToList();

    static List<FemMemberLoad> Scale(IEnumerable<FemMemberLoad> loads, double k) => loads.Select(l => new FemMemberLoad
    {
        Id = l.Id, LoadCaseId = l.LoadCaseId, MemberId = l.MemberId, CoordinateSystem = l.CoordinateSystem,
        DistributionType = l.DistributionType, StartOffsetM = l.StartOffsetM, EndOffsetM = l.EndOffsetM,
        QxStart = k * l.QxStart, QyStart = k * l.QyStart, QzStart = k * l.QzStart,
        QxEnd = k * l.QxEnd, QyEnd = k * l.QyEnd, QzEnd = k * l.QzEnd
    }).ToList();

    void Log(SubmodelNonlinearVerificationReport report)
    {
        var r = report.AtReached;
        output.WriteLine($"λ = {report.ReachedLambda:G6}, шагов {report.GaugeHistory.Count}");
        foreach (var (name, d) in new[] { ("перемещения", r.Translation), ("повороты", r.Rotation), ("концевые силы", r.EndForce),
                     ("концевые моменты", r.EndMoment), ("реакции-силы", r.ReactionForce), ("реакции-моменты", r.ReactionMoment) })
            output.WriteLine($"{name}: {d.Value:G6} (масштаб {d.Scale:G6}, отн. {(d.Scale > 0 ? d.Value / d.Scale : 0):G3}) в {d.Tag} DOF {d.Dof + 1}");
        output.WriteLine($"max невязка gauge: силы {report.MaxGaugeForce?.MaxForce:G4}, моменты {report.MaxGaugeMoment?.MaxMoment:G4}");
        foreach (var d in report.Diagnostics) output.WriteLine($"[{d.Code}] {d.Message}");
    }
}
