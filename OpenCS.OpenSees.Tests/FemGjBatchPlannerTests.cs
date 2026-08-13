using CScore;
using CScore.Fem;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.OpenSees.Tests;

public sealed class FemGjBatchPlannerTests
{
    [Fact]
    public void MissingOnlyAssignsInvalidManualBeamsAndSkipsSaintVenantAndShells()
    {
        var missing = new FemMember { ElemTag = "1", ElemType = "beam", GjStrategy = "manual", GjManualValue = null };
        var zero = new FemMember { ElemTag = "2", ElemType = "beam", GjStrategy = "manual", GjManualValue = 0 };
        var nan = new FemMember { ElemTag = "3", ElemType = "beam", GjStrategy = "manual", GjManualValue = double.NaN };
        var valid = new FemMember { ElemTag = "4", ElemType = "beam", GjStrategy = "manual", GjManualValue = 10 };
        var saintVenant = new FemMember { ElemTag = "5", ElemType = "beam", GjStrategy = "saint_venant", GjTorsionTaskId = 7 };
        var shell = new FemMember { ElemTag = "6", ElemType = "shell", GjStrategy = "manual", GjManualValue = null };
        var resolver = new FemGjDefaultResolver(() => new CalcSettings { OpenSeesAutoGjFromSection = false });
        var planner = new FemGjBatchPlanner(resolver);

        var plan = planner.Build(
            [missing, zero, nan, valid, saintVenant, shell],
            new Dictionary<int, CrossSection>(),
            FemGjBatchMode.MissingOnly);

        Assert.Equal(3, plan.Assigned);
        Assert.Equal(0, plan.Fallback);
        Assert.Equal(1, plan.SkippedSaintVenant);
        Assert.Equal(0, plan.SkippedNoSection);
        Assert.Equal([missing, zero, nan], plan.Assignments.Select(a => a.Member));
        Assert.All(plan.Assignments, assignment =>
        {
            Assert.Equal("manual", assignment.Strategy);
            Assert.Equal(1e10, assignment.ManualValue);
            Assert.Null(assignment.TorsionTaskId);
        });
        Assert.Equal(7, saintVenant.GjTorsionTaskId);
    }

    [Fact]
    public void RecalculateManualOnlyProcessesMembersWithSections()
    {
        var section = new CrossSection { Id = 10 };
        var withSection = new FemMember
        {
            ElemTag = "1", ElemType = "beam", GjStrategy = "manual",
            GjManualValue = 10, CrossSectionId = section.Id
        };
        var withoutSection = new FemMember
        {
            ElemTag = "2", ElemType = "beam", GjStrategy = "manual", GjManualValue = 10
        };
        var saintVenant = new FemMember
        {
            ElemTag = "3", ElemType = "beam", GjStrategy = "saint_venant",
            GjTorsionTaskId = 9, CrossSectionId = section.Id
        };
        var valid = new FemMember
        {
            ElemTag = "4", ElemType = "beam", GjStrategy = "manual",
            GjManualValue = 10, CrossSectionId = section.Id
        };
        var resolver = new FemGjDefaultResolver(
            () => new CalcSettings { OpenSeesAutoGjFromSection = false, OpenSeesDefaultGjKnm2 = 321 });
        var planner = new FemGjBatchPlanner(resolver);

        var plan = planner.Build(
            [withSection, withoutSection, saintVenant, valid],
            new Dictionary<int, CrossSection> { [section.Id] = section },
            FemGjBatchMode.RecalculateManual);

        Assert.Equal(2, plan.Assigned);
        Assert.Equal(0, plan.Fallback);
        Assert.Equal(1, plan.SkippedSaintVenant);
        Assert.Equal(1, plan.SkippedNoSection);
        Assert.Equal([withSection, valid], plan.Assignments.Select(a => a.Member));
        Assert.All(plan.Assignments, assignment => Assert.Equal(321000, assignment.ManualValue));
        Assert.Equal(9, saintVenant.GjTorsionTaskId);
    }
}
