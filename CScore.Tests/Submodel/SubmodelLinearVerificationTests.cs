using CScore.Fem;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Submodel;

public sealed class SubmodelLinearVerificationTests
{
    sealed record Case(SubmodelExtraction Extraction, BoundaryScenario Scenario, SubmodelMaterializationSummary Summary,
        IParentLinearResult Parent);

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

    /// <summary>Идеальный результат субмодели: перемещения и усилия родителя, реакции = p_control − p_applied.</summary>
    static Dictionary<string, Dof6> Displacements(Case c) =>
        c.Extraction.Nodes.ToDictionary(n => n.SubmodelNodeTag, n => { c.Parent.TryGetDisplacement(n.SubmodelNodeTag, out var u); return u; });

    static Dictionary<string, BeamEndForces> EndForces(Case c) =>
        c.Extraction.Segments.ToDictionary(s => s.SubmodelElementTag, s => { c.Parent.TryGetEndForces(s.SubmodelElementTag, out var f); return f; });

    static Dictionary<string, Dof6> Reactions(Case c)
    {
        var reactions = new Dictionary<string, Dof6>();
        foreach (var end in c.Scenario.Ends)
        {
            var gauge = c.Summary.GaugeDofs.Where(g => g.AtStart == end.AtStart).Select(g => g.Dof).ToHashSet();
            if (gauge.Count == 0) continue;
            var values = Enumerable.Range(0, 6)
                .Select(d => gauge.Contains(d) ? end.Control.PControl![d] - (end.Dofs[d].Value ?? 0) : 0).ToArray();
            reactions[end.ChildNodeTag] = new Dof6(values[0], values[1], values[2], values[3], values[4], values[5]);
        }
        return reactions;
    }

    static IParentLinearResult Child(Case c, Dictionary<string, Dof6>? displacements = null,
        Dictionary<string, Dof6>? reactions = null, Dictionary<string, BeamEndForces>? endForces = null) =>
        new DictionaryParentLinearResult(true, displacements ?? Displacements(c), reactions ?? Reactions(c), endForces ?? EndForces(c));

    [Fact]
    public void IdenticalResults_Pass()
    {
        var c = Portal();

        var report = SubmodelLinearVerification.Compare(c.Extraction, c.Scenario, c.Summary, c.Parent, Child(c));

        Assert.True(report.Passed, string.Join(" | ", report.Diagnostics.Select(d => d.Message)));
        Assert.DoesNotContain(report.Diagnostics, d => d.Code == SubmodelMaterializationDiagnostics.VerificationMismatch);
        Assert.True(report.Translation.Scale > 0);
        Assert.True(report.EndMoment.Scale > 0);
        Assert.True(report.ReactionForce.Scale > 0);
    }

    [Fact]
    public void GaugeReactionsOfIdealChild_AreNearZero()
    {
        // Сам факт: в идеальном ребёнке реакция gauge = p_control − p_boundary ≈ 0 (контроль Среза 3 прошёл).
        var c = Portal();

        Assert.All(Reactions(c).Values, r =>
        {
            Assert.True(r.MaxForceAbs <= 1e-6 * Math.Max(1, c.Scenario.Ends.Max(e => e.Control.PControl!.MaxForceAbs)));
            Assert.True(r.MaxMomentAbs <= 1e-6 * Math.Max(1, c.Scenario.Ends.Max(e => e.Control.PControl!.MaxMomentAbs)));
        });
    }

    [Fact]
    public void ShiftedDisplacement_FailsAtThatNode()
    {
        var c = Portal();
        var displacements = Displacements(c);
        displacements["4"] = displacements["4"] + new Dof6(0, 0, 1e-3, 0, 0, 0);

        var report = SubmodelLinearVerification.Compare(c.Extraction, c.Scenario, c.Summary, c.Parent, Child(c, displacements));

        Assert.False(report.Passed);
        Assert.Equal("4", report.Translation.Tag);
        Assert.Equal(2, report.Translation.Dof);
        var warning = Assert.Single(report.Diagnostics, d => d.Code == SubmodelMaterializationDiagnostics.VerificationMismatch);
        Assert.False(warning.IsError);
    }

