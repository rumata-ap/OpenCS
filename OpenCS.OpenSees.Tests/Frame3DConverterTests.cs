using CScore.Planar;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Structural;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public class Frame3DConverterTests
{
    [Fact]
    public void ToShellFrame_Identity_MapsAxisByAxis()
    {
        ShellFrame result = Frame3D.Identity.ToShellFrame();

        Assert.Equal(new ShellVector3(1, 0, 0), result.Ex);
        Assert.Equal(new ShellVector3(0, 1, 0), result.Ey);
        Assert.Equal(new ShellVector3(0, 0, 1), result.Normal);
    }

    [Fact]
    public void ToShellFrame_RotatedFrame_PreservesComponentsAndPassesValidation()
    {
        var frame = new Frame3D(
            PlanarVector3.Zero,
            new PlanarVector3(0, 1, 0),
            new PlanarVector3(-1, 0, 0),
            new PlanarVector3(0, 0, 1));
        frame.Validate();

        ShellFrame result = frame.ToShellFrame();

        Assert.Equal(new ShellVector3(0, 1, 0), result.Ex);
        Assert.Equal(new ShellVector3(-1, 0, 0), result.Ey);
        Assert.Equal(new ShellVector3(0, 0, 1), result.Normal);
        result.Validate();
    }
}
