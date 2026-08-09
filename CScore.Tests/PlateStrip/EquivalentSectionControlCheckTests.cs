using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class EquivalentSectionControlCheckTests
{
    [Fact]
    public void Run_ConstitutiveIntegrationSection_IsConsistentForArbitraryState()
    {
        var source = Source();
        var built = EquivalentSectionCalculator.Build(
            Analogy(), source, [source, source], ReductionPolicy.ConstitutiveIntegration, 2);
        var state = new BeamStrainState(0.001, 0.002, 0.003);

        var result = EquivalentSectionControlCheck.Run(built.Section, [source, source], state);

        Assert.True(result.IsCalculable);
        Assert.True(result.IsConsistent);
        Assert.Equal(0.0, result.Residual[0], 6);
        Assert.Equal(0.0, result.Residual[1], 6);
        Assert.Equal(0.0, result.Residual[2], 6);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Run_DirectUniaxialSection_ShowsResidualAgainstFullIntegration()
    {
        var source = Source();
        var built = EquivalentSectionCalculator.Build(
            Analogy(), source, [source, source], ReductionPolicy.DirectUniaxial, 2);
        var state = new BeamStrainState(0.0, 0.002, 0.0);

        var result = EquivalentSectionControlCheck.Run(built.Section, [source, source], state);

        Assert.True(result.IsCalculable);
        Assert.False(result.IsConsistent);
        Assert.Equal(0.08, result.Residual[0], 6);
        Assert.Equal(0.0, result.Residual[1], 6);
        Assert.Equal(0.0, result.Residual[2], 6);
        Assert.Contains(result.Diagnostics, d => d.Code == "equivalent_section_control_residual_exceeded");
    }

    [Fact]
    public void Run_ZeroState_IsTriviallyConsistent()
    {
        var source = Source();
        var built = EquivalentSectionCalculator.Build(
            Analogy(), source, [source, source], ReductionPolicy.ConstitutiveIntegration, 2);

        var result = EquivalentSectionControlCheck.Run(built.Section, [source, source], BeamStrainState.Zero);

        Assert.True(result.IsConsistent);
        Assert.Equal(new[] { 0.0, 0.0, 0.0 }, result.Direct);
        Assert.Equal(new[] { 0.0, 0.0, 0.0 }, result.Predicted);
    }

    [Fact]
    public void Run_RejectsMissingSection()
    {
        var result = EquivalentSectionControlCheck.Run(null, null, BeamStrainState.Zero);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "equivalent_section_control_missing_section");
    }

    [Fact]
    public void Run_RejectsWidthSourceCountMismatch()
    {
        var source = Source();
        var built = EquivalentSectionCalculator.Build(
            Analogy(), source, [source, source], ReductionPolicy.ConstitutiveIntegration, 2);

        var result = EquivalentSectionControlCheck.Run(built.Section, [source], BeamStrainState.Zero);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "equivalent_section_control_source_count_mismatch");
    }

    [Fact]
    public void Run_RejectsNonFiniteState()
    {
        var source = Source();
        var built = EquivalentSectionCalculator.Build(
            Analogy(), source, [source, source], ReductionPolicy.ConstitutiveIntegration, 2);

        var result = EquivalentSectionControlCheck.Run(
            built.Section, [source, source], new BeamStrainState(double.NaN, 0, 0));

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "equivalent_section_control_invalid_state");
    }

    [Fact]
    public void Run_RejectsInvalidTolerance()
    {
        var source = Source();
        var built = EquivalentSectionCalculator.Build(
            Analogy(), source, [source, source], ReductionPolicy.ConstitutiveIntegration, 2);

        var result = EquivalentSectionControlCheck.Run(
            built.Section, [source, source], BeamStrainState.Zero, relativeTolerance: -1.0);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "equivalent_section_control_invalid_tolerance");
    }

    [Fact]
    public void Run_RejectsSectionNotCalculable()
    {
        var source = Source();
        var built = EquivalentSectionCalculator.Build(
            Analogy(), source, [source, source], ReductionPolicy.ConstitutiveIntegration, 2);
        built.Section!.IsCalculable = false;

        var result = EquivalentSectionControlCheck.Run(built.Section, [source, source], BeamStrainState.Zero);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "equivalent_section_control_missing_section");
    }

    [Fact]
    public void Run_RejectsMissingStrip()
    {
        var source = Source();
        var built = EquivalentSectionCalculator.Build(
            Analogy(), source, [source, source], ReductionPolicy.ConstitutiveIntegration, 2);
        built.Section!.Strip = null!;

        var result = EquivalentSectionControlCheck.Run(built.Section, [source, source], BeamStrainState.Zero);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "equivalent_section_control_missing_section");
    }

    [Fact]
    public void Run_PreservesRequestedBeamStateOnFailure()
    {
        var state = new BeamStrainState(0.001, 0.002, 0.003);

        var result = EquivalentSectionControlCheck.Run(null, null, state);

        Assert.False(result.IsCalculable);
        Assert.Equal(state, result.BeamState);
    }

    static ConstantLinearPlateSectionResponse Source(double a00 = 1000.0, double d00 = 300.0)
    {
        var a = new double[3, 3];
        var b = new double[3, 3];
        var d = new double[3, 3];
        var ass = new double[2, 2];
        a[0, 0] = a00;
        b[0, 0] = 20.0;
        d[0, 0] = d00;
        a[1, 1] = 500.0;
        d[1, 1] = 100.0;
        ass[0, 0] = ass[1, 1] = 400.0;
        return new ConstantLinearPlateSectionResponse(a, b, d, ass, "source-fp");
    }

    static PlateStripBeamAnalogy Analogy(double width = 2.0) => new()
    {
        Id = "strip-1",
        SourceRegionId = 10,
        ExplicitWidthM = width,
        Fingerprint = "strip-fp",
        Geometry = new PlateStripGeometry { LengthM = 6.0 }
    };
}
