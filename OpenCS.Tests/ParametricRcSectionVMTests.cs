using CScore;
using CScore.ParametricRc;
using CScore.Sp63Shear;
using OpenCS.ViewModels;
using OpenCS.Views.Helpers;
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
            LowerRebarOffsetMm = 40,
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
            Tag = "Кольцо K1",
            PolarRebar = new ParametricPolarRebar(7, 0.016, 0.24)
        });

        Assert.Equal(320, vm.InnerDiameterMm, 10);
        Assert.Equal("Кольцо K1", vm.Tag);
        Assert.True(vm.Preview.Diagnostics.Count == 0);

        vm.LoadDefinition(ParametricRcSectionDefinition.Circle(0.60));
        Assert.Contains("600", vm.Tag, StringComparison.Ordinal);
    }

    [Fact]
    public void AdditionalCutSetsAreIncludedAndDifferentStepsProduceWarning()
    {
        var vm = new ParametricRcSectionVM
        {
            StirrupsEnabled = true,
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

    [Fact]
    public void RoundShapeHidesFaceRebarAndDisablesHeight()
    {
        var vm = new ParametricRcSectionVM { Shape = ParametricRcShape.Circle };

        Assert.False(vm.ShowLayerRebarFields);
        Assert.False(vm.IsHeightEnabled);
        Assert.True(vm.ShowPolarFields);

        vm.Shape = ParametricRcShape.Rectangle;

        Assert.True(vm.ShowLayerRebarFields);
        Assert.True(vm.IsHeightEnabled);
    }

    [Fact]
    public void RingPreviewConvertsInnerDiameterToMetres()
    {
        var points = ParametricRcPreviewControl.CirclePointsInMeters(320, 40);

        Assert.Equal(0.32, points.Max(p => p.X) - points.Min(p => p.X), 12);
        Assert.Equal(0.32, points.Max(p => p.Y) - points.Min(p => p.Y), 12);
    }

    [Fact]
    public void RingPreviewInnerDimensionUsesHoleEdges()
    {
        var endpoints = ParametricRcPreviewControl.GetInnerDiameterEndpointsInMeters(320);

        Assert.Equal(-0.16, endpoints.Left.X, 12);
        Assert.Equal(0, endpoints.Left.Y, 12);
        Assert.Equal(0.16, endpoints.Right.X, 12);
        Assert.Equal(0, endpoints.Right.Y, 12);

        var lowerEndpoints = ParametricRcPreviewControl.GetInnerDiameterEndpointsInMeters(320, -0.30);
        Assert.Equal(-0.30, lowerEndpoints.Left.Y, 12);
        Assert.Equal(-0.30, lowerEndpoints.Right.Y, 12);
    }

    [Fact]
    public void StirrupPreviewCreatesSegmentsForEnabledRectangularCuts()
    {
        var vm = new ParametricRcSectionVM
        {
            Shape = ParametricRcShape.Rectangle,
            StirrupsEnabled = true,
            StirrupCount = 2,
            StirrupCoverMm = 30,
            StirrupDirection = ParametricStirrupDirection.Vertical
        };

        var segments = ParametricRcPreviewControl.GetStirrupPreviewSegments(vm.BuildDefinition());

        Assert.Equal(2, segments.Count);
        Assert.All(segments, segment =>
        {
            Assert.Equal(segment.X0, segment.X1, 12);
            Assert.NotEqual(segment.Y0, segment.Y1);
            Assert.Equal(0.44, segment.Y1 - segment.Y0, 12);
        });
    }

    [Fact]
    public void DefaultTagDescribesCurrentGeometryUntilUserOverridesIt()
    {
        var vm = new ParametricRcSectionVM
        {
            Shape = ParametricRcShape.Rectangle,
            WidthMm = 300,
            HeightMm = 500
        };

        Assert.Contains("300", vm.Tag, StringComparison.Ordinal);
        Assert.Contains("500", vm.Tag, StringComparison.Ordinal);

        vm.WidthMm = 350;

        Assert.Contains("350", vm.Tag, StringComparison.Ordinal);
        Assert.DoesNotContain("300", vm.Tag, StringComparison.Ordinal);

        vm.Tag = "Балка B1";
        vm.HeightMm = 700;

        Assert.Equal("Балка B1", vm.Tag);
        Assert.Equal("Балка B1", vm.BuildDefinition().Tag);
    }

    [Fact]
    public void MaterialSelectionsAreStoredAndStirrupsCanBeDisabled()
    {
        var concrete = new Material { Id = 11, Tag = "B25" };
        var rebar = new Material { Id = 22, Tag = "A500C" };
        var vm = new ParametricRcSectionVM([concrete], [rebar])
        {
            LowerRebarEnabled = true,
            LowerRebarCount = 2,
            LowerRebarDiameterMm = 16,
            LowerRebarOffsetMm = 40,
            StirrupsEnabled = true,
            StirrupCount = 2
        };

        Assert.Equal(22, vm.StirrupMaterialId);

        var definition = vm.BuildDefinition();

        Assert.Equal(11, definition.ConcreteMaterialId);
        Assert.Equal(22, definition.LongitudinalMaterialId);
        Assert.Equal(11, vm.Preview.Section.Areas.Single(a => a.Category == AreaCategory.Region).MaterialId);
        Assert.Equal(22, vm.Preview.Section.Areas.Single(a => a.Category == AreaCategory.RebarGroup).MaterialId);
        Assert.NotEmpty(definition.StirrupCuts);

        vm.StirrupsEnabled = false;

        Assert.Empty(vm.BuildDefinition().StirrupCuts);
    }

    [Fact]
    public void EmptyMaterialCatalogBlocksSaveAndDiagnosticsHaveReadableText()
    {
        var vm = new ParametricRcSectionVM([], []);

        Assert.False(vm.CanSave);
        Assert.Contains("ParametricRcMissingConcreteMaterial", vm.DiagnosticsText, StringComparison.Ordinal);
        Assert.DoesNotContain("System.String", vm.DiagnosticsText, StringComparison.Ordinal);
    }

    [Fact]
    public void RebarOffsetsAreConvertedFromCorrespondingFacesToCentroidCoordinates()
    {
        var vm = new ParametricRcSectionVM
        {
            Shape = ParametricRcShape.Rectangle,
            WidthMm = 300,
            HeightMm = 500,
            LowerRebarEnabled = true,
            LowerRebarOffsetMm = 40,
            UpperRebarEnabled = true,
            UpperRebarOffsetMm = 50
        };

        var definition = vm.BuildDefinition();

        Assert.Equal(-0.21, definition.LowerRebar!.CoordinateM, 12);
        Assert.Equal(0.20, definition.UpperRebar!.CoordinateM, 12);
    }

    [Fact]
    public void IdealizedLayerSpanFollowsTheTeeContourAtItsElevation()
    {
        var points = TemplatePoints.TeePoints(0.60, 0.50, 0.16, 0.10);
        double bottom = points.Min(p => p.Y);
        double top = points.Max(p => p.Y);

        var lowerSpan = ParametricRcPreviewControl.GetIdealizedLineSpan(
            points, bottom + 0.040, IdealizedRebarAxis.Mx, 0.030);
        var upperSpan = ParametricRcPreviewControl.GetIdealizedLineSpan(
            points, top - 0.040, IdealizedRebarAxis.Mx, 0.030);

        Assert.Equal(0.10, lowerSpan.Max - lowerSpan.Min, 12);
        Assert.Equal(0.54, upperSpan.Max - upperSpan.Min, 12);
    }
}
