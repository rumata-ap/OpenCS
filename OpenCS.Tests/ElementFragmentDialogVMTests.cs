using System.Globalization;
using CScore.Fem;
using CScore.Fem.Fragment;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Диалог «Фрагмент вокруг элементов…»: перекрытия с/по, «вся высота», d, имя, справка (без БД).</summary>
public sealed class ElementFragmentDialogVMTests
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

    static readonly IReadOnlyList<StoreyFloorLevel> Floors = [Level(0), Level(3.3), Level(6.6), Level(9.9)];

    /// <summary>Колонна C1 z 0…3,3 с сечением 0,6 × 0,4: доступны ±0, +3,3 и сосед выше +6,6; по умолчанию +3,3.</summary>
    static ElementFragmentSelection Column(double d = 1.5, params string[] tags) => new(
        FragmentPrism.Create(2, 3, 1, 0, 0.3 + d, 0.2 + d), [], tags.Length == 0 ? ["C1"] : tags, d,
        Floors, new ElementFragmentHeight([0, 1, 2], 1, 0, 1), 0, 3.3, -1, 11);

    static ElementFragmentSelection NoFloors(double d = 1.5) => new(
        FragmentPrism.Create(0, 0, 1, 0, d, d), [], ["C1"], d, [], new ElementFragmentHeight([], null, null, null), 0, 3, 0, 3);

    static ElementFragmentSelection Refused() => new(null,
        [new FemValidationDiagnostic("fragment_prism_not_vertical", "Элемент B1 — не вертикальная.", true, ["B1"])], ["B1"], 1.5,
        [], new ElementFragmentHeight([], null, null, null), 0, 0, 0, 0);

    static ElementFragmentDialogVM Vm(ElementFragmentSelection s, Func<double, Task<ElementFragmentSelection>>? recompute = null,
        string[]? rejected = null) =>
        new("Музей", s, recompute ?? (d => Task.FromResult(Column(d))), rejected ?? [], Ru);

    [Fact]
    public void Defaults_OneFloor_RequestAndText()
    {
        var vm = Vm(Column());
        Assert.Equal(3, vm.Levels.Count);
        Assert.Same(vm.Levels[1], vm.FromLevel);
        Assert.Same(vm.Levels[1], vm.ToLevel);
        Assert.False(vm.FullHeight);
        Assert.True(vm.CanPickLevels);
        Assert.True(vm.CanBuild);
        Assert.Equal("1,5", vm.OffsetText);
        Assert.Equal(string.Format(Loc.S("ElementFragmentName"), "C1"), vm.Name);
        Assert.Equal(string.Format(Ru, Loc.S("ElementFragmentPrism"), "3,6", "3,4", "0", "2", "3", "1,5"), vm.PrismText);
        Assert.Contains(string.Format(Loc.S("ElementFragmentFloorOne"), "+3,300"), vm.RangeText);
        Assert.Contains(string.Format(Loc.S("ElementFragmentCuts"), "+1,650", "+4,950"), vm.RangeText);

        var r = vm.TryRequest(out var error);
        Assert.Null(error);
        Assert.Equal((1.65, 4.95, vm.Name), (Math.Round(r!.Z1, 6), Math.Round(r.Z2, 6), r.Name));
        Assert.NotNull(r.Prism);
        Assert.Equal((3.3, 3.3, false), (r.PrismInfo!.FloorFromZ!.Value, r.PrismInfo.FloorToZ!.Value, r.PrismInfo.FullHeight));
    }

    /// <summary>d больше 10 м — предупреждение (вероятная опечатка), но сборка разрешена; возврат к малому d его снимает.</summary>
    [Fact]
    public async Task LargeOffset_WarnsButAllowsBuild()
    {
        var vm = Vm(Column());
        Assert.DoesNotContain("10 м", vm.Message);
        vm.OffsetText = "115";
        await vm.ApplyOffsetAsync();
        Assert.Contains(string.Format(Loc.S("ElementFragmentOffsetLarge"), "115", "10"), vm.Message);
        Assert.True(vm.CanBuild);
        Assert.NotNull(vm.TryRequest(out _));
        vm.OffsetText = "1,5";
        await vm.ApplyOffsetAsync();
        Assert.DoesNotContain(string.Format(Loc.S("ElementFragmentOffsetLarge"), "115", "10"), vm.Message);
    }

    [Fact]
    public void FromAboveTo_PullsTo_AndBack()
    {
        var vm = Vm(Column());
        vm.FromLevel = vm.Levels[2];
        Assert.Same(vm.Levels[2], vm.ToLevel);
        vm.ToLevel = vm.Levels[0];
        Assert.Same(vm.Levels[0], vm.FromLevel);
        vm.ToLevel = vm.Levels[2];
        var r = vm.TryRequest(out _);
        Assert.Equal(StoreyFloorLevels.Band(Floors, 0, -1, 11).Z1, r!.Z1, 6);
        Assert.Equal(8.25, r.Z2, 6);
        Assert.Contains(string.Format(Loc.S("ElementFragmentFloorRange"), "±0,000", "+6,600"), vm.RangeText);
    }

    [Fact]
    public void FullHeight_UsesFullFromTo()
    {
        var vm = Vm(Column());
        vm.FullHeight = true;
        Assert.False(vm.CanPickLevels);
        var r = vm.TryRequest(out var error);
        Assert.Null(error);
        Assert.True(r!.PrismInfo!.FullHeight);
        Assert.Equal((0.0, 3.3), (r.PrismInfo.FloorFromZ!.Value, r.PrismInfo.FloorToZ!.Value));
        Assert.Equal(4.95, r.Z2, 6);
        Assert.Contains(string.Format(Loc.S("ElementFragmentFloorRange"), "±0,000", "+3,300"), vm.RangeText);
    }

    [Fact]
    public void NoFloors_OnlyFullHeight()
    {
        var vm = Vm(NoFloors());
        Assert.False(vm.HasLevels);
        Assert.True(vm.FullHeight);
        Assert.False(vm.CanToggleFullHeight);
        vm.FullHeight = false;
        Assert.True(vm.FullHeight);
        Assert.Contains(Loc.S("ElementFragmentNoFloors"), vm.RangeText);
        var r = vm.TryRequest(out _);
        Assert.Equal((-1.0, 4.0), (r!.Z1, r.Z2));
        Assert.True(r.PrismInfo!.FullHeight);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    public async Task InvalidOffset_Error_NoBuild(string text)
    {
        int calls = 0;
        var vm = Vm(Column(), d => { calls++; return Task.FromResult(Column(d)); });
        vm.OffsetText = text;
        await vm.ApplyOffsetAsync();
        Assert.False(vm.CanBuild);
        Assert.Contains(Loc.S("ElementFragmentOffsetInvalid"), vm.Message);
        Assert.Null(vm.TryRequest(out var error));
        Assert.Equal(Loc.S("ElementFragmentOffsetInvalid"), error);
        Assert.Equal(0, calls);

        vm.OffsetText = "1,5";
        await vm.ApplyOffsetAsync();
        Assert.True(vm.CanBuild);
        Assert.Equal("", vm.Message);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task NewOffset_Recomputes_KeepsChoiceByZ()
    {
        double? asked = null;
        var vm = Vm(Column(), d => { asked = d; return Task.FromResult(Column(d)); });
        vm.FromLevel = vm.Levels[0];
        vm.ToLevel = vm.Levels[2];
        vm.OffsetText = "2";
        await vm.ApplyOffsetAsync();
        Assert.Equal(2, asked);
        Assert.False(vm.IsBusy);
        Assert.Equal((0.0, 6.6), (vm.FromLevel!.Level.Z, vm.ToLevel!.Level.Z));
        Assert.Equal(string.Format(Ru, Loc.S("ElementFragmentPrism"), "4", "3,8", "0", "2", "3", "2"), vm.PrismText);
        Assert.Equal(2, vm.TryRequest(out _)!.PrismInfo!.OffsetM);
    }

    [Fact]
    public async Task StaleRecompute_Ignored()
    {
        var first = new TaskCompletionSource<ElementFragmentSelection>();
        var vm = Vm(Column(), d => d == 2 ? first.Task : Task.FromResult(Column(d)));
        vm.OffsetText = "2";
        var pending = vm.ApplyOffsetAsync();
        Assert.True(vm.IsBusy);
        Assert.False(vm.CanBuild);
        vm.OffsetText = "3";
        await vm.ApplyOffsetAsync();
        first.SetResult(Column(2));
        await pending;
        Assert.False(vm.IsBusy);
        Assert.Equal(3, vm.TryRequest(out _)!.PrismInfo!.OffsetM);
    }

    [Fact]
    public void Refused_MessageAndNoRequest()
    {
        var vm = Vm(Refused());
        Assert.False(vm.CanBuild);
        Assert.Contains("B1", vm.Message);
        Assert.Equal("", vm.PrismText);
        Assert.Null(vm.TryRequest(out var error));
        Assert.Contains("B1", error);
    }

    [Fact]
    public void Name_Default_MoreAndManual()
    {
        var vm = Vm(Column(1.5, "C1", "C2", "W3"), rejected: ["S1"]);
        Assert.Equal(string.Format(Loc.S("ElementFragmentNameMore"), "C1", 2), vm.Name);
        Assert.Equal("C1, C2, W3", vm.MemberTagsText);
        Assert.Contains(string.Format(Loc.S("ElementFragmentRejected"), "S1"), vm.Message);
        vm.Name = "Мой";
        vm.FullHeight = true;
        Assert.Equal("Мой", vm.Name);
        vm.Name = "";
        Assert.Null(vm.TryRequest(out var error));
        Assert.Equal(Loc.S("StoreyFragmentNameEmpty"), error);
    }

    [Fact]
    public void TagList_TruncatesAfterTen()
    {
        var tags = Enumerable.Range(1, 12).Select(i => $"C{i}").ToList();
        Assert.Equal(string.Format(Loc.S("ElementFragmentTagsMore"), string.Join(", ", tags.Take(10)), 2),
            ElementFragmentDialogVM.TagList(tags));
        Assert.Equal("C1, C2", ElementFragmentDialogVM.TagList(["C1", "C2"]));
    }
}
