using CScore;
using CScore.History;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

public sealed class LoadHistoryParamsVMTests
{
    static StagedSection Staged(int parts) =>
        new() { Parts = Enumerable.Range(0, parts).Select(i => new SectionPart { Index = i, Tag = $"ч{i}" }).ToList() };

    [Fact]
    public void Load_Build_RoundTrip()
    {
        var vm = new LoadHistoryParamsVM([]);
        vm.SetSection(Staged(2));
        var p = new LoadHistoryParams
        {
            Stages =
            [
                new() { Tag = "1", Steps = 4, Force = new StageForce { Mode = "manual", Mx = -50 } },
                new() { Tag = "2", ActivatePart = 1, Steps = 2, Force = new StageForce { Mode = "manual", Mx = -80 } },
                new() { Tag = "р", Kind = StageKind.Failure, Force = new StageForce { Mode = "manual", Mx = -1 } },
            ],
            Failure = new FailureOptions { Steps = 40, DropRatio = 0.8, CMax = null, SofteningStrain = 0.001 },
            PartRoles = [new() { PartIndex = 1, Concrete = PartLimitRole.Fails, Rebar = PartLimitRole.Limits }],
            Fallback = LoadStepFallback.WithoutConcreteTension,
        };
        vm.Load(p);
        Assert.True(vm.HasFailure);
        Assert.Equal(2, vm.Stages.Count);              // этап разрушения — отдельный блок
        Assert.Equal("", vm.CMax);

        var q = vm.Build(out var errors);
        Assert.Empty(errors);
        Assert.Equal(3, q.Stages.Count);
        Assert.Equal(StageKind.Failure, q.Stages[^1].Kind);
        Assert.Null(q.Failure.CMax);
        Assert.Equal(LoadStepFallback.WithoutConcreteTension, q.Fallback);
        Assert.Equal(PartLimitRole.Fails, q.PartRoles.Single(r => r.PartIndex == 1).Concrete);
        Assert.DoesNotContain(q.PartRoles, r => r.PartIndex == 0); // Limits/Limits не пишется
    }

    [Fact]
    public void PartChoices_OnlyNotYetActivated()
    {
        var vm = new LoadHistoryParamsVM([]);
        vm.SetSection(Staged(3));
        vm.AddStageCommand.Execute(null);
        vm.AddStageCommand.Execute(null);
        vm.Stages[0].ActivatePart = 1;
        Assert.Equal(new int?[] { null, 2 }, vm.Stages[1].PartChoices);
        Assert.Equal(["—", "3. ч2"], vm.Stages[1].PartChoiceItems.Select(i => i.Label)); // номер с 1, как в редакторе
        Assert.Equal(1, vm.Stages[0].ActivatePartChoice);
    }

    [Fact]
    public void Build_BadNumber_ReportsError()
    {
        var vm = new LoadHistoryParamsVM([]);
        vm.SetSection(Staged(1));
        vm.AddStageCommand.Execute(null);
        vm.Stages[0].Mx = "abc";
        vm.Build(out var errors);
        Assert.NotEmpty(errors);
    }
}
