using System.Collections.Generic;
using CScore;
using CScore.PlateRebar;
using Xunit;

namespace CScore.Tests.PlateRebar;

public class PlateRebarLayoutFingerprintTests
{
    [Fact]
    public void Compute_IdenticalLayers_ProducesSameFingerprint()
    {
        var a = new List<PlateRebarLayer> { new() { Asx = 0.001, Zsx = 0.05 } };
        var b = new List<PlateRebarLayer> { new() { Asx = 0.001, Zsx = 0.05 } };

        Assert.Equal(PlateRebarLayoutFingerprint.Compute(a), PlateRebarLayoutFingerprint.Compute(b));
    }

    [Fact]
    public void Compute_DifferentAsx_ProducesDifferentFingerprint()
    {
        var a = new List<PlateRebarLayer> { new() { Asx = 0.001 } };
        var b = new List<PlateRebarLayer> { new() { Asx = 0.002 } };

        Assert.NotEqual(PlateRebarLayoutFingerprint.Compute(a), PlateRebarLayoutFingerprint.Compute(b));
    }

    [Fact]
    public void Compute_DifferentAngle_ProducesDifferentFingerprint()
    {
        var a = new List<PlateRebarLayer> { new() { Asx = 0.001, Zsx = 0.05, Angle = 0.0 } };
        var b = new List<PlateRebarLayer> { new() { Asx = 0.001, Zsx = 0.05, Angle = 30.0 } };

        Assert.NotEqual(PlateRebarLayoutFingerprint.Compute(a), PlateRebarLayoutFingerprint.Compute(b));
    }

    [Fact]
    public void Compute_DifferentFace_ProducesDifferentFingerprint()
    {
        var a = new List<PlateRebarLayer> { new() { Asx = 0.001, Zsx = 0.05, Face = RebarFace.PlusN } };
        var b = new List<PlateRebarLayer> { new() { Asx = 0.001, Zsx = 0.05, Face = RebarFace.MinusN } };

        Assert.NotEqual(PlateRebarLayoutFingerprint.Compute(a), PlateRebarLayoutFingerprint.Compute(b));
    }

    [Fact]
    public void Compute_EmptyList_IsStableAndNonEmpty()
    {
        string fp1 = PlateRebarLayoutFingerprint.Compute(new List<PlateRebarLayer>());
        string fp2 = PlateRebarLayoutFingerprint.Compute(new List<PlateRebarLayer>());

        Assert.Equal(fp1, fp2);
        Assert.False(string.IsNullOrEmpty(fp1));
    }
}
