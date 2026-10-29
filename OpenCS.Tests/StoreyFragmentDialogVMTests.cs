using System.Globalization;
using CScore.Fem.Fragment;
using CScore.Planar;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Диалог «Фрагмент этажа…»: список перекрытий, предвыбор из 3D, автоотметки, ручной режим, имя.</summary>
public sealed class StoreyFragmentDialogVMTests
{
    static readonly CultureInfo Ru = Comma();

    static CultureInfo Comma()
    {
        var c = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        c.NumberFormat.NumberDecimalSeparator = ",";
        return c;
    }

    static StoreyFloorLevel Level(double z) =>
        new(z, 100, ["П"], 0, Enumerable.Range(0, 400).Select(i => (long)i).ToHashSet());

    /// <summary>Фундаментная плита ±0, перекрытия +3,3 и +6,6; сваи до −12, парапет до +7,8.</summary>
    static StoreyFloorContext Context() => new([Level(0), Level(3.3), Level(6.6)], -12, 7.8);

    [Fact]
    public void Picked_SelectsLevel_ComputesBand_Name()
    {
        var vm = new StoreyFragmentDialogVM("Музей", Context(), 3.31, null, Ru);
        Assert.Same(vm.Levels[1], vm.SelectedLevel);
        Assert.False(vm.Manual);
        Assert.Equal("", vm.PickMessage);
        Assert.Equal(("1,65", "4,95"), (vm.Z1Text, vm.Z2Text));
        Assert.Equal(string.Format(Loc.S("StoreyFloorBelow"), "3,3", "+1,650"), vm.BelowText);
        Assert.Equal("Музей · +3,300", vm.Name);
        Assert.Equal(string.Format(Ru, Loc.S("StoreyFloorRow"), "+3,300", "100", string.Format(Loc.S("StoreyFloorMembers"), 1)),
            vm.Levels[1].Label);

        var r = vm.TryRequest(out var error);
        Assert.Null(error);
        Assert.Equal((1.65, 4.95, "Музей · +3,300"), (r!.Z1, r.Z2, r.Name));
    }

    [Fact]
    public void Foundation_BandToSchemaBottom()
    {
        var vm = new StoreyFragmentDialogVM("Музей", Context(), null, null, Ru) { };
        vm.SelectedLevel = vm.Levels[0];
        Assert.Equal(("-13", "1,65"), (vm.Z1Text, vm.Z2Text));
        Assert.Equal(string.Format(Loc.S("StoreyFloorBelowEdge"), "−13,000"), vm.BelowText);
        Assert.Equal("Музей · ±0,000", vm.Name);
    }

    [Fact]
    public void NoSelection_Refused_NotFoundMessage()
    {
        var vm = new StoreyFragmentDialogVM("Музей", Context(), 1.5, null, Ru);
        Assert.Null(vm.SelectedLevel);
        Assert.Equal(string.Format(Loc.S("StoreyFloorPickNotFound"), "+1,500"), vm.PickMessage);
        Assert.Null(vm.TryRequest(out var error));
        Assert.Equal(Loc.S("StoreyFloorNotSelected"), error);

        var tilted = new StoreyFragmentDialogVM("Музей", Context(), null, "StoreyFloorPickTilted", Ru);
        Assert.Equal(Loc.S("StoreyFloorPickTilted"), tilted.PickMessage);
    }

    [Fact]
    public void Manual_KeepsComputedLevels_EditsFollowName_BackToAutoRecomputes()
    {
        var vm = new StoreyFragmentDialogVM("Музей", Context(), 3.3, null, Ru);
        vm.Manual = true;
        Assert.Equal(("1,65", "4,95"), (vm.Z1Text, vm.Z2Text));
        Assert.Equal("", vm.BelowText);
        vm.Z2Text = "5";
        Assert.Equal(StoreyFragmentForm.DefaultName("Музей", 1.65, 5, Ru), vm.Name);
        Assert.Equal(5, vm.TryRequest(out _)!.Z2);

        vm.Manual = false;
        Assert.Equal("4,95", vm.Z2Text);
        vm.Name = "Своё";
        vm.SelectedLevel = vm.Levels[2];
        Assert.Equal("Своё", vm.Name);
    }

    [Fact]
    public void NoLevels_ManualOnly()
    {
        var vm = new StoreyFragmentDialogVM("Музей", new StoreyFloorContext([], 0, 9), 3, null, Ru);
        Assert.False(vm.HasLevels);
        Assert.True(vm.Manual);
        Assert.Equal(Loc.S("StoreyFloorNone"), vm.PickMessage);
        vm.Manual = false;
        Assert.True(vm.Manual);
    }
}
