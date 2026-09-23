using CScore.Fem;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Submodel;

public sealed class SubmodelNonlinearVerificationTests
{
    sealed record Case(SubmodelExtraction Extraction, BoundaryScenario Scenario, SubmodelMaterializationSummary Summary,
        IParentLinearResult Parent);

    /// <summary>Рама, выборка «13»: оба конца силовые, gauge — 6 DOF начального конца.</summary>
    static Case Portal()
    {
        var parent = PortalFrameReference.Model();
        var (extraction, nodes, elements) = PortalFrameReference.ExtractWithMesh(parent, ["13"]);
        var result = PortalFrameReference.FixtureResult();
        var scenario = PortalFrameReference.Build(parent, extraction, result);
        var build = SubmodelMaterializationPlanner.Plan(new(extraction, scenario, nodes, elements, parent.Nodes));
        Assert.True(build.IsSuccess);
        return new Case(extraction, scenario, build.Plan!.Summary, result);
    }

    /// <summary>Идеальные реакции во всех DOF начального конца: p_control − p_boundary (все DOF силовые).</summary>
    static Dictionary<string, Dof6> IdealReactions(Case c)
    {
        var start = c.Scenario.Ends.Single(e => e.AtStart);
        var values = Enumerable.Range(0, 6).Select(d => start.Control.PControl![d] - (start.Dofs[d].Value ?? 0)).ToArray();
        return new() { [start.ChildNodeTag] = new Dof6(values[0], values[1], values[2], values[3], values[4], values[5]) };
    }

    static SubmodelStepState Step(Case c, int index, double lambda, Dictionary<string, Dof6>? reactions = null) =>
        new(index, lambda, SubmodelLinearVerificationTests.ScaledChild(c.Parent, c.Extraction, reactions ?? IdealReactions(c), lambda));

    static SubmodelNonlinearVerificationReport Compare(Case c, IReadOnlyList<SubmodelStepState> steps,
        bool lumped = false, SubmodelMaterializationSummary? summary = null) =>
        SubmodelNonlinearVerification.Compare(c.Extraction, c.Scenario, summary ?? c.Summary, c.Parent, steps, lumped);

    [Fact]
    public void LambdaReached_ZeroDeviationsAndNoWarnings()
    {
        var c = Portal();

        var report = Compare(c, [Step(c, 1, 0.5), Step(c, 2, 1.0)]);

        Assert.True(report.LambdaReached);
        Assert.Equal(1.0, report.ReachedLambda);
        Assert.False(report.MemberLoadsLumped);
        Assert.Equal(0, report.AtReached.Translation.Value);
        Assert.Equal(0, report.AtReached.EndMoment.Value);
        Assert.DoesNotContain(report.Diagnostics, d => d.Code == SubmodelNonlinearDiagnostics.LambdaNotReached);
        Assert.DoesNotContain(report.Diagnostics, d => d.Code == SubmodelNonlinearDiagnostics.MemberLoadsApproximated);
        Assert.Equal(2, report.GaugeHistory.Count);
    }

    [Fact]
    public void LambdaNotReached_WarnsAndComparesAgainstScaledParent()
    {
        var c = Portal();

        var report = Compare(c, [Step(c, 1, 0.25), Step(c, 2, 0.4)]);

        Assert.False(report.LambdaReached);
        Assert.Equal(0.4, report.ReachedLambda);
        var warning = Assert.Single(report.Diagnostics, d => d.Code == SubmodelNonlinearDiagnostics.LambdaNotReached);
        Assert.False(warning.IsError);
        Assert.Equal(0, report.AtReached.Translation.Value);
        Assert.Equal(0, report.AtReached.EndForce.Value);
    }

    [Fact]
    public void MemberLoadsLumped_Warns()
    {
        var c = Portal();

        var report = Compare(c, [Step(c, 1, 1.0)], lumped: true);

        Assert.True(report.MemberLoadsLumped);
        var warning = Assert.Single(report.Diagnostics, d => d.Code == SubmodelNonlinearDiagnostics.MemberLoadsApproximated);
        Assert.False(warning.IsError);
    }

