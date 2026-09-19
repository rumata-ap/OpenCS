using CScore;
using CScore.ParametricRc;
using CScore.Sp63Shear;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

public sealed class ParametricRcSectionVMTests
{
    [Fact]
    public void BuildDefinitionConvertsMillimetresToMetres()
    {
        var vm = new ParametricRcSectionVM
        {
            Shape = ParametricRcShape.Rectangle,
            WidthMm = 300,
            HeightMm = 500
        };

        var definition = vm.BuildDefinition();

        Assert.Equal(0.30, definition.WidthM, 12);
        Assert.Equal(0.50, definition.HeightM, 12);
    }

    [Fact]
    public void IdealizedLayerUsesDeclaredAreaAndCircleDisablesStirrups()
    {
        var vm = new ParametricRcSectionVM
        {
            LowerRebarEnabled = true,
            LowerRebarIdealized = true,
            LowerRebarAreaMm2 = 1200,
            LowerRebarDiameterMm = 20,
            LowerRebarCoordinateMm = -210,
            Shape = ParametricRcShape.Rectangle
        };

        var definition = vm.BuildDefinition();
        Assert.Equal(0.0012, definition.LowerRebar!.AreaM2, 12);
        Assert.Equal(RebarRepresentation.IdealizedLayer,
            vm.Preview.Section.Areas.Single(a => a.RebarRepresentation == RebarRepresentation.IdealizedLayer).RebarRepresentation);

        vm.Shape = ParametricRcShape.Circle;
        Assert.False(vm.CanUseStirrups);
        Assert.Empty(vm.BuildDefinition().StirrupCuts);
    }

    [Fact]
    public void LoadingDefinitionPreservesRingInnerDiameter()
    {
        var vm = new ParametricRcSectionVM();
        vm.LoadDefinition(ParametricRcSectionDefinition.Annulus(0.60, 0.32) with
        {
            PolarRebar = new ParametricPolarRebar(7, 0.016, 0.24)
        });

        Assert.Equal(320, vm.InnerDiameterMm, 10);
        Assert.True(vm.Preview.Diagnostics.Count == 0);
    }

    [Fact]
    public void AdditionalCutSetsAreIncludedAndDifferentStepsProduceWarning()
    {
        var vm = new ParametricRcSectionVM
        {
            StirrupCount = 1,
            StirrupMaterialId = 17
        };
        vm.AdditionalStirrupCuts.Add(new ParametricStirrupCutSet(
            ParametricStirrupZone.Body, ParametricStirrupDirection.Horizontal,
            1, 0.008, 0.25, 0.03, 18));

        Assert.Equal(2, vm.BuildDefinition().StirrupCuts.Count);
        Assert.NotEmpty(vm.StirrupStepWarning);
        Assert.True(vm.Qsw > 0);
        Assert.Equal(vm.Preview.Section.Areas
            .Where(a => a.Category == AreaCategory.Stirrups)
            .SelectMany(a => a.Stirrups)
            .SelectMany(g => g.Elements)
            .Sum(e => StirrupResolver.BranchArea(e,
                e.Source?.Direction == StirrupCutDirection.Horizontal
                    ? ShearPlane.Vx : ShearPlane.Vy)), vm.Asw, 12);
    }

    [Fact]
    public void ApplicabilityHintsFollowTheSelectedShape()
    {
        var vm = new ParametricRcSectionVM { Shape = ParametricRcShape.Tee };

        Assert.True(vm.ShowTeeApplicability);
        Assert.False(vm.ShowRoundApplicability);

        vm.Shape = ParametricRcShape.Annulus;

        Assert.False(vm.ShowTeeApplicability);
        Assert.True(vm.ShowRoundApplicability);
        Assert.False(vm.CanUseStirrups);
    }
}
