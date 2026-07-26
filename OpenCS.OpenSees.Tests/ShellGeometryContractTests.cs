using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public sealed class ShellGeometryContractTests
{
    [Fact]
    public void Frame_RejectsNonOrthogonalBasis()
    {
        var frame = new ShellFrame(
            new ShellVector3(1, 0, 0),
            new ShellVector3(Math.Sqrt(0.5), Math.Sqrt(0.5), 0),
            new ShellVector3(0, 0, 1));

        var ex = Assert.Throws<InvalidOperationException>(() => frame.Validate());

        Assert.Contains("ортогон", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Element_RequiresThreeNodesForT3()
    {
        var element = new NormalizedShellElement(
            10, ShellElementKind.ASDShellT3, [1, 2, 3, 4], 20, "s",
            ShellFrame.Identity, ShellIntegrationPolicy.Full, "fixture");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            element.Validate(new Dictionary<int, NormalizedShellNode>
            {
                [1] = new(1, 0, 0, 0, new bool[6], null),
                [2] = new(2, 1, 0, 0, new bool[6], null),
                [3] = new(3, 1, 1, 0, new bool[6], null),
                [4] = new(4, 0, 1, 0, new bool[6], null)
            }, new HashSet<int>([20])));

        Assert.Contains("3 уз", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Element_ReportsIntegrationPointCount()
    {
        var q4 = new NormalizedShellElement(
            10, ShellElementKind.ASDShellQ4, [1, 2, 3, 4], 20, "s",
            ShellFrame.Identity, ShellIntegrationPolicy.Full, null);
        var t3Reduced = new NormalizedShellElement(
            11, ShellElementKind.ASDShellT3, [1, 2, 3], 20, "s",
            ShellFrame.Identity, ShellIntegrationPolicy.Reduced, null);

        Assert.Equal(4, q4.IntegrationPointCount);
        Assert.Equal(1, t3Reduced.IntegrationPointCount);
    }
}
