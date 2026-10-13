using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

/// <summary>Срез 8b: GJ, GAy/GAz и политика EnergyEquivalent на изотропной упругой плите.</summary>
public sealed class EquivalentSectionTimoshenkoTests
{
    const double E = 3.0e7;   // кПа
    const double Nu = 0.2;
    const double H = 0.2;     // м
    const double B = 1.5;     // ширина полосы, м
    static readonly double G = E / (2.0 * (1.0 + Nu));

    static ConstantLinearPlateSectionResponse Isotropic(double e = E, double nu = Nu, double h = H)
    {
        double c = e * h / (1.0 - nu * nu);
        var a = new double[3, 3]
        {
            { c, c * nu, 0 },
            { c * nu, c, 0 },
            { 0, 0, c * (1.0 - nu) / 2.0 }
        };
        var d = new double[3, 3];
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
            d[i, j] = a[i, j] * h * h / 12.0;
        var ass = new double[2, 2];
        ass[0, 0] = ass[1, 1] = 5.0 / 6.0 * e / (2.0 * (1.0 + nu)) * h;
        return new ConstantLinearPlateSectionResponse(a, new double[3, 3], d, ass, "iso");
    }

    static PlateStripBeamAnalogy Analogy() => new()
    {
        Id = "strip",
        SourceRegionId = 1,
        ExplicitWidthM = B,
        Fingerprint = "fp",
        Geometry = new PlateStripGeometry { LengthM = 6.0 }
    };

    static EquivalentSection Build(ReductionPolicy policy, StripBeamFormulation f)
    {
        var s = Isotropic();
        var r = EquivalentSectionCalculator.Build(Analogy(), s, [s, s, s], policy, 3, f);
        Assert.True(r.IsCalculable);
        return r.Section!;
    }

    [Theory]
    [InlineData(ReductionPolicy.DirectUniaxial)]
    [InlineData(ReductionPolicy.ConstitutiveIntegration)]
    public void Timoshenko_TorsionAndShear_FromPlateTangent(ReductionPolicy policy)
    {
        var section = Build(policy, StripBeamFormulation.Timoshenko);

        Assert.Equal(G * B * H * H * H / 3.0, section.TorsionalStiffness, 6);
        Assert.Equal(5.0 / 6.0 * G * H * B, section.ShearStiffnessGAz, 6);
        Assert.Equal(section.ShearStiffnessGAz, section.ShearStiffnessGAy, 12);
        Assert.Equal(StripBeamFormulation.Timoshenko, section.BeamFormulation);
    }

    [Fact]
    public void EulerBernoulli_LeavesTorsionAndShearZero()
    {
        var section = Build(ReductionPolicy.ConstitutiveIntegration, StripBeamFormulation.EulerBernoulli);

        Assert.Equal(0.0, section.TorsionalStiffness);
        Assert.Equal(0.0, section.ShearStiffnessGAz);
        Assert.Equal(0.0, section.ShearStiffnessGAy);
    }

    [Fact]
    public void ConstitutiveIntegration_KeepsLateralStrainsSuppressed()
    {
        var section = Build(ReductionPolicy.ConstitutiveIntegration, StripBeamFormulation.Timoshenko);
        double ea = E * H * B / (1.0 - Nu * Nu);

        Assert.Equal(ea, section.EA, 4);
        Assert.Equal(E * H * H * H / 12.0 / (1.0 - Nu * Nu) * B, section.EIy, 4);
    }

    [Theory]
    [InlineData(StripBeamFormulation.EulerBernoulli)]
    [InlineData(StripBeamFormulation.Timoshenko)]
    public void EnergyEquivalent_IsolatedStrip_HasFreeLateralStrains(StripBeamFormulation f)
    {
        var section = Build(ReductionPolicy.EnergyEquivalent, f);

        Assert.Equal(E * H * B, section.EA, 4);
        Assert.Equal(E * H * H * H / 12.0 * B, section.EIy, 4);
        Assert.Equal(E * H * B * B * B / 12.0, section.EIz, 4);
        Assert.Equal(0.0, section.BeamTangent[0, 1], 6);
    }

    [Fact]
    public void EnergyEquivalent_Timoshenko_KeepsTorsion()
    {
        var section = Build(ReductionPolicy.EnergyEquivalent, StripBeamFormulation.Timoshenko);

        Assert.Equal(G * B * H * H * H / 3.0, section.TorsionalStiffness, 6);
        Assert.Equal(5.0 / 6.0 * G * H * B, section.ShearStiffnessGAz, 6);
    }

    [Fact]
    public void EnergyEquivalent_BendingRatioToConstitutiveIntegration_Is_OneMinusNuSquared()
    {
        var ci = Build(ReductionPolicy.ConstitutiveIntegration, StripBeamFormulation.EulerBernoulli);
        var ee = Build(ReductionPolicy.EnergyEquivalent, StripBeamFormulation.EulerBernoulli);

        Assert.Equal(1.0 - Nu * Nu, ee.EIy / ci.EIy, 8);
        Assert.Equal(1.0 - Nu * Nu, ee.EA / ci.EA, 8);
    }

    [Fact]
    public void EnergyEquivalent_ReportsSingularCondensedBlock()
    {
        var zero = new ConstantLinearPlateSectionResponse(
            new double[3, 3] { { 1000, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 } },
            new double[3, 3],
            new double[3, 3] { { 300, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 } },
            new double[2, 2], "singular");
        var r = EquivalentSectionCalculator.Build(Analogy(), zero, [zero, zero], ReductionPolicy.EnergyEquivalent, 2);

        Assert.False(r.IsCalculable);
        Assert.Contains(r.Diagnostics, d => d.Code == "equivalent_section_energy_singular");
    }

    [Fact]
    public void EnergyEquivalent_EnergyEqualsMinimumOfPlateEnergy()
    {
        // ½·ε₀ᵀ·K·ε₀ при ε₀ = ε_x единичной полосы равно минимуму плитной энергии по ε_y, γ_xy, κ_y.
        var s = Isotropic();
        var r = EquivalentSectionCalculator.Build(Analogy(), s, [s, s], ReductionPolicy.EnergyEquivalent, 2);
        double beamEnergy = 0.5 * r.Section!.EA * 1e-6;

        double best = double.MaxValue;
        for (double ey = -2e-3; ey <= 2e-3; ey += 1e-5)
        {
            var f = s.Forces(new ShellStrainState(1e-3, ey, 0, 0, 0, 0));
            double energy = 0.5 * (f.Nx * 1e-3 + f.Ny * ey) * B;
            best = Math.Min(best, energy);
        }
        Assert.Equal(best, beamEnergy, 3);
    }
}
