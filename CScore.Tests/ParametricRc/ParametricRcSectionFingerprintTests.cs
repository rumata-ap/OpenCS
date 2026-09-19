using CScore.ParametricRc;
using Xunit;

namespace CScore.Tests.ParametricRc;

public sealed class ParametricRcSectionFingerprintTests
{
    [Fact]
    public void Fingerprint_ignoresCalculationAndMeshState_butTracksGeometry()
    {
        var section = ParametricRcSectionGenerator.Generate(
            ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with
            { LowerRebar = ParametricLongitudinalLayer.Physical(2, 0.016, -0.21) }).Section;

        string original = ParametricRcSectionFingerprint.Compute(section, 1);
        var concrete = section.Areas[0];
        concrete.NX = 99;
        concrete.MeshMethod = MeshMethod.Ruppert;
        section.Areas[1].Fibers[0].Sig = 123.45;

        Assert.Equal(original, ParametricRcSectionFingerprint.Compute(section, 1));

        concrete.Hull!.X[0] += 0.001;
        concrete.Hull.Points = concrete.Hull.XYsToPoints();
        concrete.SetWKT();
        Assert.NotEqual(original, ParametricRcSectionFingerprint.Compute(section, 1));
    }
}
