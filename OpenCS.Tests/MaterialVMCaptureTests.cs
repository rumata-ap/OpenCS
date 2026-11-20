using CScore;
using OpenCS.Services;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Отмена выбора в справочнике материалов возвращает материал как был.</summary>
public sealed class MaterialVMCaptureTests
{
    [Fact]
    public void CaptureState_RestoresTagTypeAndChars()
    {
        var vm = new MaterialVM { Material = MaterialCatalog.CreateHeavyConcrete("B25")! };
        double fcBefore = vm.Material.C!.Fc;
        var restore = vm.CaptureState();

        var other = MaterialCatalog.CreateRebar("A500C")!;
        vm.Material.Tag = other.Tag;
        vm.Material.Type = other.Type;
        vm.Material.Description = other.Description;
        vm.Material.C = other.C;
        vm.Material.E = other.E;

        restore();

        Assert.Equal("B25", vm.Material.Tag);
        Assert.Equal(MatType.Concrete, vm.Material.Type);
        Assert.Equal("Бетон тяжелый по СП 63.13330", vm.Material.Description);
        Assert.Equal(fcBefore, vm.Material.C!.Fc, 6);
        Assert.Equal(vm.Material.N!.E, vm.Material.E, 6);
    }
}