    [Fact]
    public void NonZeroGaugeReaction_Fails()
    {
        var c = Portal();
        var reactions = Reactions(c);
        string startTag = c.Scenario.Ends.Single(e => e.AtStart).ChildNodeTag;
        reactions[startTag] = reactions[startTag] + new Dof6(0, 0, 500, 0, 0, 0);

        var report = SubmodelLinearVerification.Compare(c.Extraction, c.Scenario, c.Summary, c.Parent, Child(c, reactions: reactions));

        Assert.False(report.Passed);
        Assert.Equal(startTag, report.ReactionForce.Tag);
        Assert.Equal(500, report.ReactionForce.Value, 6);
    }

    [Fact]
    public void MissingChildNode_IsInformationalNotFailure()
    {
        var c = Portal();
        var displacements = Displacements(c);
        displacements.Remove("4");

        var report = SubmodelLinearVerification.Compare(c.Extraction, c.Scenario, c.Summary, c.Parent, Child(c, displacements));

        Assert.True(report.Passed);
        Assert.Contains(report.Diagnostics, d => d.Code == SubmodelMaterializationDiagnostics.Info && d.Message.Contains("Узел 4"));
    }

    /// <summary>Идеальный ребёнок, масштабированный на λ (перемещения, усилия, реакции).</summary>
    internal static IParentLinearResult ScaledChild(IParentLinearResult parent, SubmodelExtraction extraction,
        IReadOnlyDictionary<string, Dof6> idealReactions, double lambda) =>
        new DictionaryParentLinearResult(false,
            extraction.Nodes.ToDictionary(n => n.SubmodelNodeTag,
                n => { parent.TryGetDisplacement(n.SubmodelNodeTag, out var u); return u.Scale(lambda); }),
            idealReactions.ToDictionary(p => p.Key, p => p.Value.Scale(lambda)),
            extraction.Segments.ToDictionary(s => s.SubmodelElementTag,
                s => { parent.TryGetEndForces(s.SubmodelElementTag, out var f); return new BeamEndForces(f.I.Scale(lambda), f.J.Scale(lambda)); }));

    [Fact]
    public void ScaledChild_MatchesParentScaledByLambda()
    {
        var c = Portal();
        var child = ScaledChild(c.Parent, c.Extraction, Reactions(c), 0.5);

        var report = SubmodelLinearVerification.Compare(c.Extraction, c.Scenario, c.Summary, c.Parent, child, lambda: 0.5);

        Assert.True(report.Passed, string.Join(" | ", report.Diagnostics.Select(d => d.Message)));
        Assert.Equal(0, report.Translation.Value);
        Assert.Equal(0, report.EndMoment.Value);
        Assert.True(report.ReactionForce.Value <= 1e-9);
    }

    [Fact]
    public void ScaledChild_FailsAgainstUnscaledParent()
    {
        var c = Portal();
        var child = ScaledChild(c.Parent, c.Extraction, Reactions(c), 0.5);

        var report = SubmodelLinearVerification.Compare(c.Extraction, c.Scenario, c.Summary, c.Parent, child);

        Assert.False(report.Passed);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidLambda_Throws(double lambda)
    {
        var c = Portal();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SubmodelLinearVerification.Compare(c.Extraction, c.Scenario, c.Summary, c.Parent, Child(c), lambda: lambda));
    }

    [Fact]
    public void EndForceMismatch_FailsOnMoments()
    {
        var c = Portal();
        var forces = EndForces(c);
        var f = forces["13"];
        forces["13"] = new BeamEndForces(f.I, f.J + new Dof6(0, 0, 0, 0, 2000, 0));

        var report = SubmodelLinearVerification.Compare(c.Extraction, c.Scenario, c.Summary, c.Parent, Child(c, endForces: forces));

        Assert.False(report.Passed);
        Assert.Equal("13", report.EndMoment.Tag);
        Assert.Equal(4, report.EndMoment.Dof);
    }
}
