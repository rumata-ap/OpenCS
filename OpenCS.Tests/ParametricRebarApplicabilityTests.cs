using CScore;
using CScore.ParametricRc;
using CScore.Sp63.Normal;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public sealed class ParametricRebarApplicabilityTests
{
    [Fact]
    public void IdealizedLayerRejectsOtherKindsWithTypedReason()
    {
        var section = ParametricRcSectionGenerator.Generate(
            ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with
            {
                LowerRebar = ParametricLongitudinalLayer.Idealized(
                    0.0012, 0.020, -0.21, IdealizedRebarAxis.Mx)
            }).Section;

        var result = ParametricRebarApplicability.Evaluate(
            section, "shear_inclined", new LoadItem { N = 0, Mx = 0, My = 0 });

        Assert.False(result.IsApplicable);
        Assert.Equal(ParametricRebarApplicabilityReason.UnsupportedTaskKind, result.Reason);
    }

    [Fact]
    public void IdealizedLayerAllowsSp63DeflectionOnlyOnItsAxis()
    {
        var section = ParametricRcSectionGenerator.Generate(
            ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with
            {
                LowerRebar = ParametricLongitudinalLayer.Idealized(
                    0.0012, 0.020, -0.21, IdealizedRebarAxis.Mx)
            }).Section;

        var supported = ParametricRebarApplicability.Evaluate(
            section, "sp63_deflection", new LoadItem { Mx = 10 }, Sp63NormalAxis.Mx);
        var wrongAxis = ParametricRebarApplicability.Evaluate(
            section, "sp63_deflection", new LoadItem { My = 10 }, Sp63NormalAxis.My);

        Assert.True(supported.IsApplicable);
        Assert.Equal(ParametricRebarApplicabilityReason.AxisMismatch, wrongAxis.Reason);
    }

    [Fact]
    public void IdealizedLayerRejectsMomentAboutOtherAxis()
    {
        var section = ParametricRcSectionGenerator.Generate(
            ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with
            {
                LowerRebar = ParametricLongitudinalLayer.Idealized(
                    0.0012, 0.020, -0.21, IdealizedRebarAxis.Mx)
            }).Section;

        var result = ParametricRebarApplicability.Evaluate(
            section, "strain_state", new LoadItem { My = 10 });

        Assert.False(result.IsApplicable);
        Assert.Equal(ParametricRebarApplicabilityReason.BiaxialLoad, result.Reason);
    }

    [Fact]
    public void SingleStrainStateReturnsNotApplicableBeforeSolverForBiaxialLoad()
    {
        var section = ParametricRcSectionGenerator.Generate(
            ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with
            {
                LowerRebar = ParametricLongitudinalLayer.Idealized(
                    0.0012, 0.020, -0.21, IdealizedRebarAxis.Mx)
            }).Section;

        var result = new StrainStateHandler().Run(
            new CalcTask { Id = 2, Kind = "strain_state" }, section,
            new LoadItem { Mx = 10, My = 1 }, CalcSettings.Default);

        Assert.Equal("not_applicable", result.Status);
        Assert.Contains("idealized_rebar_biaxial_load", result.DataJson);
    }

    [Fact]
    public void TaskRunnerKeepsNullSectionErrorContractForNonParametricTasks()
    {
        var result = TaskRunner.Run(
            new CalcTask { Kind = "shell_layered_sls" }, null!, new LoadItem(),
            CalcSettings.Default);

        Assert.Equal("error", result.Status);
        Assert.DoesNotContain("Unknown task kind", result.DataJson,
            StringComparison.Ordinal);
    }
}
