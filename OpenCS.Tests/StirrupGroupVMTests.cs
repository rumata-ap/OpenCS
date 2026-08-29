using CScore;
using OpenCS.Services;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

[Collection("Stirrup group VM")]
/// <summary>ViewModel страницы группы поперечного армирования.</summary>
public sealed class StirrupGroupVMTests(StirrupGroupVMFixture fixture)
{
    [Fact]
    public void AddLoop_AppendsClosedElementWithContribution()
    {
        var vm = NewVM();
        vm.OffsetM = 0.03;
        vm.Diameter = 0.008;

        vm.AddLoopCommand.Execute(null);

        var element = Assert.Single(vm.Elements);
        Assert.True(element.AswVy > 0);
        Assert.True(element.AswVx > 0);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void AddCut_Vertical_ContributesOnlyToVy()
    {
        var vm = NewVM();
        vm.OffsetM = 0.03;
        vm.Diameter = 0.008;
        vm.CutDirection = StirrupCutDirection.Vertical;
        vm.CutPosition = 0.0;

        vm.AddCutCommand.Execute(null);

        var element = Assert.Single(vm.Elements);
        Assert.True(element.AswVy > 0);
        Assert.Equal(0.0, element.AswVx, 12);
    }

    [Fact]
    public void AddLoop_WithExcessiveOffset_SetsErrorAndAddsNothing()
    {
        var vm = NewVM();
        vm.OffsetM = 0.5;

        vm.AddLoopCommand.Execute(null);

        Assert.Empty(vm.Elements);
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public void Duplicate_CreatesRequestedNumberOfCopies()
    {
        var vm = NewVM();
        vm.OffsetM = 0.03;
        vm.Diameter = 0.008;
        vm.AddLoopCommand.Execute(null);
        vm.SelectedElement = vm.Elements[0];
        vm.CopyDx = 0.05;
        vm.CopyCount = 2;

        vm.DuplicateCommand.Execute(null);

        Assert.Equal(3, vm.Elements.Count);
    }

    [Fact]
    public void ChangingGroupOffset_DoesNotMoveExistingElements()
    {
        var vm = NewVM();
        vm.OffsetM = 0.03;
        vm.Diameter = 0.008;
        vm.AddLoopCommand.Execute(null);
        double before = vm.Elements[0].LengthM;

        vm.OffsetM = 0.05;

        Assert.Equal(before, vm.Elements[0].LengthM, 12);
    }

    [Fact]
    public void DiameterMm_ConvertsToMetersForGeometryBuilder()
    {
        var vm = NewVM();
        var property = typeof(StirrupGroupVM).GetProperty("DiameterMm");

        Assert.NotNull(property);
        property!.SetValue(vm, 12.5);

        Assert.Equal(0.0125, vm.Diameter, 12);
        Assert.Equal(12.5, (double)property.GetValue(vm)!, 12);
    }

    StirrupGroupVM NewVM()
    {
        var app = fixture.App;
        app.MaterialAreas.Clear();
        app.RefreshMaterialAreaLiveCollections();
        var anchor = new MaterialArea { Id = 3, Category = AreaCategory.Region, MaterialId = 2, Tag = "бетон" };
        anchor.Hull = new Contour([-0.15, 0.15, 0.15, -0.15, -0.15], [-0.25, -0.25, 0.25, 0.25, -0.25], "hull");
        anchor.SetWKT();
        app.MaterialAreas.Add(anchor);
        app.RefreshMaterialAreaLiveCollections();

        var area = new MaterialArea { Tag = "хомуты", Category = AreaCategory.Stirrups, MaterialId = 17 };
        var vm = new StirrupGroupVM(area, app);
        vm.SelectedAnchorArea = anchor;
        return vm;
    }
}

[CollectionDefinition("Stirrup group VM", DisableParallelization = true)]
public sealed class StirrupGroupVMCollection : ICollectionFixture<StirrupGroupVMFixture>
{
}

public sealed class StirrupGroupVMFixture
{
    public AppViewModel App { get; } = new(
        new LogService(), new NullFileDialogService(),
        Path.Combine(Path.GetTempPath(), $"opencs-stirrup-vm-{Guid.NewGuid():N}.db"));
}
