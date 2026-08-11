using System;
using CScore;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public class ShellMeshPatchStateBoundsTests
{
    [Fact]
    public void Validate_WithinBounds_DoesNotThrow()
    {
        var bounds = new ShellMeshPatchStateBounds(EpsGammaBoundAbs: 1e-3, KappaBoundAbs: 0.05);
        var state = new ShellStrainState(5e-4, -5e-4, 2e-4, 0.02, -0.02, 0.01);

        bounds.Validate(state);
    }

    [Fact]
    public void Validate_EpsOutOfBounds_Throws()
    {
        var bounds = new ShellMeshPatchStateBounds(EpsGammaBoundAbs: 1e-3, KappaBoundAbs: 0.05);
        var state = new ShellStrainState(2e-3, 0, 0, 0, 0, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => bounds.Validate(state));
    }

    [Fact]
    public void Validate_KappaOutOfBounds_Throws()
    {
        var bounds = new ShellMeshPatchStateBounds(EpsGammaBoundAbs: 1e-3, KappaBoundAbs: 0.05);
        var state = new ShellStrainState(0, 0, 0, 0.1, 0, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => bounds.Validate(state));
    }

    [Fact]
    public void Constructor_NonPositiveBound_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShellMeshPatchStateBounds(0.0, 0.05));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShellMeshPatchStateBounds(1e-3, -1.0));
    }
}
