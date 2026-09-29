using CScore.Submodel;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

public sealed class SubmodelReportRowsTests
{
    static string N(double v) => SubmodelReportRows.Number(v);

    [Fact]
    public void FromReport_ConvertsUnitsAndRelativeDeviation()
    {
        var report = new SubmodelVerificationReport(
            Translation: new(0.002, 0.01, "3", 2),
            Rotation: new(1e-4, 0.002, "4", 4),
            EndForce: new(500, 20000, "13", 0),
            EndMoment: new(30, 0, "12", 5),
            ReactionForce: MaxDeviation.None,
            ReactionMoment: new(10, 1000, "2", 3),
            Passed: true, Diagnostics: []);

        var rows = SubmodelReportRows.FromReport(report);

        Assert.Equal(6, rows.Count);
        Assert.Equal(new DeviationRow("SubmodelKindTranslation", N(2), "SubmodelUnitMm", N(10), N(0.2), "3", "Uz"), rows[0]);
        Assert.Equal(new DeviationRow("SubmodelKindRotation", N(1e-4), "SubmodelUnitRad", N(0.002), N(0.05), "4", "Ry"), rows[1]);
        Assert.Equal(new DeviationRow("SubmodelKindEndForce", N(0.5), "SubmodelUnitKn", N(20), N(0.025), "13", "Fx"), rows[2]);
        Assert.Equal(SubmodelReportRows.Dash, rows[3].Relative);
        Assert.Equal("Mz", rows[3].Dof);
        Assert.Equal(new DeviationRow("SubmodelKindReactionForce", "SubmodelNoData", "SubmodelUnitKn",
            "SubmodelNoData", "SubmodelNoData", "SubmodelNoData", "SubmodelNoData"), rows[4]);
        Assert.Equal("Mx", rows[5].Dof);
        Assert.Equal(N(0.01), rows[5].Value);
    }

    static ScenarioEnd End(bool atStart, params DofAssignment[] dofs) =>
        new(atStart, atStart ? "2" : "5", atStart ? "2" : "3", dofs, null, [], null, null,
            new ControlCheck(null, 1500, 20, true, true));

    [Fact]
    public void FromScenario_TwelveRowsWithUnitsAndOverrideChoice()
    {
        var start = End(true,
            new(DofMode.Force, 1000, DofSource.Auto), new(DofMode.Fixed, null, DofSource.Auto),
            new(DofMode.Kinematic, 0.003, DofSource.Override), new(DofMode.Force, 2000, DofSource.Auto),
            new(DofMode.Kinematic, 0.01, DofSource.Auto), new(DofMode.Fixed, null, DofSource.Override));
        var end = End(false, Enumerable.Repeat(new DofAssignment(DofMode.Force, 0, DofSource.Auto), 6).ToArray());
        var scenario = new BoundaryScenario(1, 1, ScenarioStatus.Complete, LoadCompleteness.Known, [], [], [], [],
            new LoadAccounting(1, 1, 0), [start, end], []);

        var rows = SubmodelReportRows.FromScenario(scenario);

        Assert.Equal(12, rows.Count);
        Assert.Equal($"{N(1)} SubmodelUnitKn", rows[0].Value);
        Assert.Equal(SubmodelReportRows.Dash, rows[1].Value);
        Assert.Equal($"{N(3)} SubmodelUnitMm", rows[2].Value);
        Assert.Equal($"{N(2)} SubmodelUnitKnm", rows[3].Value);
        Assert.Equal($"{N(0.01)} SubmodelUnitRad", rows[4].Value);
        Assert.Equal(DofOverrideChoice.Kinematic, rows[2].Choice);
        Assert.Equal(DofOverrideChoice.Fixed, rows[5].Choice);
        Assert.Equal("SubmodelDofSourceOverride", rows[2].SourceText);
        Assert.All(rows.Where(r => r.Source == DofSource.Auto), r => Assert.Equal(DofOverrideChoice.Auto, r.Choice));
        Assert.Equal(["Ux", "Uy", "Uz", "Rx", "Ry", "Rz"], rows.Take(6).Select(r => r.Dof));
        Assert.All(rows.Skip(6), r => Assert.Equal("SubmodelEndEnd", r.End));

        var overrides = SubmodelReportRows.Overrides(rows);
        Assert.Equal([new DofOverride(true, 2, DofMode.Kinematic), new DofOverride(true, 5, DofMode.Fixed)], overrides);
        rows[7].Choice = DofOverrideChoice.Kinematic;
        Assert.Contains(new DofOverride(false, 1, DofMode.Kinematic), SubmodelReportRows.Overrides(rows));
    }

    [Fact]
    public void GaugeAndControlTexts()
    {
        var summary = new SubmodelMaterializationSummary(1, 4, [new GaugeDof(true, "2", 2, 0), new GaugeDof(false, "5", 5, 0)], 4, 3, 2, 3, 0, []);

        Assert.Equal(2, SubmodelReportRows.GaugeDofs(summary).Count);
        Assert.Equal("SubmodelControlUnavailable", SubmodelReportRows.ControlText(new ControlCheck(null, 0, 0, false, false)));
        Assert.Null(SubmodelReportRows.GaugeResidual(new GaugeResidualStep(1, 0.5, null, 3), force: true));
        Assert.NotNull(SubmodelReportRows.GaugeResidual(new GaugeResidualStep(1, 0.5, null, 3), force: false));
        Assert.Null(SubmodelReportRows.GaugeResidual(null, force: false));
    }
}
