using CScore;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Справочник материалов: тип конструкции «бетонная» вводит γb2 = 0,9 в любом порядке выбора.</summary>
public sealed class DataSourceVMTests
{
    static (MaterialVM Mvm, DataSourceVM Ds) Open()
    {
        var mvm = new MaterialVM { Material = new Material(0) };
        var ds = new DataSourceVM { Material = mvm };
        ds.Select();
        return (mvm, ds);
    }

    [Fact]
    public void PlainConstruction_ThenClass_AppliesGb2()
    {
        var (mvm, ds) = Open();
        ds.IsRC = 1;
        ds.SelectedMaterial = ds.N.First(r => r.Tag == "B30");
        Assert.Contains("γb2 = 0,9", mvm.Material.Description);
        Assert.Equal(MatType.Concrete, mvm.Material.Type);
    }

    [Fact]
    public void Class_ThenPlainConstruction_RecomputesGb2()
    {
        var (mvm, ds) = Open();
        ds.SelectedMaterial = ds.N.First(r => r.Tag == "B30");
        double fc = mvm.Material.C!.Fc;
        ds.IsRC = 1;
        Assert.Contains("γb2 = 0,9", mvm.Material.Description);
        Assert.Equal(fc * 0.9, mvm.Material.C!.Fc, 6);
    }

    [Fact]
    public void RebarTypes_OnlySteel()
    {
        var (_, ds) = Open();
        Assert.Single(ds.RfSteelTypes);
    }
}
