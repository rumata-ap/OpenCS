using System;
using System.Collections.Generic;
using CScore;
using CScore.Planar;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public class RvePatchPreconditionsTests
{
    [Fact]
    public void FrameAligned_IdenticalFrames_ReturnsTrue()
    {
        Assert.True(RvePatchPreconditions.FrameAligned(Frame3D.Identity, Frame3D.Identity));
    }

    [Fact]
    public void FrameAligned_OppositeXY_ReturnsFalse()
    {
        // LocalX/LocalY одновременно инвертированы (разворот полосы на 180°), LocalZ то же.
        var flipped = new Frame3D(
            PlanarVector3.Zero,
            new PlanarVector3(-1, 0, 0),
            new PlanarVector3(0, -1, 0),
            new PlanarVector3(0, 0, 1));

        Assert.False(RvePatchPreconditions.FrameAligned(flipped, Frame3D.Identity));
    }

    [Fact]
    public void FrameAligned_RotatedAroundZ_ReturnsFalse()
    {
        var rotated = new Frame3D(
            PlanarVector3.Zero,
            new PlanarVector3(0, 1, 0),
            new PlanarVector3(-1, 0, 0),
            new PlanarVector3(0, 0, 1));

        Assert.False(RvePatchPreconditions.FrameAligned(rotated, Frame3D.Identity));
    }

    [Fact]
    public void FrameAligned_TooLargeTolerance_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RvePatchPreconditions.FrameAligned(Frame3D.Identity, Frame3D.Identity, tol: 0.5));
    }

    [Fact]
    public void FrameAligned_NonPositiveTolerance_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RvePatchPreconditions.FrameAligned(Frame3D.Identity, Frame3D.Identity, tol: 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RvePatchPreconditions.FrameAligned(Frame3D.Identity, Frame3D.Identity, tol: double.NaN));
    }

    [Fact]
    public void AllRebarAnglesZero_AllZero_ReturnsTrue()
    {
        var layers = new List<PlateRebarLayer>
        {
            new() { Asx = 0.001, Asy = 0.001, Angle = 0.0 },
            new() { Asx = 0.0, Asy = 0.002, Angle = 0.0 },
        };
        Assert.True(RvePatchPreconditions.AllRebarAnglesZero(layers));
    }

    [Fact]
    public void AllRebarAnglesZero_OneNonZero_ReturnsFalse()
    {
        var layers = new List<PlateRebarLayer>
        {
            new() { Asx = 0.001, Angle = 0.0 },
            new() { Asx = 0.001, Angle = 45.0 },
        };
        Assert.False(RvePatchPreconditions.AllRebarAnglesZero(layers));
    }
}
