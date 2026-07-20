using CScore;
using OpenCS.OpenSees.CScore;

namespace OpenCS.OpenSees.Tests;

public class FemSectionStiffnessTests
{
    [Fact]
    public void FromGeoProps_SingleMaterial_ComputesEffectiveEandI()
    {
        // A=0.02 м², E=3e10 Па, Ix=2e-4, Iy=5e-4
        var gp = new GeoProps
        {
            A = 0.02, EA = 0.02 * 3e10,
            Ix = 2e-4, EIx = 2e-4 * 3e10,
            Iy = 5e-4, EIy = 5e-4 * 3e10
        };
        var s = FemSectionStiffness.FromGeoProps(gp);
        Assert.Equal(3e10, s.E, 3);
        Assert.Equal(0.02, s.A, 12);
        Assert.Equal(5e-4, s.Iy, 12);   // Iy_arg ← EIy/E
        Assert.Equal(2e-4, s.Iz, 12);   // Iz_arg ← EIx/E
    }

    [Fact]
    public void FromGeoProps_MultiMaterial_KeepsReducedStiffnessExact()
    {
        // Разные E: проверяем, что E*A=EA и E*Iy=EIy точно
        var gp = new GeoProps
        {
            A = 0.01, EA = 2.5e8,
            Ix = 1e-4, EIx = 3.0e6,
            Iy = 1e-4, EIy = 4.0e6
        };
        var s = FemSectionStiffness.FromGeoProps(gp);
        Assert.Equal(gp.EA, s.E * s.A, 1);
        Assert.Equal(gp.EIy, s.E * s.Iy, 1);
        Assert.Equal(gp.EIx, s.E * s.Iz, 1);
    }

    [Fact]
    public void FromGeoProps_ZeroArea_Throws()
    {
        var gp = new GeoProps { A = 0, EA = 0 };
        Assert.Throws<InvalidOperationException>(() => FemSectionStiffness.FromGeoProps(gp));
    }
}
