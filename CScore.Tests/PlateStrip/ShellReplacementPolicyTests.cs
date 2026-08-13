using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class ShellReplacementPolicyTests
{
    [Fact]
    public void PlateStripBeamAnalogy_DefaultPolicy_IsDiagnosticOnly()
    {
        var analogy = new PlateStripBeamAnalogy();

        Assert.Equal(ShellReplacementPolicy.DiagnosticOnly, analogy.Policy);
    }

    [Fact]
    public void PlateStripBeamAnalogy_PolicyIsSettable()
    {
        var analogy = new PlateStripBeamAnalogy { Policy = ShellReplacementPolicy.ReplaceShellRegion };

        Assert.Equal(ShellReplacementPolicy.ReplaceShellRegion, analogy.Policy);
    }
}
