using CScore;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

public sealed class FireForceContractTests
{
    [Theory]
    [InlineData("fire_r_check_batch")]
    [InlineData("fire_thermal_curvature")]
    public void BatchAndCurvature_ResolveWithoutForceItem(string kind)
    {
        var task = new CalcTask { Kind = kind, ForceSetId = 0, ForceItemId = 0 };

        Assert.True(CalcTaskForceHelper.UsesDummyForceItem(task));
        Assert.NotNull(CalcTaskForceHelper.ResolveOptionalForceItem(task, []));
    }

    [Theory]
    [InlineData("fire_r_check")]
    [InlineData("fire_r_time")]
    public void SingleKinds_UseRealForceItem_NotDummy(string kind)
    {
        var task = new CalcTask { Kind = kind, ForceSetId = 1, ForceItemId = 2 };

        Assert.False(CalcTaskForceHelper.UsesDummyForceItem(task));
        Assert.False(CalcTaskForceHelper.UsesManualForces(task));
    }

    [Fact]
    public void BatchKind_WithForceSet_KeepsSetForHandler()
    {
        var set = new ForceSet
        {
            Id = 7,
            Items = { new LoadItem { Id = 3, N = -100, Mx = 50, My = 0 } }
        };
        var task = new CalcTask { Kind = "fire_r_check_batch", ForceSetId = 7, ForceItemId = 0 };

        LoadItem resolved = CalcTaskForceHelper.ResolveOptionalForceItem(task, [set]);

        Assert.Equal(0, resolved.Id);
        Assert.Equal(7, task.ForceSetId);
    }
}