    [Fact]
    public void NonZeroGaugeResidualOnOneStep_EntersHistoryAndMaximum()
    {
        var c = Portal();
        var reactions = IdealReactions(c);
        string tag = reactions.Keys.Single();
        var disturbed = new Dictionary<string, Dof6> { [tag] = reactions[tag] + new Dof6(0, 0, 800, 0, 300, 0) };

        // Возмущение задаётся в «шкале λ = 1» и масштабируется вместе с шагом: при λ = 0,5 невязка 400 и 150.
        var report = Compare(c, [Step(c, 1, 1.0 / 3), Step(c, 2, 0.5, disturbed), Step(c, 3, 1.0)]);

        Assert.Equal(3, report.GaugeHistory.Count);
        Assert.Equal(400, report.GaugeHistory[1].MaxForce!.Value, 6);
        Assert.Equal(150, report.GaugeHistory[1].MaxMoment!.Value, 6);
        Assert.True(report.GaugeHistory[0].MaxForce <= 1e-6);
        Assert.Equal(2, report.MaxGaugeForce!.StepIndex);
        Assert.Equal(2, report.MaxGaugeMoment!.StepIndex);
    }

    [Fact]
    public void OnlyTranslationalGauge_MomentMaximaAreNull()
    {
        var c = Portal();
        var summary = c.Summary with { GaugeDofs = c.Summary.GaugeDofs.Where(g => g.Dof < 3).ToList() };

        var report = Compare(c, [Step(c, 1, 0.5), Step(c, 2, 1.0)], summary: summary);

        Assert.All(report.GaugeHistory, h => { Assert.NotNull(h.MaxForce); Assert.Null(h.MaxMoment); });
        Assert.NotNull(report.MaxGaugeForce);
        Assert.Null(report.MaxGaugeMoment);
    }

    [Fact]
    public void OnlyRotationalGauge_ForceMaximaAreNull()
    {
        var c = Portal();
        var summary = c.Summary with { GaugeDofs = c.Summary.GaugeDofs.Where(g => g.Dof >= 3).ToList() };

        var report = Compare(c, [Step(c, 1, 1.0)], summary: summary);

        Assert.All(report.GaugeHistory, h => { Assert.Null(h.MaxForce); Assert.NotNull(h.MaxMoment); });
        Assert.Null(report.MaxGaugeForce);
        Assert.NotNull(report.MaxGaugeMoment);
    }

    [Fact]
    public void NoGauge_EmptyHistoryAndNullMaxima()
    {
        var c = Portal();
        var summary = c.Summary with { GaugeDofs = [] };

        var report = Compare(c, [Step(c, 1, 1.0)], summary: summary);

        Assert.Empty(report.GaugeHistory);
        Assert.Null(report.MaxGaugeForce);
        Assert.Null(report.MaxGaugeMoment);
    }

    [Fact]
    public void MismatchAtReachedPoint_IsInformationalNotWarning()
    {
        var c = Portal();
        var parent = c.Parent;
        var shifted = new DictionaryParentLinearResult(false,
            c.Extraction.Nodes.ToDictionary(n => n.SubmodelNodeTag,
                n => { parent.TryGetDisplacement(n.SubmodelNodeTag, out var u); return u + new Dof6(0, 0, 0.01, 0, 0, 0); }),
            IdealReactions(c),
            c.Extraction.Segments.ToDictionary(s => s.SubmodelElementTag,
                s => { parent.TryGetEndForces(s.SubmodelElementTag, out var f); return f; }));

        var report = Compare(c, [new SubmodelStepState(1, 1.0, shifted)]);

        Assert.False(report.AtReached.Passed);
        Assert.DoesNotContain(report.Diagnostics, d => d.Code == SubmodelMaterializationDiagnostics.VerificationMismatch);
        Assert.Contains(report.Diagnostics, d => d.Code == SubmodelNonlinearDiagnostics.Info && d.Message.Contains("перемещения"));
        Assert.DoesNotContain(report.Diagnostics, d => d.IsError);
    }

    [Fact]
    public void EmptySteps_Throws()
    {
        var c = Portal();

        Assert.Throws<ArgumentException>(() => Compare(c, []));
    }
}
