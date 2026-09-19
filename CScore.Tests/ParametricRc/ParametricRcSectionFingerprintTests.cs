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

    [Fact]
    public void FingerprintTracksCutSourceAndStepButIgnoresEntityIdsAndMeshFiberWkt()
    {
        var section = ParametricRcSectionGenerator.Generate(
            ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with
            {
                StirrupCuts = [new(ParametricStirrupZone.Body,
                    ParametricStirrupDirection.Vertical, 1, 0.008, 0.20, 0.03, 17)]
            }).Section;
        var original = ParametricRcSectionFingerprint.Compute(section, 1);
        var stirrup = section.Areas.Single(a => a.Category == AreaCategory.Stirrups);
        var element = stirrup.Stirrups.Single().Elements.Single();

        section.Id = 99;
        foreach (var area in section.Areas) area.Id += 100;
        section.Tag = "changed tag";
        section.Description = "changed description";
        section.Areas[0].Fibers.Add(new Fiber(0, 0)
        {
            TypeFiber = FiberType.poly, WKT = "POLYGON ((0 0, 1 0, 1 1, 0 0))", Area = 1
        });
        section.Areas[0].SigSp = 10;
        section.Areas[0].NX = 99;
        element.CenterlineContour.WKT = "LINESTRING (0 0, 0 1)";
        Assert.Equal(original, ParametricRcSectionFingerprint.Compute(section, 1));

        element.Source!.Position += 0.001;
        Assert.NotEqual(original, ParametricRcSectionFingerprint.Compute(section, 1));

        var sourceChanged = ParametricRcSectionFingerprint.Compute(section, 1);
        stirrup.Stirrups[0].SpacingM = 0.25;
        Assert.NotEqual(sourceChanged, ParametricRcSectionFingerprint.Compute(section, 1));
        Assert.NotEqual(sourceChanged, ParametricRcSectionFingerprint.Compute(section, 2));
    }
}
