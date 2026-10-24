using CScore;
using CScore.LimitSurface;
using OpenCS.LimitSurface;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>VM окна поверхности: смена режима пересчитывает Кисп, ошибки показываются текстом.</summary>
public class LimitSurfaceResultVMTests(LimitSurfaceSceneFixture fx) : IClassFixture<LimitSurfaceSceneFixture>
{
    LimitSurfaceResultVM Ok() => new(new CalcResult { Status = "ok", DataJson = fx.Data.ToJson() });

    [Fact]
    public void ModeChange_RecomputesRatio()
    {
        var vm = Ok();
        Assert.Null(vm.ErrorText);
        double all = vm.Points[0].Ratio;
        vm.Mode = UtilizationMode.Moment;
        Assert.NotEqual(all, vm.Points[0].Ratio);
    }

    [Fact]
    public void ModeChange_KeepsSelectedPoint()
    {
        var vm = Ok();
        vm.SelectedPoint = vm.Points[1];
        vm.Mode = UtilizationMode.Axial;
        Assert.Same(vm.Points[1], vm.SelectedPoint);
    }

    [Fact]
    public void SliceThroughSelected_SetsLevelFromPoint()
    {
        var vm = Ok();
        vm.SelectedPoint = vm.Points[0];
        vm.SliceKind = SliceKind.AtN;
        vm.SliceThroughSelected();
        Assert.Equal(vm.Points[0].Source.N, vm.SliceLevel);
        vm.SliceFamilyCount = 5;
        Assert.Equal(6, vm.SliceLevels.Count);       // уровень + 5 равных шагов
        Assert.Equal(vm.SliceLevel, vm.SliceLevels[0]);
        vm.SliceFamilyCount = 50;
        Assert.Equal(9, vm.SliceFamilyCount);
    }

    [Fact]
    public void SliceKindChange_ResetsLevelIntoRange()
    {
        var vm = Ok();
        vm.SliceKind = SliceKind.AtMx;
        var (lo, hi) = vm.Scene!.LevelRange(SliceKind.AtMx);
        Assert.InRange(vm.SliceLevel, lo, hi);
    }

    [Fact]
    public void ErrorResult_ShowsMessage()
    {
        var vm = new LimitSurfaceResultVM(new CalcResult { Status = "error", DataJson = """{"error":"нет бетона"}""" });
        Assert.Equal("нет бетона", vm.ErrorText);
        Assert.Null(vm.Scene);
    }

    [Fact]
    public void OldFormat_AsksToRecalculate()
    {
        var vm = new LimitSurfaceResultVM(new CalcResult { Status = "ok", DataJson = """{"FormatVersion":0}""" });
        Assert.NotNull(vm.ErrorText);
        Assert.Null(vm.Scene);
    }
}
